using System.Collections.Concurrent;
using System.Diagnostics;
using Bmfontier.Cli.Config;
using Bmfontier.Cli.Utilities;
using Bmfontier.Font;

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

        // Run jobs
        int total = jobs.Count + parseFailures.Count;
        int totalJobs = jobs.Count;
        var totalSw = showTime ? Stopwatch.StartNew() : null;

        var results = new ConcurrentBag<(string ConfigPath, bool Success, string Message, long ElapsedMs)>();
        int completedCount = 0;
        var lockObj = new object();

        // Pre-add parse failures as failed results
        foreach (var (path, error) in parseFailures)
        {
            results.Add((path, false, error, 0));
        }

        // Pre-load and cache font data to avoid repeated system font resolution
        var fontCache = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var systemFontProvider = new DefaultSystemFontProvider();
        Console.WriteLine("Pre-loading fonts...");
        foreach (var (configPath, options) in jobs)
        {
            var fontKey = options.FontPath ?? options.SystemFontName;
            if (fontKey == null) continue;

            fontCache.GetOrAdd(fontKey, key =>
            {
                if (options.FontPath != null)
                {
                    Console.WriteLine($"  Loading: {key}");
                    return File.ReadAllBytes(key);
                }
                else
                {
                    Console.WriteLine($"  Loading system font: {key}");
                    return systemFontProvider.LoadFont(key)
                        ?? throw new FileNotFoundException($"System font not found: {key}");
                }
            });
        }

        Console.WriteLine($"Processing {total} job(s) (parallel: {parallel})...");

        // Suppress per-glyph verbose output in batch mode; batch prints its own status lines
        ConsoleOutput.SetQuiet(true);

        if (parallel > 1)
        {
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = parallel };
            Parallel.ForEach(jobs, parallelOptions, job =>
            {
                var (configPath, options) = job;
                var fontKey = options.FontPath ?? options.SystemFontName;
                var fontData = fontKey != null && fontCache.TryGetValue(fontKey, out var cached) ? cached : null;
                RunSingleJob(configPath, options, fontData, ref completedCount, totalJobs, results, lockObj);
            });
        }
        else
        {
            foreach (var (configPath, options) in jobs)
            {
                var fontKey = options.FontPath ?? options.SystemFontName;
                var fontData = fontKey != null && fontCache.TryGetValue(fontKey, out var cached) ? cached : null;
                RunSingleJob(configPath, options, fontData, ref completedCount, totalJobs, results, lockObj);
            }
        }

        totalSw?.Stop();

        // Summary
        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        Console.WriteLine();
        if (showTime && totalSw != null)
            Console.WriteLine($"Done. {succeeded} succeeded, {failed} failed in {totalSw.ElapsedMilliseconds}ms total");
        else
            Console.WriteLine($"Done. {succeeded} succeeded, {failed} failed");

        // Report failures at the end
        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Failures:");
            foreach (var f in failures)
                Console.Error.WriteLine($"  {f.ConfigPath}: {f.Message}");
        }

        return failed > 0 ? ExitCodes.InvalidArguments : ExitCodes.Success;
    }

    private static void RunSingleJob(
        string configPath,
        CliOptions options,
        byte[]? fontData,
        ref int completedCount,
        int total,
        ConcurrentBag<(string ConfigPath, bool Success, string Message, long ElapsedMs)> results,
        object lockObj)
    {
        try
        {
            var jobResult = GenerateCommand.RunJob(options, fontData: fontData);

            var idx = Interlocked.Increment(ref completedCount);
            lock (lockObj)
            {
                Console.WriteLine($"[{idx}/{total}] OK {configPath} -> {jobResult.OutputPath} ({jobResult.ElapsedMs}ms)");
            }

            results.Add((configPath, true, jobResult.OutputPath, jobResult.ElapsedMs));
        }
        catch (Exception ex)
        {
            var idx = Interlocked.Increment(ref completedCount);
            lock (lockObj)
            {
                Console.WriteLine($"[{idx}/{total}] FAIL {configPath} - {ex.Message}");
            }

            results.Add((configPath, false, ex.Message, 0));
        }
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
