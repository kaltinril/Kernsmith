using System.Runtime.InteropServices;
using Bmfontier.Font.Models;

namespace Bmfontier.Font;

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

        // Fall back to first match if no exact style match
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
                        catch
                        {
                            // Skip faces that can't be parsed
                        }
                    }
                }
                catch
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
            dirs.Add(@"C:\Windows\Fonts");

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
