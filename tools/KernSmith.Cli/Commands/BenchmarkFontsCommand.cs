using System.Diagnostics;
using System.Text.Json;
using KernSmith.Cli.Utilities;
using KernSmith.Font;

namespace KernSmith.Cli.Commands;

/// <summary>
/// Benchmarks <see cref="DefaultSystemFontProvider.LoadFont"/> resolution cost across every
/// installed font family in two passes within the same process — a "cold" pass (first
/// resolution) and a "warm" pass (repeat resolution) — so the effect of the per-family
/// resolved-font cache is directly visible in one run.
/// </summary>
internal sealed class BenchmarkFontsCommand
{
    /// <summary>
    /// Parses arguments, times a cold and warm <c>LoadFont</c> pass over every installed
    /// font family, and prints per-family timings plus summary statistics for each pass.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded from the top-level dispatcher.</param>
    /// <returns>An exit code indicating success or the category of failure.</returns>
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

            // Distinct family names — LoadFont resolves per family, not per face/style.
            var familyNames = fonts
                .Select(f => f.FamilyName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .AsEnumerable();

            if (filter != null)
            {
                familyNames = familyNames.Where(f =>
                    f.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var families = familyNames.ToList();

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            if (families.Count == 0)
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { families = 0 }, jsonOptions));
                }
                else
                {
                    Console.WriteLine("No font families found to benchmark.");
                }
                return ExitCodes.Success;
            }

            if (!json)
            {
                Console.WriteLine($"Benchmarking font resolution for {families.Count} families...");
                Console.WriteLine();
            }

            // Two passes over the SAME family list, deliberately not re-calling
            // GetInstalledFonts() in between — the cold pass populates the resolved-font
            // cache per family, and the warm pass should be served entirely from it.
            var coldResults = RunPass(provider, families);
            var warmResults = RunPass(provider, families);

            if (json)
            {
                var jsonData = new
                {
                    families = families.Count,
                    coldPass = ToJsonPass(coldResults),
                    warmPass = ToJsonPass(warmResults)
                };

                Console.WriteLine(JsonSerializer.Serialize(jsonData, jsonOptions));
            }
            else
            {
                Console.WriteLine("Cold pass:");
                PrintPass(coldResults);
                Console.WriteLine();
                Console.WriteLine("Warm pass (cache hit):");
                PrintPass(warmResults);
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

    /// <summary>
    /// Times a single <c>LoadFont(family)</c> call per family name, in list order.
    /// </summary>
    private static List<(string Family, double Ms)> RunPass(
        DefaultSystemFontProvider provider, List<string> families)
    {
        var results = new List<(string Family, double Ms)>(families.Count);

        foreach (var family in families)
        {
            var sw = Stopwatch.StartNew();
            provider.LoadFont(family);
            sw.Stop();
            results.Add((family, sw.Elapsed.TotalMilliseconds));
        }

        return results;
    }

    private static void PrintPass(List<(string Family, double Ms)> results)
    {
        foreach (var (family, ms) in results)
        {
            Console.WriteLine($"  {family,-24} {ms,8:F1}ms");
        }

        var times = results.Select(r => r.Ms).ToList();
        Console.WriteLine(
            $"  Min: {times.Min():F1}ms  Mean: {times.Average():F1}ms  " +
            $"Max: {times.Max():F1}ms  Total: {times.Sum():F1}ms");
    }

    private static object ToJsonPass(List<(string Family, double Ms)> results)
    {
        var times = results.Select(r => r.Ms).ToList();
        return new
        {
            results = results.Select(r => new { family = r.Family, ms = r.Ms }).ToArray(),
            min = times.Min(),
            mean = times.Average(),
            max = times.Max(),
            totalMs = times.Sum()
        };
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Benchmark font resolution cost across installed font families.

            Runs two timed passes of LoadFont() over every installed font family in the
            same process, so the effect of the per-family resolved-font cache is visible
            in one run instead of requiring two separate invocations:

              Cold pass — first resolution for each family (registry lookup or full
                          directory scan on first miss).
              Warm pass — repeat resolution for each family, served from the
                          resolved-font cache.

            Usage: kernsmith benchmark-fonts [options]

            Options:
              --filter <pattern>          Only benchmark families matching this substring
                                           (case-insensitive)
              --json                      Output as JSON

            Examples:
              kernsmith benchmark-fonts
              kernsmith benchmark-fonts --filter "roboto"
              kernsmith benchmark-fonts --json
            """);
    }
}
