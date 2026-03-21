# Phase 13 — Batch CLI Generation

> **Status**: Complete
> **Created**: 2026-03-20
> **Goal**: Enable the CLI to process multiple font generation jobs in a single invocation, eliminating per-invocation .NET startup overhead and supporting parallel generation for asset pipelines.

---

## Current State

The CLI (`tools/Bmfontier.Cli`) processes one font generation job per invocation via `bmfontier generate`. Each invocation pays approximately 1100ms of .NET runtime startup overhead. The actual font generation typically takes only 4-6ms per font, so when generating multiple fonts (e.g., 18 test fonts), total wall time is dominated by startup: 18 x 1100ms = ~20s.

The CLI already supports loading generation settings from `.bmfc` configuration files via `--config <path>` on the `generate` command, and saving settings via `--save-config <path>`. The `BmfcParser` reads INI-like `.bmfc` files into `CliOptions`, and the `GenerateCommand` builds `FontGeneratorOptions` from those options and calls `BmFont.Generate()` or `BmFont.GenerateFromSystem()`.

Current command routing in `Program.cs` dispatches to `GenerateCommand.Execute(rest)` for the `generate` subcommand, alongside `benchmark`, `inspect`, `convert`, `list-fonts`, and `info`.

## Target State

A new `batch` command accepts multiple `.bmfc` file paths and runs all jobs in a single process invocation. Jobs can run sequentially or in parallel. Output path collisions are detected before any generation starts. One failed job does not block others. A summary reports success/failure counts and timing.

---

## Design

### Input Methods

The batch command supports multiple ways to specify jobs:

1. **Positional args**: `bmfontier batch a.bmfc b.bmfc c.bmfc`
2. **Glob patterns**: `bmfontier batch fonts/*.bmfc` (shell-expanded on Unix; the CLI should also expand globs internally for Windows compatibility)
3. **Job list file**: `bmfontier batch --jobs jobs.txt` (one `.bmfc` path per line, blank lines and `#` comments ignored)

All methods can be combined in a single invocation.

### Parallel Execution

- `--parallel <n>` flag controls concurrency (default: `1` = sequential)
- `--parallel 0` means use all available CPU cores (`Environment.ProcessorCount`)
- Use `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` for clean async parallelism
- Each job gets its own `BmFont.Generate()` call — no shared mutable state

**FreeType thread safety**: FreeType is NOT thread-safe per library/face handle. The current `BmFont.Generate()` creates a fresh `FreeTypeRasterizer` internally per call, which allocates its own `FT_Library` and `FT_Face`. This means parallel calls to `BmFont.Generate()` are safe as long as they do not share rasterizer instances. Verify this by confirming that `BmFont.Generate()` does not use any static mutable state.

### Output Collision Detection

Before running ANY jobs, validate that no two jobs would write to the same output path:

1. Parse all `.bmfc` files via `BmfcParser.Parse()` to extract `CliOptions`
2. Resolve output paths: if `OutputPath` is set, use it; otherwise derive from font filename (mirroring `GenerateCommand` logic)
3. Normalize paths: `Path.GetFullPath()`, then case-insensitive comparison on Windows (`StringComparer.OrdinalIgnoreCase`)
4. Check for duplicates — same resolved base path means the `.fnt` and `.png` files would collide
5. If collision detected: print error listing the conflicting `.bmfc` files and their resolved output path, then exit with non-zero code
6. This check happens BEFORE any generation starts (fail-fast)

### Progress Reporting

- Print job count at start: `Processing 18 jobs (parallel: 4)...`
- Per-job status line: `[3/18] OK a.bmfc -> output/font-a (12ms)`
- Failed jobs: `[5/18] FAIL b.bmfc - Font file not found: missing.ttf`
- Summary at end: `Done. 17 succeeded, 1 failed in 340ms total`
- With `--time`: include per-job timing in the status line (always shown for batch; `--time` adds total elapsed)
- Parallel output must not interleave — buffer each job's console output and print atomically using a lock or `ConcurrentQueue`

### Error Handling

- One failed job does NOT stop other jobs from running
- Collect all failures (config path + exception message) and report at the end
- Exit code: `0` if all succeeded, `1` if any failed
- If a `.bmfc` file cannot be parsed, treat it as a failed job (do not abort the entire batch)
- If the `--jobs` file does not exist, exit immediately with an error (no jobs to run)

---

## Tasks

### Phase 1 — Batch Command Skeleton

- [ ] Create `BatchCommand.cs` in `tools/Bmfontier.Cli/Commands/`
- [ ] Accept positional args as `.bmfc` file paths
- [ ] Accept `--jobs <file>` for a text file listing `.bmfc` paths (one per line, `#` comments, blank lines skipped)
- [ ] Expand glob patterns using `Directory.GetFiles()` with the pattern for Windows compatibility
- [ ] Accept `--parallel <n>` (default `1`, `0` = `Environment.ProcessorCount`)
- [ ] Accept `--time` flag for total elapsed timing
- [ ] Add `--help` with usage text
- [ ] Add `["batch", .. var rest] => BatchCommand.Execute(rest)` to `Program.cs` routing
- [ ] Update `ShowHelp()` in `Program.cs` to include the `batch` command

### Phase 2 — Output Collision Detection

- [ ] After collecting all `.bmfc` paths, parse each with `BmfcParser.Parse()`
- [ ] Resolve output paths using the same logic as `GenerateCommand` (font filename fallback, directory handling)
- [ ] Extract this output-path-resolution logic into a shared helper (avoid duplicating `GenerateCommand` lines 153-168)
- [ ] Normalize with `Path.GetFullPath()` and compare case-insensitively on Windows
- [ ] On collision: print which configs conflict and their shared output path, exit with `ExitCodes.InvalidArguments`

### Phase 3 — Sequential Execution Engine

- [ ] Implement the core job runner: iterate over parsed configs, call generation logic for each
- [ ] Extract the generation logic from `GenerateCommand.Execute()` (lines 64-198) into a reusable static method, e.g., `GenerateCommand.RunJob(CliOptions options)` returning a result object
- [ ] Collect per-job results: success (with timing) or failure (with exception message)
- [ ] Print per-job status lines as each completes
- [ ] Print summary at end

### Phase 4 — Parallel Execution

- [ ] When `--parallel` > 1, use `Parallel.ForEachAsync` with `ParallelOptions.MaxDegreeOfParallelism`
- [ ] Buffer per-job output (suppress `ConsoleOutput` writes during parallel runs; capture them)
- [ ] Use a lock or atomic counter for the `[n/total]` progress numbering
- [ ] Print buffered output atomically after each job completes
- [ ] Verify FreeType thread safety by confirming `BmFont.Generate()` creates isolated rasterizer instances

### Phase 5 — Polish

- [ ] Add `-q` / `--quiet` support (suppress per-job lines, only show summary)
- [ ] Add `-v` / `--verbose` support (show full generation output per job)
- [ ] Add `--dry-run` support (parse and validate all configs, show what would run, but do not generate)
- [ ] Add `--no-color` support (already handled globally in `Program.cs`)
- [ ] Write tests for collision detection logic
- [ ] Write integration test: batch with 3 configs, verify all outputs created

---

## Architectural Considerations

### Refactoring GenerateCommand

The core generation logic in `GenerateCommand.Execute()` (building `FontGeneratorOptions`, calling `BmFont.Generate()`, writing output) is currently monolithic. For batch mode to reuse it, extract a method like:

```csharp
internal static JobResult RunJob(CliOptions options)
```

This method should:
- Build `FontGeneratorOptions` from `CliOptions`
- Resolve the output path
- Call `BmFont.Generate()` / `BmFont.GenerateFromSystem()`
- Write output via `result.ToFile()`
- Return success/failure with timing

The existing `GenerateCommand.Execute()` becomes a thin wrapper that parses CLI args, calls `RunJob()`, and handles single-job output formatting.

### .bmfc File Format

The existing `BmfcParser` handles the full configuration format. Each `.bmfc` file specifies font path, size, charset, output path, effects, and all generation options under INI-style sections: `[font]`, `[rendering]`, `[characters]`, `[atlas]`, `[effects]`, `[kerning]`, `[variable]`, `[output]`. The `[output] path` key is the primary input for collision detection. If `[output] path` is absent, the output path derives from the font filename.

### Memory Considerations

Each font generation allocates atlas texture pages as large byte arrays (e.g., 1024x1024x4 = 4MB per page). With high parallelism on large fonts with many pages, memory usage could spike. The `--parallel` default of `1` (sequential) is safe. Document that high parallelism values require adequate RAM, especially for large character sets or high-resolution fonts.

### Glob Expansion

On Unix shells, `bmfontier batch fonts/*.bmfc` is expanded by the shell before the CLI sees it, so the CLI receives individual file paths. On Windows (cmd.exe, PowerShell), glob expansion does not happen automatically. The batch command should detect unexpanded glob patterns (containing `*` or `?`) in positional args and expand them via `Directory.GetFiles()`.

---

## File Changes Summary

| File | Change |
|------|--------|
| `tools/Bmfontier.Cli/Commands/BatchCommand.cs` | **New** — batch command implementation |
| `tools/Bmfontier.Cli/Commands/GenerateCommand.cs` | **Modify** — extract `RunJob()` method for reuse |
| `tools/Bmfontier.Cli/Program.cs` | **Modify** — add `batch` routing and help text |
| `tests/Bmfontier.Tests/Cli/BatchCommandTests.cs` | **New** — collision detection and batch execution tests |

---

## Success Criteria

1. `bmfontier batch *.bmfc` processes all matching files in one invocation
2. `bmfontier batch --jobs jobs.txt` reads configs from a job list file
3. Output collision detection catches conflicts before any work starts
4. `--parallel 4` runs 4 jobs concurrently with no output interleaving
5. One failed job does not block others; summary shows all failures
6. Total time for 18 sequential jobs is under 2s (vs ~20s with 18 separate invocations)
7. Total time for 18 jobs with `--parallel 4` is under 1s
8. `--dry-run` validates all configs without generating any files
9. Exit code is `0` when all jobs succeed, `1` when any job fails

---

## Related Plans

- **phase-12-pre-ship-polish.md** — CLI polish and final quality pass
- **master-plan.md** — overall project roadmap
