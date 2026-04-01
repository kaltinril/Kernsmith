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
    private List<SystemFontInfo>? _cachedFonts;

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
    public FontLoadResult? LoadFont(string familyName, string? styleName = null)
    {
        // Try fast registry-based lookup on Windows before falling back to
        // the full font directory scan, which can take several seconds.
        if (TryLoadFontFromRegistry(familyName, styleName, out FontLoadResult? registryResult, out bool familyFound))
        {
            return registryResult;
        }

        // If the registry confirmed the family exists but the specific style
        // doesn't, there's no separate font file for that style — skip the
        // expensive full scan and return null so the caller uses synthetic styling.
        if (familyFound && styleName != null)
        {
            return null;
        }

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

        SystemFontInfo? best = null;

        if (styleName is not null)
        {
            // Match the requested style
            foreach (var m in matches)
            {
                if (string.Equals(m.StyleName, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    best = m;
                    break;
                }
            }
        }
        else
        {
            // Prefer "Regular"
            foreach (var m in matches)
            {
                if (string.Equals(m.StyleName, "Regular", StringComparison.OrdinalIgnoreCase))
                {
                    best = m;
                    break;
                }
            }
        }

        // When a specific style was requested but not found, return null
        // so the caller knows it needs synthetic bold/italic. Only fall back
        // to the first match when no style was requested.
        if (best == null && styleName != null)
        {
            return null;
        }
        best ??= matches[0];

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
    /// Attempts to find and load a font file using the Windows registry, avoiding
    /// the expensive full font directory scan. Returns false on non-Windows platforms
    /// or if the font cannot be found via the registry.
    /// </summary>
    private static bool TryLoadFontFromRegistry(string familyName, string? styleName, out FontLoadResult? fontResult, out bool familyFound)
    {
        fontResult = null;
        familyFound = false;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            return TryLoadFontFromRegistryCore(familyName, styleName, out fontResult, out familyFound);
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
    private static bool TryLoadFontFromRegistryCore(string familyName, string? styleName, out FontLoadResult? fontResult, out bool familyFound)
    {
        fontResult = null;
        familyFound = false;

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
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "fc-list",
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
        catch
        {
            // fc-list not found or any other error — fall back to directory scan.
            return null;
        }
    }

    /// <summary>
    /// Slow fallback: scans all platform font directories and parses every font
    /// file with <see cref="TtfParser"/> to extract metadata.
    /// </summary>
    private static List<SystemFontInfo> ScanFontDirectories()
    {
        var directories = GetFontDirectories();
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
                            var parser = new TtfParser(data, faceIndex);
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
