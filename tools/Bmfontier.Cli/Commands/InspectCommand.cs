using System.Text.Json;
using Bmfontier.Cli.Utilities;
using Bmfontier.Output;
using Bmfontier.Output.Model;

namespace Bmfontier.Cli.Commands;

internal sealed class InspectCommand
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
                ConsoleOutput.WriteError("A .fnt file path is required.");
                return ExitCodes.InvalidArguments;
            }

            if (!File.Exists(path))
            {
                ConsoleOutput.WriteError($"File not found: {path}");
                return ExitCodes.FileNotFound;
            }

            var data = File.ReadAllBytes(path);
            var format = DetectFormat(data);
            var model = BmFontReader.Read(data);

            if (json)
                PrintJson(model);
            else
                PrintHuman(model, format);

            return ExitCodes.Success;
        }
        catch (FormatException ex)
        {
            ConsoleOutput.WriteError($"Format error: {ex.Message}");
            return ExitCodes.FontParseError;
        }
        catch (IOException ex)
        {
            ConsoleOutput.WriteError($"I/O error: {ex.Message}");
            return ExitCodes.OutputWriteError;
        }
        catch (ArgumentException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
    }

    private static string DetectFormat(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 66 && data[1] == 77 && data[2] == 70)
            return "Binary";

        // Only decode the first few bytes to detect XML vs text format.
        var peekLength = Math.Min(data.Length, 64);
        var text = System.Text.Encoding.UTF8.GetString(data, 0, peekLength).TrimStart();
        if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<font", StringComparison.OrdinalIgnoreCase))
            return "XML";

        // Validate that it looks like a BMFont text format before returning "Text".
        if (text.StartsWith("info ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("common ", StringComparison.OrdinalIgnoreCase))
            return "Text";

        return "Text";
    }

    private static void PrintHuman(BmFontModel model, string format)
    {
        var style = "Regular";
        if (model.Info.Bold && model.Info.Italic) style = "Bold Italic";
        else if (model.Info.Bold) style = "Bold";
        else if (model.Info.Italic) style = "Italic";

        Console.WriteLine($"{"Font:",-13} {model.Info.Face}");
        Console.WriteLine($"{"Size:",-13} {model.Info.Size}px");
        Console.WriteLine($"{"Style:",-13} {style}");
        Console.WriteLine($"{"Characters:",-13} {model.Characters.Count}");
        Console.WriteLine($"{"Kerning:",-13} {model.KerningPairs.Count} pairs");
        Console.WriteLine($"{"Pages:",-13} {model.Pages.Count}");
        Console.WriteLine($"{"Texture:",-13} {model.Common.ScaleW} x {model.Common.ScaleH}");
        Console.WriteLine($"{"Line height:",-13} {model.Common.LineHeight}");
        Console.WriteLine($"{"Base:",-13} {model.Common.Base}");
        Console.WriteLine($"{"Format:",-13} {format}");

        if (model.Common.Packed)
            Console.WriteLine($"{"Packed:",-13} yes");

        // Unicode ranges
        var ranges = ComputeUnicodeRanges(model.Characters);
        if (ranges.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Unicode ranges:");
            foreach (var (start, end, count) in ranges)
            {
                Console.WriteLine($"  U+{start:X4}..U+{end:X4}  ({count} chars)");
            }
        }

        // Pages
        if (model.Pages.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Pages:");
            foreach (var page in model.Pages)
            {
                Console.WriteLine($"  [{page.Id}] {page.File}");
            }
        }
    }

    private static void PrintJson(BmFontModel model)
    {
        var ranges = ComputeUnicodeRanges(model.Characters);
        var obj = new
        {
            face = model.Info.Face,
            size = model.Info.Size,
            bold = model.Info.Bold,
            italic = model.Info.Italic,
            characterCount = model.Characters.Count,
            kerningPairCount = model.KerningPairs.Count,
            pageCount = model.Pages.Count,
            textureWidth = model.Common.ScaleW,
            textureHeight = model.Common.ScaleH,
            lineHeight = model.Common.LineHeight,
            @base = model.Common.Base,
            packed = model.Common.Packed,
            pages = model.Pages.Select(p => new { id = p.Id, file = p.File }).ToArray(),
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

    private static List<(int Start, int End, int Count)> ComputeUnicodeRanges(IReadOnlyList<CharEntry> characters)
    {
        if (characters.Count == 0)
            return new List<(int, int, int)>();

        var ids = characters.Select(c => c.Id).OrderBy(id => id).ToList();
        var ranges = new List<(int Start, int End, int Count)>();

        var rangeStart = ids[0];
        var rangeEnd = ids[0];
        var count = 1;

        for (int i = 1; i < ids.Count; i++)
        {
            if (ids[i] == rangeEnd + 1)
            {
                rangeEnd = ids[i];
                count++;
            }
            else
            {
                ranges.Add((rangeStart, rangeEnd, count));
                rangeStart = ids[i];
                rangeEnd = ids[i];
                count = 1;
            }
        }
        ranges.Add((rangeStart, rangeEnd, count));

        return ranges;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Inspect an existing BMFont file.

            Usage: bmfontier inspect <path> [options]

              <path>                      Path to a .fnt file (text, XML, or binary)

            Options:
              --json                      Output as JSON instead of human-readable table

            Examples:
              bmfontier inspect myfont.fnt
              bmfontier inspect myfont.fnt --json
            """);
    }
}
