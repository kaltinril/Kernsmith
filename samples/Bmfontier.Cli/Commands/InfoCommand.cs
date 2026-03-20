using System.Text.Json;
using Bmfontier.Cli.Utilities;
using Bmfontier.Font;

namespace Bmfontier.Cli.Commands;

internal sealed class InfoCommand
{
    public static int Execute(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            ShowHelp();
            return ExitCodes.Success;
        }

        try
        {
            string? path = null;
            var json = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--json":
                        json = true;
                        break;
                    case "--no-color":
                        ConsoleOutput.SetNoColor(true);
                        break;
                    case "-v":
                    case "--verbose":
                        ConsoleOutput.SetVerbose(true);
                        break;
                    case "-q":
                    case "--quiet":
                        ConsoleOutput.SetQuiet(true);
                        break;
                    default:
                        if (args[i].StartsWith('-'))
                            throw new ArgumentException($"Unknown option: {args[i]}");
                        path = args[i];
                        break;
                }
            }

            if (path == null)
            {
                ConsoleOutput.WriteError("A font file path is required.");
                return ExitCodes.InvalidArguments;
            }

            if (!File.Exists(path))
            {
                ConsoleOutput.WriteError($"File not found: {path}");
                return ExitCodes.FileNotFound;
            }

            var data = File.ReadAllBytes(path);

            // Detect format
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var formatName = ext switch
            {
                ".otf" => "OpenType (OTF)",
                ".woff" => "WOFF",
                ".woff2" => "WOFF2",
                _ => "TrueType (TTF)"
            };

            // Auto-decompress WOFF
            if (WoffDecompressor.IsWoff(data) || WoffDecompressor.IsWoff2(data))
            {
                data = WoffDecompressor.Decompress(data);
            }

            // Parse font
            var reader = new TtfFontReader();
            var fontInfo = reader.ReadFont(data);

            if (json)
                PrintJson(path, fontInfo, formatName);
            else
                PrintHuman(path, fontInfo, formatName);

            return ExitCodes.Success;
        }
        catch (FontParsingException ex)
        {
            ConsoleOutput.WriteError($"Font error: {ex.Message}");
            return ExitCodes.FontParseError;
        }
        catch (ArgumentException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            ConsoleOutput.WriteError($"Error reading font: {ex.Message}");
            return ExitCodes.FontParseError;
        }
    }

    private static void PrintHuman(string path, Font.Models.FontInfo fontInfo, string formatName)
    {
        Console.WriteLine($"{"File:",-14} {Path.GetFileName(path)}");
        Console.WriteLine($"{"Family:",-14} {fontInfo.FamilyName}");
        Console.WriteLine($"{"Style:",-14} {fontInfo.StyleName}");
        Console.WriteLine($"{"Format:",-14} {formatName}");
        Console.WriteLine($"{"Glyphs:",-14} {fontInfo.NumGlyphs}");
        Console.WriteLine($"{"Kerning:",-14} {fontInfo.KerningPairs.Count} pairs");
        Console.WriteLine($"{"Units/Em:",-14} {fontInfo.UnitsPerEm}");

        if (fontInfo.HasColorGlyphs)
            Console.WriteLine($"{"Color:",-14} Yes (color glyph tables detected)");

        // Variation axes
        if (fontInfo.VariationAxes is { Count: > 0 })
        {
            Console.WriteLine();
            Console.WriteLine("Variation axes:");
            foreach (var axis in fontInfo.VariationAxes)
            {
                var name = axis.Name ?? axis.Tag;
                Console.WriteLine($"  {axis.Tag,-6}{name,-16}{axis.MinValue}..{axis.MaxValue}  (default: {axis.DefaultValue})");
            }
        }

        // Named instances
        if (fontInfo.NamedInstances is { Count: > 0 })
        {
            Console.WriteLine();
            Console.WriteLine("Named instances:");
            foreach (var instance in fontInfo.NamedInstances)
            {
                var coords = string.Join(", ", instance.Coordinates.Select(c => $"{c.Key}={c.Value}"));
                Console.WriteLine($"  {instance.Name ?? "(unnamed)"}: {coords}");
            }
        }

        // Unicode coverage (summarize)
        if (fontInfo.AvailableCodepoints.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Unicode coverage:");
            var ranges = ComputeRanges(fontInfo.AvailableCodepoints);
            var displayCount = Math.Min(ranges.Count, 20);
            for (int i = 0; i < displayCount; i++)
            {
                var (start, end, count) = ranges[i];
                Console.WriteLine($"  U+{start:X4}..U+{end:X4}  ({count} chars)");
            }
            if (ranges.Count > 20)
                Console.WriteLine($"  ... and {ranges.Count - 20} more ranges");
        }
    }

    private static void PrintJson(string path, Font.Models.FontInfo fontInfo, string formatName)
    {
        var ranges = ComputeRanges(fontInfo.AvailableCodepoints);

        var obj = new
        {
            file = Path.GetFileName(path),
            family = fontInfo.FamilyName,
            style = fontInfo.StyleName,
            format = formatName,
            glyphs = fontInfo.NumGlyphs,
            kerningPairs = fontInfo.KerningPairs.Count,
            unitsPerEm = fontInfo.UnitsPerEm,
            bold = fontInfo.IsBold,
            italic = fontInfo.IsItalic,
            hasColorGlyphs = fontInfo.HasColorGlyphs,
            variationAxes = fontInfo.VariationAxes?.Select(a => new
            {
                tag = a.Tag,
                name = a.Name,
                min = a.MinValue,
                max = a.MaxValue,
                defaultValue = a.DefaultValue
            }).ToArray(),
            unicodeRanges = ranges.Select(r => new
            {
                start = r.Start.ToString("X4"),
                end = r.End.ToString("X4"),
                count = r.Count
            }).ToArray()
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        Console.WriteLine(JsonSerializer.Serialize(obj, jsonOptions));
    }

    private static List<(int Start, int End, int Count)> ComputeRanges(IReadOnlyList<int> codepoints)
    {
        if (codepoints.Count == 0)
            return new List<(int, int, int)>();

        var sorted = codepoints.OrderBy(c => c).ToList();
        var ranges = new List<(int Start, int End, int Count)>();

        var start = sorted[0];
        var end = sorted[0];
        var count = 1;

        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == end + 1)
            {
                end = sorted[i];
                count++;
            }
            else
            {
                ranges.Add((start, end, count));
                start = sorted[i];
                end = sorted[i];
                count = 1;
            }
        }
        ranges.Add((start, end, count));

        return ranges;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Show font file metadata.

            Usage: bmfontier info <path> [options]

              <path>                      Path to a font file (TTF, OTF, WOFF)

            Options:
              --json                      Output as JSON

            Shows: family name, style, glyph count, kerning pairs,
            variation axes (if variable font), Unicode coverage.

            Examples:
              bmfontier info myfont.ttf
              bmfontier info RobotoFlex.ttf --json
            """);
    }
}
