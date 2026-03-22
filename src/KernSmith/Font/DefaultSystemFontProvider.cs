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
    public byte[]? LoadFont(string familyName, string? styleName = null)
    {
        // Try fast registry-based lookup on Windows before falling back to
        // the full font directory scan, which can take several seconds.
        if (TryLoadFontFromRegistry(familyName, styleName, out byte[]? registryResult, out bool familyFound))
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
            return File.ReadAllBytes(best.FilePath);
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
    private static bool TryLoadFontFromRegistry(string familyName, string? styleName, out byte[]? fontData, out bool familyFound)
    {
        fontData = null;
        familyFound = false;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            return TryLoadFontFromRegistryCore(familyName, styleName, out fontData, out familyFound);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException
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
    private static bool TryLoadFontFromRegistryCore(string familyName, string? styleName, out byte[]? fontData, out bool familyFound)
    {
        fontData = null;
        familyFound = false;

        // Registry keys that contain font entries. The HKLM key has system-wide fonts;
        // the HKCU key has per-user installed fonts.
        const string systemFontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        string? bestFileName = null;

        // Search both system-wide and per-user font registry keys.
        using (RegistryKey? systemKey = Registry.LocalMachine.OpenSubKey(systemFontsKey))
        {
            bestFileName = FindFontFileNameInRegistryKey(systemKey, familyName, styleName, out familyFound);
        }

        if (bestFileName == null && !familyFound)
        {
            using RegistryKey? userKey = Registry.CurrentUser.OpenSubKey(systemFontsKey);
            bestFileName = FindFontFileNameInRegistryKey(userKey, familyName, styleName, out familyFound);
        }

        if (bestFileName == null)
        {
            return false;
        }

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

        fontData = File.ReadAllBytes(fontPath);
        return true;
    }
#pragma warning restore CA1416

    /// <summary>
    /// Searches a single registry key for a font entry matching the requested
    /// family and style. Registry entry names typically look like
    /// "Arial (TrueType)" or "Arial Bold (TrueType)".
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? FindFontFileNameInRegistryKey(RegistryKey? key, string familyName, string? styleName, out bool familyFound)
    {
        familyFound = false;

        if (key == null)
        {
            return null;
        }

        string[] valueNames = key.GetValueNames();
        string? fallbackFileName = null;

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
            // Find the segment that matches our family name.
            string? matchedSegment = null;
            if (displayPart.Contains(" & "))
            {
                string[] segments = displayPart.Split(new[] { " & " }, StringSplitOptions.None);
                foreach (string segment in segments)
                {
                    if (segment.StartsWith(familyName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedSegment = segment;
                        break;
                    }
                }
            }
            else if (displayPart.StartsWith(familyName, StringComparison.OrdinalIgnoreCase))
            {
                matchedSegment = displayPart;
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
                    return fileName;
                }
            }
            else
            {
                // No style requested — prefer the entry with no style qualifier (i.e., Regular).
                if (entryStyle.Length == 0
                    || string.Equals(entryStyle, "Regular", StringComparison.OrdinalIgnoreCase))
                {
                    return fileName;
                }

                // Keep the first match as a fallback in case no "Regular" entry exists.
                fallbackFileName ??= fileName;
            }
        }

        return fallbackFileName;
    }

    private static List<SystemFontInfo> ScanSystemFonts()
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
