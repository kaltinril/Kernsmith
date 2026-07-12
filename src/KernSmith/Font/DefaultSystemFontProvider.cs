using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using KernSmith.Font.Models;
using Microsoft.Win32;

namespace KernSmith.Font;

/// <summary>
/// Default implementation of <see cref="ISystemFontProvider"/> that scans
/// platform-specific font directories and uses <see cref="TtfParser"/> to
/// extract font metadata.
/// </summary>
public sealed class DefaultSystemFontProvider : ISystemFontProvider
{
    private static readonly string[] FontExtensions = { ".ttf", ".otf", ".ttc" };

    private readonly object _lock = new();
    internal List<SystemFontInfo>? _cachedFonts;

    // Lazy per-family-name cache populated incrementally by LoadFont, distinct
    // from _cachedFonts (the full enumerated list backing GetInstalledFonts()).
    // Internal so tests can seed/inspect entries directly instead of depending
    // on OS registry/font-directory state.
    private readonly object _resolvedFontCacheLock = new();
    internal readonly Dictionary<string, SystemFontInfo> _resolvedFontCache = new(StringComparer.OrdinalIgnoreCase);

    // Instance-level (not static) so tests can substitute it per-provider to count
    // reads without any cross-test interference from parallel test execution.
    internal Func<string, byte[]> ReadFileBytes { get; set; } = File.ReadAllBytes;

    // Test seam: when set, overrides WellKnownFontSeeds with a caller-supplied
    // family -> fully-resolved candidate path list, so tests can exercise seeding
    // deterministically against a fixture directory instead of the real OS's
    // actual font paths.
    internal Dictionary<string, string[]>? SeedTableOverride { get; set; }

    // Tracks families we've already attempted to seed this instance, so a missed
    // guess isn't retried on every call — one bounded attempt per family, then
    // fall through to the normal resolution chain for the rest of this
    // provider's lifetime.
    private readonly object _seedAttemptedLock = new();
    private readonly HashSet<string> _seedAttempted = new(StringComparer.OrdinalIgnoreCase);

    // Test seam: when set, overrides GetFontDirectories() for the heuristic
    // filename-match tier, so tests can point it at a fixture directory instead of
    // the real OS's actual font directories.
    internal List<string>? FontDirectoriesOverride { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<SystemFontInfo> GetInstalledFonts()
    {
        if (_cachedFonts is not null)
            return _cachedFonts;

        lock (_lock)
        {
            if (_cachedFonts is not null)
                return _cachedFonts;

            _cachedFonts = ScanSystemFonts();
            return _cachedFonts;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolves a family through four tiers, in order, each cheaper than the next fallback
    /// so the common case (a family already seen, or a well-known one) never pays for the
    /// expensive tiers at all:
    /// <list type="number">
    /// <item><description>
    /// <b>Cache / seed</b> (<see cref="TryGetValidCachedFont"/>, no-style requests only) —
    /// a hit against <c>_resolvedFontCache</c> from a prior call, a consumer-supplied hint
    /// (<see cref="AddResolvedFontHint"/>, exposed via <see cref="KernSmith.BmFont.HintFontLocation"/>),
    /// or a lazy one-shot attempt against <see cref="WellKnownFontSeeds"/>'s best-guess
    /// candidate paths (<see cref="TryResolveFromSeed"/>) the first time a family is
    /// requested. Every candidate — cached, hinted, or seeded — is validated identically via
    /// <see cref="IsCacheEntryValidCore"/> before being trusted.
    /// </description></item>
    /// <item><description>
    /// <b>Windows registry</b> (<see cref="TryLoadFontFromRegistry"/>) — fast, Windows-only;
    /// no-op elsewhere. If the registry confirms the family exists but not the requested
    /// style, resolution stops here and returns null (no separate file for that style).
    /// </description></item>
    /// <item><description>
    /// <b>Heuristic filename match</b> (<see cref="TryHeuristicFilenameMatch"/>) — narrows
    /// candidates by filename first (cheap directory enumeration, no parsing), then verifies
    /// with a real parse. A filename hint that fails to verify is a bounded, definitive miss
    /// (does not escalate); only a total absence of filename-narrowed candidates falls
    /// through to the next tier.
    /// </description></item>
    /// <item><description>
    /// <b>Full scan</b> (<see cref="GetInstalledFonts"/>) — the correctness backstop: every
    /// installed font is enumerated and parsed. Always eventually finds a real, installed
    /// font regardless of whether the earlier tiers' guesses were right.
    /// </description></item>
    /// </list>
    /// A successful no-style resolution from any of tiers 2-4 is written back into
    /// <c>_resolvedFontCache</c> via <see cref="CacheResolvedFont"/>, so the next no-style
    /// request for that family is a pure tier-1 cache hit regardless of which tier
    /// originally resolved it.
    /// </remarks>
    public FontLoadResult? LoadFont(string familyName, string? styleName = null)
    {
        // Style-aware caching is out of scope for now — the resolved-font cache
        // is keyed by family name only, so only consult/populate it when no
        // specific style was requested.
        if (styleName is null && TryGetValidCachedFont(familyName, out FontLoadResult? cachedResult))
        {
            return cachedResult;
        }

        // Try fast registry-based lookup on Windows before falling back to
        // the full font directory scan, which can take several seconds.
        if (TryLoadFontFromRegistry(familyName, styleName, out FontLoadResult? registryResult, out bool familyFound, out SystemFontInfo? registryInfo))
        {
            if (styleName is null && registryInfo is not null)
            {
                CacheResolvedFont(familyName, registryInfo);
            }
            return registryResult;
        }

        // If the registry confirmed the family exists but the specific style
        // doesn't, there's no separate font file for that style — skip the
        // expensive full scan and return null so the caller uses synthetic styling.
        if (familyFound && styleName != null)
        {
            return null;
        }

        // Heuristic filename-targeted parse: narrow candidates by filename first (cheap),
        // then verify with a real parse, before paying for a full directory enumeration +
        // parse of every installed font. A filename match that fails to parse to the
        // requested family (e.g. "Helvetica" matching "HelveticaNeue-Bold.ttf") is treated
        // as a definitive miss, not a signal to escalate — only a total absence of
        // filename-narrowed candidates falls through to the full scan.
        var fontDirs = FontDirectoriesOverride ?? GetFontDirectories();
        if (TryHeuristicFilenameMatch(familyName, styleName, fontDirs, out FontLoadResult? heuristicResult))
        {
            return heuristicResult;
        }

        Trace.TraceInformation(
            $"KernSmith: '{familyName}' requires a full font directory scan — the most expensive " +
            "resolution path. Consider BmFont.HintFontLocation or BmFont.RegisterFont for this family.");

        var fonts = GetInstalledFonts();

        // Find all fonts matching the family name (case-insensitive)
        var matches = new List<SystemFontInfo>();
        foreach (var font in fonts)
        {
            if (string.Equals(font.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                matches.Add(font);
        }

        if (matches.Count == 0)
            return null;

        var best = SelectBestMatch(matches, styleName);

        // When a specific style was requested but not found, return null
        // so the caller knows it needs synthetic bold/italic. Only fall back
        // to the first match when no style was requested.
        if (best == null)
        {
            return null;
        }

        if (styleName is null)
        {
            CacheResolvedFont(familyName, best);
        }

        try
        {
            return new FontLoadResult(File.ReadAllBytes(best.FilePath), best.FaceIndex);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Selects the best match from a family-matched candidate list: an exact style match
    /// if <paramref name="styleName"/> was given, otherwise a preferred "Regular" match,
    /// otherwise the first candidate. Returns null when a specific style was requested and
    /// not found among <paramref name="matches"/> — the caller should treat that as
    /// "not found" (so it can fall back to synthetic styling), not fall back to an
    /// arbitrary style.
    /// </summary>
    private static SystemFontInfo? SelectBestMatch(List<SystemFontInfo> matches, string? styleName)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        if (styleName is not null)
        {
            foreach (var m in matches)
            {
                if (string.Equals(m.StyleName, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }

            return null;
        }

        foreach (var m in matches)
        {
            if (string.Equals(m.StyleName, "Regular", StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }
        }

        return matches[0];
    }

    /// <summary>
    /// Cheap narrowing tier that runs before the full directory scan: enumerates filenames
    /// only (no parsing) across <paramref name="fontDirs"/>, keeps files whose normalized
    /// name contains the normalized family name as a substring, then parses ONLY those
    /// narrowed candidates to verify the real family name — a filename match is just a
    /// hint, never trusted on its own.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this tier produced a definitive outcome (via <paramref name="result"/>,
    /// which may still be null if a specific style wasn't found among the heuristic
    /// matches) — the caller must NOT fall through to the full scan in that case.
    /// <c>false</c> only when there were no filename-narrowed candidates at all, meaning
    /// the caller should fall through to the full scan as the correctness backstop.
    /// </returns>
    private bool TryHeuristicFilenameMatch(string familyName, string? styleName, List<string> fontDirs, out FontLoadResult? result)
    {
        result = null;

        var normalizedFamily = NormalizeForFilenameMatch(familyName);
        var narrowedFiles = new List<string>();

        foreach (var dir in fontDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var filePath in files)
            {
                var ext = Path.GetExtension(filePath);
                if (!IsFontExtension(ext))
                    continue;

                var fileNameOnly = Path.GetFileNameWithoutExtension(filePath);
                if (NormalizeForFilenameMatch(fileNameOnly).Contains(normalizedFamily, StringComparison.Ordinal))
                {
                    narrowedFiles.Add(filePath);
                }
            }
        }

        if (narrowedFiles.Count == 0)
        {
            // No filename hint at all — fall through to the full scan (correctness backstop).
            Trace.TraceInformation(
                $"KernSmith: no filename-narrowed candidates found for '{familyName}' — falling back " +
                "to a full font directory scan.");
            return false;
        }

        var matches = new List<SystemFontInfo>();
        foreach (var filePath in narrowedFiles)
        {
            try
            {
                var data = File.ReadAllBytes(filePath);
                var faceCount = GetFaceCount(data);

                for (var faceIndex = 0; faceIndex < faceCount; faceIndex++)
                {
                    try
                    {
                        var parser = new TtfParser(data, faceIndex, parseAdvancedTables: false);
                        if (!parser.IsValid)
                            continue;

                        var parsedFamily = parser.Names?.FontFamily;
                        if (string.IsNullOrWhiteSpace(parsedFamily))
                            continue;

                        // The filename match was just a hint — the parsed family name is
                        // the real source of truth. Reject anything that doesn't actually
                        // match (e.g. "Helvetica" matching "HelveticaNeue-Bold.ttf").
                        if (!string.Equals(parsedFamily, familyName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        matches.Add(new SystemFontInfo
                        {
                            FamilyName = parsedFamily,
                            StyleName = parser.Names?.FontSubfamily ?? "Regular",
                            FilePath = filePath,
                            FaceIndex = faceIndex
                        });
                    }
                    catch (Exception ex) when (ex is FontParsingException or IOException or UnauthorizedAccessException)
                    {
                        // Skip faces that can't be parsed
                    }
                }
            }
            catch (Exception ex) when (ex is FontParsingException or IOException or UnauthorizedAccessException)
            {
                // Skip files that can't be read or parsed
            }
        }

        // A filename hint existed — this tier is done regardless of outcome. Even if every
        // narrowed candidate turned out to be a false positive (matches is empty), we do
        // NOT fall through to the full scan; that's a deliberate design tradeoff (a bounded
        // failed heuristic, not an escalation).
        var best = SelectBestMatch(matches, styleName);
        if (best == null)
        {
            if (matches.Count == 0)
            {
                Trace.TraceInformation(
                    $"KernSmith: {narrowedFiles.Count} filename-narrowed candidate(s) for '{familyName}' " +
                    "failed real verification (parsed family name did not match) — treating as a definitive miss.");
            }
            return true;
        }

        if (styleName is null)
        {
            CacheResolvedFont(familyName, best);
        }

        try
        {
            result = new FontLoadResult(File.ReadAllBytes(best.FilePath), best.FaceIndex);
        }
        catch (IOException)
        {
            result = null;
        }
        catch (UnauthorizedAccessException)
        {
            result = null;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a family name or filename for heuristic comparison: lowercased, with
    /// spaces, hyphens, and underscores stripped (so e.g. "Times New Roman" and
    /// "TimesNewRoman.ttf" compare equal on the parts that matter).
    /// </summary>
    private static string NormalizeForFilenameMatch(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is ' ' or '-' or '_')
                continue;
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Looks up <paramref name="familyName"/> in the lazy resolved-font cache. Returns
    /// true only if a cached entry exists and is still valid — validated and read in a
    /// single pass via <see cref="IsCacheEntryValidCore"/> so a cache hit costs exactly
    /// one file read, not two. Stale entries are evicted so the caller falls through to
    /// the normal resolution path.
    /// </summary>
    private bool TryGetValidCachedFont(string familyName, out FontLoadResult? result)
    {
        result = null;

        SystemFontInfo? entry;
        lock (_resolvedFontCacheLock)
        {
            _resolvedFontCache.TryGetValue(familyName, out entry);
        }

        if (entry is null)
        {
            // Not yet resolved this instance — try the well-known seed table before
            // falling through to the registry/full-scan tiers. A miss here is one
            // bounded, harmless failed check (see WellKnownFontSeeds); a hit skips
            // both directory enumeration and heuristic filtering entirely.
            return TryResolveFromSeed(familyName, out result);
        }

        if (IsCacheEntryValidCore(entry, familyName, ReadFileBytes, out byte[]? data) && data is not null)
        {
            result = new FontLoadResult(data, entry.FaceIndex);
            return true;
        }

        Trace.TraceInformation(
            $"KernSmith: cached/hinted font entry for '{familyName}' at '{entry.FilePath}' is no longer valid " +
            (File.Exists(entry.FilePath) ? "(parsed family name no longer matches)." : "(file no longer exists).") +
            " Falling back to normal resolution.");

        lock (_resolvedFontCacheLock)
        {
            _resolvedFontCache.Remove(familyName);
        }
        return false;
    }

    /// <summary>
    /// Attempts to resolve a family from <see cref="WellKnownFontSeeds"/> (or
    /// <see cref="SeedTableOverride"/> in tests): tries each candidate path in order,
    /// validating it exactly like any other cache entry via
    /// <see cref="IsCacheEntryValidCore"/>. The family is marked as attempted regardless
    /// of outcome, so a miss is never retried within this provider instance's lifetime.
    /// </summary>
    private bool TryResolveFromSeed(string familyName, out FontLoadResult? result)
    {
        result = null;

        lock (_seedAttemptedLock)
        {
            if (!_seedAttempted.Add(familyName))
                return false; // already tried (and failed) this family this instance
        }

        foreach (var candidatePath in GetSeedCandidatePaths(familyName))
        {
            var candidate = new SystemFontInfo
            {
                FamilyName = familyName,
                StyleName = "Regular",
                FilePath = candidatePath,
                FaceIndex = 0
            };

            if (IsCacheEntryValidCore(candidate, familyName, ReadFileBytes, out byte[]? data) && data is not null)
            {
                CacheResolvedFont(familyName, candidate);
                result = new FontLoadResult(data, candidate.FaceIndex);
                return true;
            }

            Trace.TraceInformation(
                $"KernSmith: seed candidate for '{familyName}' at '{candidatePath}' was invalid " +
                (File.Exists(candidatePath) ? "(parsed family name did not match)." : "(file does not exist)."));
        }

        return false;
    }

    /// <summary>
    /// Returns the best-guess candidate file paths for a family, in try-order. Uses
    /// <see cref="SeedTableOverride"/> when set (tests), otherwise the real OS-specific
    /// <see cref="WellKnownFontSeeds"/> table. Windows candidates are filenames resolved
    /// against <see cref="GetFontDirectories"/> (same directories LoadFont's registry path
    /// already resolves against); macOS candidates are already-absolute paths. No seeding
    /// on Linux — <c>fc-list</c> already covers the fast path there.
    /// </summary>
    private List<string> GetSeedCandidatePaths(string familyName)
    {
        if (SeedTableOverride is not null)
        {
            return SeedTableOverride.TryGetValue(familyName, out var overridePaths)
                ? new List<string>(overridePaths)
                : new List<string>();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!WellKnownFontSeeds.WindowsFileNames.TryGetValue(familyName, out var fileNames))
                return new List<string>();

            var dirs = GetFontDirectories();
            var candidates = new List<string>();
            foreach (var fileName in fileNames)
            {
                foreach (var dir in dirs)
                {
                    candidates.Add(Path.Combine(dir, fileName));
                }
            }
            return candidates;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return WellKnownFontSeeds.MacPaths.TryGetValue(familyName, out var macPaths)
                ? new List<string>(macPaths)
                : new List<string>();
        }

        return new List<string>();
    }

    /// <summary>
    /// Pre-populates <see cref="_resolvedFontCache"/> with a consumer-supplied file path for a
    /// family name, exposed publicly via <see cref="KernSmith.BmFont.HintFontLocation"/>. The
    /// next <see cref="LoadFont"/> call for this family is a tier-1 cache lookup, validated by
    /// <see cref="IsCacheEntryValidCore"/> exactly like any other cache/seed entry before being
    /// trusted — an invalid hint is evicted and normal resolution proceeds unchanged.
    /// </summary>
    internal void AddResolvedFontHint(string familyName, string filePath, int faceIndex = 0)
    {
        CacheResolvedFont(familyName, new SystemFontInfo
        {
            FamilyName = familyName,
            StyleName = "Regular",
            FilePath = filePath,
            FaceIndex = faceIndex
        });
    }

    /// <summary>
    /// Stores the resolved <see cref="SystemFontInfo"/> for a family name so the next
    /// no-style-requested lookup for that family is a pure cache hit.
    /// </summary>
    private void CacheResolvedFont(string familyName, SystemFontInfo info)
    {
        lock (_resolvedFontCacheLock)
        {
            _resolvedFontCache[familyName] = info;
        }
    }

    /// <summary>
    /// Validates a cached resolved-font entry: the file must still exist and must still
    /// parse as a font whose family name matches <paramref name="familyName"/>. Used to
    /// detect entries invalidated by files being deleted, moved, or replaced.
    /// </summary>
    internal static bool IsCacheEntryValid(SystemFontInfo entry, string familyName)
        => IsCacheEntryValidCore(entry, familyName, File.ReadAllBytes, out _);

    private static bool IsCacheEntryValidCore(SystemFontInfo entry, string familyName, Func<string, byte[]> readFileBytes, out byte[]? data)
    {
        data = null;

        if (!File.Exists(entry.FilePath))
        {
            return false;
        }

        try
        {
            var bytes = readFileBytes(entry.FilePath);
            var parser = new TtfParser(bytes, entry.FaceIndex, parseAdvancedTables: false);
            if (parser.IsValid && string.Equals(parser.Names?.FontFamily, familyName, StringComparison.OrdinalIgnoreCase))
            {
                data = bytes;
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is FontParsingException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to find and load a font file using the Windows registry, avoiding
    /// the expensive full font directory scan. Returns false on non-Windows platforms
    /// or if the font cannot be found via the registry.
    /// </summary>
    private static bool TryLoadFontFromRegistry(string familyName, string? styleName, out FontLoadResult? fontResult, out bool familyFound, out SystemFontInfo? resolvedInfo)
    {
        fontResult = null;
        familyFound = false;
        resolvedInfo = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            return TryLoadFontFromRegistryCore(familyName, styleName, out fontResult, out familyFound, out resolvedInfo);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException
                                     or TypeLoadException
                                     or System.Security.SecurityException
                                     or IOException
                                     or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Core registry lookup logic, separated so the platform guard and exception
    /// handling remain in the caller.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool TryLoadFontFromRegistryCore(string familyName, string? styleName, out FontLoadResult? fontResult, out bool familyFound, out SystemFontInfo? resolvedInfo)
    {
        fontResult = null;
        familyFound = false;
        resolvedInfo = null;

        // Registry keys that contain font entries. The HKLM key has system-wide fonts;
        // the HKCU key has per-user installed fonts.
        const string systemFontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        (string FileName, int FaceIndex)? bestMatch = null;

        // Search both system-wide and per-user font registry keys.
        using (RegistryKey? systemKey = Registry.LocalMachine.OpenSubKey(systemFontsKey))
        {
            bestMatch = FindFontFileNameInRegistryKey(systemKey, familyName, styleName, out familyFound);
        }

        if (bestMatch == null && !familyFound)
        {
            using RegistryKey? userKey = Registry.CurrentUser.OpenSubKey(systemFontsKey);
            bestMatch = FindFontFileNameInRegistryKey(userKey, familyName, styleName, out familyFound);
        }

        if (bestMatch == null)
        {
            return false;
        }

        string bestFileName = bestMatch.Value.FileName;
        int faceIndex = bestMatch.Value.FaceIndex;

        // The registry value is usually just a filename; resolve it against known font directories.
        string fontPath;
        if (Path.IsPathFullyQualified(bestFileName))
        {
            fontPath = bestFileName;
        }
        else
        {
            // Try system fonts directory first, then user fonts directory.
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemFontsDir = Path.Combine(windowsDir, "Fonts");
            string candidate = Path.Combine(systemFontsDir, bestFileName);
            if (File.Exists(candidate))
            {
                fontPath = candidate;
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    candidate = Path.Combine(localAppData, "Microsoft", "Windows", "Fonts", bestFileName);
                    if (File.Exists(candidate))
                    {
                        fontPath = candidate;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        fontResult = new FontLoadResult(File.ReadAllBytes(fontPath), faceIndex);
        resolvedInfo = new SystemFontInfo
        {
            FamilyName = familyName,
            StyleName = styleName ?? "Regular",
            FilePath = fontPath,
            FaceIndex = faceIndex
        };
        return true;
    }
#pragma warning restore CA1416

    /// <summary>
    /// Searches a single registry key for a font entry matching the requested
    /// family and style. Registry entry names typically look like
    /// "Arial (TrueType)" or "Arial Bold (TrueType)".
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (string FileName, int FaceIndex)? FindFontFileNameInRegistryKey(RegistryKey? key, string familyName, string? styleName, out bool familyFound)
    {
        familyFound = false;

        if (key == null)
        {
            return null;
        }

        string[] valueNames = key.GetValueNames();
        (string FileName, int FaceIndex)? fallback = null;

        foreach (string name in valueNames)
        {
            // Registry entry names are like "Arial (TrueType)", "Arial Bold Italic (TrueType)",
            // or bundled TTC entries like "Batang & BatangChe & Gungsuh & GungsuhChe (TrueType)".
            // Strip the suffix in parentheses to get the font display name(s).
            string displayPart = name;
            int parenIndex = name.IndexOf('(');
            if (parenIndex > 0)
            {
                displayPart = name.Substring(0, parenIndex).TrimEnd();
            }

            // Handle bundled TTC entries with " & " separators.
            // Find the segment that matches our family name. The segment's position
            // in the split array corresponds to the TTC face index.
            string? matchedSegment = null;
            int faceIndex = 0;
            if (displayPart.Contains(" & "))
            {
                string[] segments = displayPart.Split(new[] { " & " }, StringSplitOptions.None);
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].StartsWith(familyName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedSegment = segments[i];
                        faceIndex = i;
                        break;
                    }
                }
            }
            else if (displayPart.StartsWith(familyName, StringComparison.OrdinalIgnoreCase))
            {
                matchedSegment = displayPart;
                faceIndex = 0;
            }

            if (matchedSegment == null)
            {
                continue;
            }

            familyFound = true;

            // Extract the style portion after the family name.
            string entryStyle = matchedSegment.Length > familyName.Length
                ? matchedSegment.Substring(familyName.Length).Trim()
                : "";

            string? fileName = key.GetValue(name) as string;
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            if (styleName != null)
            {
                // Caller wants a specific style — match it exactly.
                if (string.Equals(entryStyle, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    return (fileName, faceIndex);
                }
            }
            else
            {
                // No style requested — prefer the entry with no style qualifier (i.e., Regular).
                if (entryStyle.Length == 0
                    || string.Equals(entryStyle, "Regular", StringComparison.OrdinalIgnoreCase))
                {
                    return (fileName, faceIndex);
                }

                // Keep the first match as a fallback in case no "Regular" entry exists.
                fallback ??= (fileName, faceIndex);
            }
        }

        return fallback;
    }

    /// <summary>
    /// Tries platform-specific fast discovery first, falling back to the slow
    /// directory scan if the fast path fails or returns no results.
    /// </summary>
    private static List<SystemFontInfo> ScanSystemFonts()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var results = TryScanFromRegistry();
                if (results is { Count: > 0 })
                    return results;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                     || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var results = TryScanFromFcList();
                if (results is { Count: > 0 })
                    return results;
            }
        }
        catch
        {
            // Fast path failed — fall through to slow scan.
        }

        return ScanFontDirectories();
    }

    /// <summary>
    /// Enumerates all fonts from the Windows registry, avoiding the expensive
    /// full directory scan with TtfParser.
    /// </summary>
    private static List<SystemFontInfo>? TryScanFromRegistry()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            return TryScanFromRegistryCore();
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException
                                     or TypeLoadException
                                     or System.Security.SecurityException
                                     or IOException
                                     or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Core registry enumeration logic for font discovery on Windows.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<SystemFontInfo>? TryScanFromRegistryCore()
    {
        const string fontsRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
        var fontDirs = GetFontDirectories();
        var results = new List<SystemFontInfo>();

        // Enumerate both system-wide and per-user font registry keys.
        using (RegistryKey? systemKey = Registry.LocalMachine.OpenSubKey(fontsRegistryKey))
        {
            EnumerateRegistryFonts(systemKey, fontDirs, results);
        }

        using (RegistryKey? userKey = Registry.CurrentUser.OpenSubKey(fontsRegistryKey))
        {
            EnumerateRegistryFonts(userKey, fontDirs, results);
        }

        return results.Count > 0 ? results : null;
    }

    /// <summary>
    /// Reads all font entries from a single registry key and adds them to the results list.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void EnumerateRegistryFonts(RegistryKey? key, List<string> fontDirs, List<SystemFontInfo> results)
    {
        if (key == null)
            return;

        string[] valueNames = key.GetValueNames();

        foreach (string name in valueNames)
        {
            string? fileName = key.GetValue(name) as string;
            if (string.IsNullOrEmpty(fileName))
                continue;

            // Strip the suffix in parentheses, e.g., "(TrueType)" or "(OpenType)".
            string displayPart = name;
            int parenIndex = name.IndexOf('(');
            if (parenIndex > 0)
            {
                displayPart = name.Substring(0, parenIndex).TrimEnd();
            }

            // Only include TrueType/OpenType fonts (skip bitmap, vector, etc.)
            string ext = Path.GetExtension(fileName);
            if (!IsFontExtension(ext))
                continue;

            // Resolve the filename to a full path.
            string? fullPath = ResolveRegistryFontPath(fileName, fontDirs);
            if (fullPath == null)
                continue;

            // Handle bundled TTC entries with " & " separators.
            if (displayPart.Contains(" & "))
            {
                string[] segments = displayPart.Split(new[] { " & " }, StringSplitOptions.None);
                for (int i = 0; i < segments.Length; i++)
                {
                    ParseRegistryDisplayName(segments[i], out string family, out string style);
                    if (!string.IsNullOrWhiteSpace(family))
                    {
                        results.Add(new SystemFontInfo
                        {
                            FamilyName = family,
                            StyleName = style,
                            FilePath = fullPath,
                            FaceIndex = i
                        });
                    }
                }
            }
            else
            {
                ParseRegistryDisplayName(displayPart, out string family, out string style);
                if (!string.IsNullOrWhiteSpace(family))
                {
                    results.Add(new SystemFontInfo
                    {
                        FamilyName = family,
                        StyleName = style,
                        FilePath = fullPath,
                        FaceIndex = 0
                    });
                }
            }
        }
    }

    /// <summary>
    /// Parses a registry display name like "Arial Bold Italic" into family and style.
    /// Known style keywords (Bold, Italic, Light, Medium, etc.) at the end of the
    /// name are treated as the style; everything before them is the family name.
    /// </summary>
    private static void ParseRegistryDisplayName(string displayName, out string familyName, out string styleName)
    {
        displayName = displayName.Trim();

        if (string.IsNullOrEmpty(displayName))
        {
            familyName = "";
            styleName = "Regular";
            return;
        }

        // Known style tokens that appear at the end of registry display names.
        string[] knownStyles = { "Bold Italic", "Bold", "Italic", "Light Italic", "Light",
                                  "Medium Italic", "Medium", "SemiBold Italic", "SemiBold",
                                  "Thin Italic", "Thin", "ExtraBold Italic", "ExtraBold",
                                  "ExtraLight Italic", "ExtraLight", "Black Italic", "Black",
                                  "DemiBold Italic", "DemiBold", "Regular" };

        foreach (string token in knownStyles)
        {
            if (displayName.EndsWith(" " + token, StringComparison.OrdinalIgnoreCase))
            {
                familyName = displayName.Substring(0, displayName.Length - token.Length - 1).Trim();
                styleName = token;
                return;
            }

            if (string.Equals(displayName, token, StringComparison.OrdinalIgnoreCase))
            {
                // The entire display name is a style keyword — treat it as the family.
                familyName = displayName;
                styleName = "Regular";
                return;
            }
        }

        // No known style suffix found — it's a Regular font.
        familyName = displayName;
        styleName = "Regular";
    }

    /// <summary>
    /// Resolves a registry font filename to a full file path using the known font directories.
    /// </summary>
    private static string? ResolveRegistryFontPath(string fileName, List<string> fontDirs)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        foreach (string dir in fontDirs)
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Enumerates system fonts using the <c>fc-list</c> command, available on
    /// Linux and optionally macOS (via Homebrew fontconfig).
    /// </summary>
    private static List<SystemFontInfo>? TryScanFromFcList()
    {
        return TryScanFromFcList("fc-list");
    }

    /// <summary>
    /// Enumerates system fonts using the given fontconfig-compatible executable.
    /// Extracted from the no-arg overload so tests can point it at a controlled
    /// (non-existent) executable name instead of the hardcoded <c>fc-list</c>.
    /// </summary>
    internal static List<SystemFontInfo>? TryScanFromFcList(string executableName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executableName,
                Arguments = "--format=%{family}|%{style}|%{file}|%{index}\\n",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { /* best effort */ }
                return null;
            }

            if (process.ExitCode != 0)
                return null;

            if (string.IsNullOrWhiteSpace(output))
                return null;

            var results = new List<SystemFontInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                string[] parts = trimmed.Split('|');
                if (parts.Length < 3)
                    continue;

                // fc-list may return comma-separated values; use the first one.
                string family = parts[0].Split(',')[0].Trim();
                string style = parts[1].Split(',')[0].Trim();
                string filePath = parts[2].Trim();
                int faceIndex = 0;

                if (parts.Length >= 4 && int.TryParse(parts[3].Trim(), out int parsed))
                {
                    faceIndex = parsed;
                }

                if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(filePath))
                    continue;

                if (string.IsNullOrWhiteSpace(style))
                    style = "Regular";

                results.Add(new SystemFontInfo
                {
                    FamilyName = family,
                    StyleName = style,
                    FilePath = filePath,
                    FaceIndex = faceIndex
                });
            }

            return results.Count > 0 ? results : null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Thrown by Process.Start when the target executable can't be found or
            // launched (e.g. stock macOS, which uses CoreText rather than fontconfig
            // and typically has no fc-list binary installed). This is a common,
            // expected fallback path — log it so it isn't silent, then fall back to
            // the full font directory scan.
            Trace.TraceInformation(
                $"KernSmith: '{executableName}' was not found ({ex.Message}). " +
                "Falling back to a full font directory scan.");
            return null;
        }
        catch
        {
            // Any other error (timeout, non-zero exit, malformed output, etc.) —
            // fall back to directory scan without additional logging.
            return null;
        }
    }

    /// <summary>
    /// Slow fallback: scans all platform font directories and parses every font
    /// file with <see cref="TtfParser"/> to extract metadata.
    /// </summary>
    private static List<SystemFontInfo> ScanFontDirectories()
    {
        return ScanFontDirectories(GetFontDirectories());
    }

    /// <summary>
    /// Scans the given directories and parses every font file with
    /// <see cref="TtfParser"/> to extract metadata. Extracted from the no-arg
    /// overload so tests can point it at a controlled directory instead of the
    /// hardcoded platform-specific system font directories.
    /// </summary>
    internal static List<SystemFontInfo> ScanFontDirectories(IEnumerable<string> directories)
    {
        var results = new List<SystemFontInfo>();

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var filePath in files)
            {
                var ext = Path.GetExtension(filePath);
                if (!IsFontExtension(ext))
                    continue;

                try
                {
                    var data = File.ReadAllBytes(filePath);
                    var faceCount = GetFaceCount(data);

                    for (var faceIndex = 0; faceIndex < faceCount; faceIndex++)
                    {
                        try
                        {
                            var parser = new TtfParser(data, faceIndex, parseAdvancedTables: false);
                            if (!parser.IsValid)
                            {
                                continue;
                            }
                            var familyName = parser.Names?.FontFamily;
                            var styleName = parser.Names?.FontSubfamily ?? "Regular";

                            if (string.IsNullOrWhiteSpace(familyName))
                                continue;

                            results.Add(new SystemFontInfo
                            {
                                FamilyName = familyName,
                                StyleName = styleName,
                                FilePath = filePath,
                                FaceIndex = faceIndex
                            });
                        }
                        catch (Exception ex) when (ex is FontParsingException or IOException or UnauthorizedAccessException)
                        {
                            // Skip faces that can't be parsed
                        }
                    }
                }
                catch (Exception ex) when (ex is FontParsingException or IOException or UnauthorizedAccessException)
                {
                    // Skip files that can't be read or parsed
                }
            }
        }

        return results;
    }

    private static bool IsFontExtension(string extension)
    {
        foreach (var ext in FontExtensions)
        {
            if (string.Equals(extension, ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Determines the number of font faces in a file. TTC files contain
    /// multiple faces; regular TTF/OTF files contain exactly one.
    /// </summary>
    private static int GetFaceCount(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return 1;

        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data);

        // "ttcf" magic means TrueType Collection
        if (magic == 0x74746366)
        {
            var numFonts = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8));
            return (int)numFonts;
        }

        return 1;
    }

    private static List<string> GetFontDirectories()
    {
        var dirs = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            dirs.Add(Path.Combine(windowsDir, "Fonts"));

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                dirs.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            dirs.Add("/Library/Fonts");
            dirs.Add("/System/Library/Fonts");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                dirs.Add(Path.Combine(home, "Library", "Fonts"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            dirs.Add("/usr/share/fonts");
            dirs.Add("/usr/local/share/fonts");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, ".fonts"));
                dirs.Add(Path.Combine(home, ".local", "share", "fonts"));
            }
        }

        return dirs;
    }
}
