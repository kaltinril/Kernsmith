using Bmfontier.Cli.Config;
using Bmfontier.Cli.Utilities;
using Bmfontier.Output;

namespace Bmfontier.Cli.Commands;

internal sealed class BatchCommand
{
    public static int Execute(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            ShowHelp();
            return ExitCodes.Success;
        }

        // Parse batch-specific args
        var configPaths = new List<string>();
        string? jobsFile = null;
        int parallel = 1;
        bool showTime = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--jobs":
                    i++;
                    if (i >= args.Length)
                    {
                        ConsoleOutput.WriteError("Missing value for --jobs");
                        return ExitCodes.InvalidArguments;
                    }
                    jobsFile = args[i];
                    break;
                case "--parallel":
                    i++;
                    if (i >= args.Length)
                    {
                        ConsoleOutput.WriteError("Missing value for --parallel");
                        return ExitCodes.InvalidArguments;
                    }
                    parallel = int.Parse(args[i]);
                    if (parallel == 0)
                        parallel = Environment.ProcessorCount;
                    break;
                case "--time":
                    showTime = true;
                    break;
                default:
                    // Positional arg: treat as .bmfc path or glob pattern
                    if (args[i].StartsWith('-'))
                    {
                        ConsoleOutput.WriteError($"Unknown option: {args[i]}");
                        return ExitCodes.InvalidArguments;
                    }
                    configPaths.Add(args[i]);
                    break;
            }
        }

        // Load paths from --jobs file
        if (jobsFile != null)
        {
            if (!File.Exists(jobsFile))
            {
                ConsoleOutput.WriteError($"Jobs file not found: {jobsFile}");
                return ExitCodes.FileNotFound;
            }

            foreach (var line in File.ReadAllLines(jobsFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                    continue;
                configPaths.Add(trimmed);
            }
        }

        // Expand glob patterns (for Windows compatibility; Unix shells pre-expand)
        var expandedPaths = new List<string>();
        foreach (var path in configPaths)
        {
            if (path.Contains('*') || path.Contains('?'))
            {
                var dir = Path.GetDirectoryName(path);
                var pattern = Path.GetFileName(path);
                var searchDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(dir);

                if (Directory.Exists(searchDir))
                {
                    var matches = Directory.GetFiles(searchDir, pattern);
                    Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
                    expandedPaths.AddRange(matches);
                }
            }
            else
            {
                expandedPaths.Add(Path.GetFullPath(path));
            }
        }

        if (expandedPaths.Count == 0)
        {
            ConsoleOutput.WriteError("No .bmfc config files specified. Provide paths as arguments or use --jobs <file>.");
            return ExitCodes.InvalidArguments;
        }

        // Parse all configs and check for output collisions
        var jobs = new List<(string ConfigPath, CliOptions Options)>();
        var parseFailures = new List<(string ConfigPath, string Error)>();

        foreach (var configPath in expandedPaths)
        {
            try
            {
                var options = BmfcParser.Parse(configPath);
                jobs.Add((configPath, options));
            }
            catch (Exception ex)
            {
                parseFailures.Add((configPath, ex.Message));
            }
        }

        // Report parse failures but continue with valid configs
        foreach (var (path, error) in parseFailures)
        {
            ConsoleOutput.WriteError($"Failed to parse {path}: {error}");
        }

        if (jobs.Count == 0)
        {
            ConsoleOutput.WriteError("No valid configs to process.");
            return ExitCodes.InvalidArguments;
        }

        // Output collision detection
        var outputMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (configPath, options) in jobs)
        {
            try
            {
                var resolved = Path.GetFullPath(GenerateCommand.ResolveOutputPath(options));
                if (!outputMap.TryGetValue(resolved, out var list))
                {
                    list = new List<string>();
                    outputMap[resolved] = list;
                }
                list.Add(configPath);
            }
            catch (Exception ex)
            {
                // If output path resolution fails, treat as a parse failure
                parseFailures.Add((configPath, $"Cannot resolve output path: {ex.Message}"));
            }
        }

        var collisions = outputMap.Where(kv => kv.Value.Count > 1).ToList();
        if (collisions.Count > 0)
        {
            ConsoleOutput.WriteError("Output path collision detected:");
            foreach (var collision in collisions)
            {
                Console.Error.WriteLine($"  {collision.Key}");
                foreach (var source in collision.Value)
                    Console.Error.WriteLine($"    <- {source}");
            }
            return ExitCodes.InvalidArguments;
        }

        // Build BatchJob list from parsed CliOptions
        var batchJobs = new List<BatchJob>();
        foreach (var (configPath, options) in jobs)
        {
            var characters = GenerateCommand.BuildCharacterSet(options);
            var genOptions = GenerateCommand.BuildGenOptions(options, characters);

            batchJobs.Add(new BatchJob
            {
                FontPath = options.FontPath,
                SystemFont = options.SystemFontName,
                Options = genOptions,
            });
        }

        // Call library batch API
        var batchOptions = new BatchOptions
        {
            MaxParallelism = parallel,
        };

        int total = jobs.Count + parseFailures.Count;
        Console.WriteLine($"Processing {total} job(s) (parallel: {parallel})...");

        // Suppress per-glyph verbose output in batch mode; batch prints its own status lines
        ConsoleOutput.SetQuiet(true);

        var batchResult = BmFont.GenerateBatch(batchJobs, batchOptions);

        // Write output files and print progress
        int succeeded = 0;
        int failed = parseFailures.Count;
        var failures = new List<(string ConfigPath, string Error)>(parseFailures);

        for (int i = 0; i < batchResult.Results.Count; i++)
        {
            var jobResult = batchResult.Results[i];
            var (configPath, options) = jobs[i];
            var outputPath = GenerateCommand.ResolveOutputPath(options);

            if (jobResult.Success)
            {
                try
                {
                    jobResult.Result!.ToFile(outputPath, options.OutputFormat);
                    Console.WriteLine($"[{i + 1}/{total}] OK {configPath} -> {outputPath} ({jobResult.Elapsed.TotalMilliseconds:F0}ms)");
                    succeeded++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{i + 1}/{total}] FAIL {configPath} - {ex.Message}");
                    failures.Add((configPath, ex.Message));
                    failed++;
                }
            }
            else
            {
                Console.WriteLine($"[{i + 1}/{total}] FAIL {configPath} - {jobResult.Error?.Message}");
                failures.Add((configPath, jobResult.Error?.Message ?? "Unknown error"));
                failed++;
            }
        }

        // Summary
        Console.WriteLine();
        if (showTime)
            Console.WriteLine($"Done. {succeeded} succeeded, {failed} failed in {batchResult.TotalElapsed.TotalMilliseconds:F0}ms total");
        else
            Console.WriteLine($"Done. {succeeded} succeeded, {failed} failed");

        // Report failures at the end
        if (failures.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Failures:");
            foreach (var f in failures)
                Console.Error.WriteLine($"  {f.ConfigPath}: {f.Error}");
        }

        return failed > 0 ? ExitCodes.InvalidArguments : ExitCodes.Success;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Process multiple .bmfc config files in a single invocation.

            Usage: bmfontier batch <config1.bmfc> [config2.bmfc ...] [options]

            Input:
              <paths>                    One or more .bmfc config file paths (supports glob patterns)
              --jobs <file>              Text file listing .bmfc paths (one per line, # comments)

            Options:
              --parallel <n>             Max parallel jobs (default: 1, 0 = all CPU cores)
              --time                     Show total elapsed time in summary

            Output collision detection:
              Before any generation starts, all output paths are checked for conflicts.
              If two configs would write to the same path, the batch is aborted.

            Error handling:
              A failed job does not stop other jobs from running.
              Exit code is 0 if all jobs succeed, 1 if any job fails.

            Examples:
              bmfontier batch a.bmfc b.bmfc c.bmfc
              bmfontier batch fonts/*.bmfc --parallel 4 --time
              bmfontier batch --jobs jobs.txt --parallel 0
            """);
    }
}
