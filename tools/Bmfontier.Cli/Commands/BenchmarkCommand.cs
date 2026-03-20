using System.Diagnostics;
using Bmfontier.Cli.Utilities;

namespace Bmfontier.Cli.Commands;

internal sealed class BenchmarkCommand
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
            var iterations = 10;
            string? fontPath = null;
            string? systemFontName = null;
            int size = 32;
            string charsetPreset = "ascii";
            var packingAlgorithm = PackingAlgorithm.MaxRects;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--iterations":
                        iterations = int.Parse(NextArg(args, ref i, args[i]));
                        break;
                    case "-f":
                    case "--font":
                        fontPath = NextArg(args, ref i, args[i]);
                        break;
                    case "--system-font":
                        systemFontName = NextArg(args, ref i, args[i]);
                        break;
                    case "-s":
                    case "--size":
                        size = int.Parse(NextArg(args, ref i, args[i]));
                        break;
                    case "-c":
                    case "--charset":
                        charsetPreset = NextArg(args, ref i, args[i]);
                        break;
                    case "--packer":
                        packingAlgorithm = NextArg(args, ref i, args[i]).ToLowerInvariant() switch
                        {
                            "maxrects" => PackingAlgorithm.MaxRects,
                            "skyline" => PackingAlgorithm.Skyline,
                            var p => throw new ArgumentException($"Unknown packer: {p}. Use maxrects or skyline.")
                        };
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[i]}");
                }
            }

            if (fontPath == null && systemFontName == null)
            {
                ConsoleOutput.WriteError("--font or --system-font is required.");
                return ExitCodes.InvalidArguments;
            }

            if (fontPath != null && !File.Exists(fontPath))
            {
                ConsoleOutput.WriteError($"Font file not found: {fontPath}");
                return ExitCodes.InvalidArguments;
            }

            if (iterations < 1)
            {
                ConsoleOutput.WriteError("--iterations must be at least 1.");
                return ExitCodes.InvalidArguments;
            }

            var characters = charsetPreset.ToLowerInvariant() switch
            {
                "ascii" => CharacterSet.Ascii,
                "extended" => CharacterSet.ExtendedAscii,
                "latin" => CharacterSet.Latin,
                _ => CharacterSet.FromChars(charsetPreset)
            };

            var genOptions = new FontGeneratorOptions
            {
                Size = size,
                Characters = characters,
                PackingAlgorithm = packingAlgorithm,
            };

            var fontDisplay = fontPath ?? systemFontName!;
            Console.WriteLine($"Benchmark: {fontDisplay} @ {size}px, {charsetPreset} charset, {iterations} iterations");

            // Warmup
            Console.Write("Warming up... ");
            RunGeneration(fontPath, systemFontName, genOptions);
            Console.WriteLine("done.");

            // Timed iterations
            Console.Write($"Running {iterations} iterations");
            var times = new List<double>(iterations);
            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                RunGeneration(fontPath, systemFontName, genOptions);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                Console.Write(".");
            }
            Console.WriteLine();

            var mean = times.Average();
            var min = times.Min();
            var max = times.Max();
            var stddev = Math.Sqrt(times.Average(t => Math.Pow(t - mean, 2)));

            Console.WriteLine();
            Console.WriteLine($"  Min:    {min,8:F1}ms");
            Console.WriteLine($"  Mean:   {mean,8:F1}ms");
            Console.WriteLine($"  Max:    {max,8:F1}ms");
            Console.WriteLine($"  StdDev: {stddev,8:F1}ms");
            Console.WriteLine($"  Iterations: {iterations}");

            return ExitCodes.Success;
        }
        catch (ArgumentException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (FileNotFoundException ex)
        {
            ConsoleOutput.WriteError($"File not found: {ex.FileName ?? ex.Message}");
            return ExitCodes.FileNotFound;
        }
        catch (FontParsingException ex)
        {
            ConsoleOutput.WriteError($"Font error: {ex.Message}");
            return ExitCodes.FontParseError;
        }
        catch (InvalidOperationException ex)
        {
            ConsoleOutput.WriteError($"Generation error: {ex.Message}");
            return ExitCodes.GenerationError;
        }
    }

    private static void RunGeneration(string? fontPath, string? systemFontName, FontGeneratorOptions options)
    {
        if (systemFontName != null)
            BmFont.GenerateFromSystem(systemFontName, options);
        else
            BmFont.Generate(fontPath!, options);
    }

    private static string NextArg(string[] allArgs, ref int i, string flag)
    {
        i++;
        if (i >= allArgs.Length)
            throw new ArgumentException($"Missing value for {flag}");
        return allArgs[i];
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Benchmark font generation performance.

            Usage: bmfontier benchmark -f <font> -s <size> [options]

            Options:
              -f, --font <path>           Font file path (TTF, OTF, WOFF)
              --system-font <name>        Use a system-installed font by family name
              -s, --size <n>              Font size in pixels (default: 32)
              -c, --charset <preset>      Character set: ascii (default), extended, latin
              --packer <maxrects|skyline>  Packing algorithm (default: maxrects)
              --iterations <n>            Number of timed iterations (default: 10)

            Runs generation N+1 times (first run is warmup) and reports timing statistics.
            No output files are written.

            Examples:
              bmfontier benchmark -f arial.ttf -s 32
              bmfontier benchmark -f roboto.ttf -s 48 --charset latin --iterations 20
            """);
    }
}
