using System.Text.Json;
using Bmfontier.Cli.Utilities;
using Bmfontier.Font;

namespace Bmfontier.Cli.Commands;

internal sealed class ListFontsCommand
{
    public static int Execute(string[] args)
    {
        if (args is ["--help"])
        {
            ShowHelp();
            return ExitCodes.Success;
        }

        try
        {
            string? filter = null;
            var json = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--filter":
                        i++;
                        if (i >= args.Length)
                            throw new ArgumentException("Missing value for --filter");
                        filter = args[i];
                        break;
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
                        // Treat positional argument as a filter pattern.
                        filter = args[i];
                        break;
                }
            }

            ConsoleOutput.WriteProgress("Scanning system fonts...");

            var provider = new DefaultSystemFontProvider();
            var fonts = provider.GetInstalledFonts();

            // Group by family name
            var grouped = fonts
                .GroupBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            // Apply filter
            var filtered = grouped.AsEnumerable();
            if (filter != null)
            {
                filtered = filtered.Where(g =>
                    g.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var families = filtered.ToList();

            if (json)
            {
                var jsonData = families.Select(g => new
                {
                    family = g.Key,
                    styles = g.Select(f => f.StyleName)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                }).ToArray();

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                Console.WriteLine(JsonSerializer.Serialize(jsonData, jsonOptions));
            }
            else
            {
                var totalFaces = families.Sum(g => g.Count());
                Console.WriteLine($"Found {totalFaces} font faces in {families.Count} families:");
                Console.WriteLine();

                foreach (var family in families)
                {
                    var styles = family
                        .Select(f => f.StyleName)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine($"  {family.Key}");
                    Console.WriteLine($"    {string.Join(", ", styles)}");
                }
            }

            return ExitCodes.Success;
        }
        catch (ArgumentException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (UnauthorizedAccessException ex)
        {
            ConsoleOutput.WriteError($"Access denied while scanning fonts: {ex.Message}");
            return ExitCodes.OutputWriteError;
        }
        catch (DirectoryNotFoundException ex)
        {
            ConsoleOutput.WriteError($"Font directory not found: {ex.Message}");
            return ExitCodes.FileNotFound;
        }
        catch (IOException ex)
        {
            ConsoleOutput.WriteError($"I/O error while scanning fonts: {ex.Message}");
            return ExitCodes.OutputWriteError;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            List system-installed fonts.

            Usage: bmfontier list-fonts [options]

            Options:
              --filter <pattern>          Filter by family name (case-insensitive substring)
              --json                      Output as JSON

            Examples:
              bmfontier list-fonts
              bmfontier list-fonts --filter "roboto"
              bmfontier list-fonts --json
            """);
    }
}
