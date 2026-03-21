# Phase 15 — Library Performance & Batch API

> **Status**: Planning
> **Created**: 2026-03-20
> **Goal**: Move performance optimizations (font caching, batch generation, parallelism) from the CLI into the NuGet library so ALL consumers benefit, not just CLI users.

---

## Problem

Performance optimizations are currently trapped in the CLI layer:

1. **System font resolution is uncached** — `BmFont.GenerateFromSystem()` creates a new `DefaultSystemFontProvider` per call, scanning the entire system fonts directory (~800ms on Windows). Any NuGet consumer calling this in a loop pays 800ms × N.

2. **No batch API** — generating multiple fonts requires N separate `BmFont.Generate()` calls. The CLI implemented font caching and parallel execution, but NuGet consumers have to reinvent this.

3. **No font pre-loading API** — consumers who know they'll generate multiple fonts from the same .ttf have no way to load once and reuse. `BmFont.Generate(byte[], options)` accepts bytes but there's no managed cache.

## Current State

### BmFont.GenerateFromSystem (the bottleneck)
```csharp
public static BmFontResult GenerateFromSystem(string fontFamily, FontGeneratorOptions? options = null)
{
    options ??= new FontGeneratorOptions();
    var provider = new DefaultSystemFontProvider(); // NEW instance every call
    var fontData = provider.LoadFont(fontFamily)    // Scans fonts directory
        ?? throw new FontParsingException($"System font '{fontFamily}' not found");
    return Generate(fontData, options);
}
```

### DefaultSystemFontProvider
- Scans platform font directories (C:\Windows\Fonts, /usr/share/fonts, etc.)
- Parses TTF name tables to match family names
- Has internal `_cachedFonts` with double-checked locking but creates a new instance each call, so the cache is useless
- `LoadFont()` returns `byte[]` — reads the entire file into memory

### CLI BatchCommand (what should move to library)
- Pre-loads fonts into a `ConcurrentDictionary<string, byte[]>` cache
- Creates a single shared `DefaultSystemFontProvider` instance
- Runs jobs via `Parallel.ForEach` with configurable `MaxDegreeOfParallelism`
- This logic belongs in the library

---

## Design

### 1. Cache DefaultSystemFontProvider as static singleton

`BmFont.GenerateFromSystem()` should use a shared static provider instance so the font directory scan happens once per process lifetime.

```csharp
private static readonly Lazy<DefaultSystemFontProvider> _systemFontProvider = new();

public static BmFontResult GenerateFromSystem(string fontFamily, FontGeneratorOptions? options = null)
{
    options ??= new FontGeneratorOptions();
    var fontData = _systemFontProvider.Value.LoadFont(fontFamily)
        ?? throw new FontParsingException($"System font '{fontFamily}' not found");
    return Generate(fontData, options);
}
```

This alone fixes the 800ms-per-call regression for any consumer.

### 2. Add FontCache class

A managed font cache that consumers can use for explicit pre-loading:

```csharp
namespace Bmfontier;

public sealed class FontCache
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Load from file path
    public void LoadFile(string path) { ... }

    // Load from system font name
    public void LoadSystemFont(string fontFamily) { ... }

    // Load raw bytes with a key
    public void Add(string key, byte[] fontData) { ... }

    // Get cached font data
    public byte[] Get(string key) { ... }

    // Check if font is cached
    public bool Contains(string key) { ... }

    // Number of cached fonts
    public int Count { get; }

    // Clear cache
    public void Clear() { ... }
}
```

### 3. Add BmFont.GenerateBatch() API

```csharp
public static BatchResult GenerateBatch(
    IReadOnlyList<BatchJob> jobs,
    BatchOptions? options = null)
```

Where:
```csharp
public sealed class BatchJob
{
    // Font source — one of these must be set
    public byte[]? FontData { get; init; }
    public string? FontPath { get; init; }
    public string? SystemFont { get; init; }

    // Generation options
    public FontGeneratorOptions Options { get; init; }
}

public sealed class BatchOptions
{
    public int MaxParallelism { get; init; } = 1; // 0 = ProcessorCount
    public FontCache? FontCache { get; init; }     // Optional shared cache
}

public sealed class BatchResult
{
    public IReadOnlyList<BatchJobResult> Results { get; init; }
    public int Succeeded { get; }
    public int Failed { get; }
    public TimeSpan TotalElapsed { get; }
}

public sealed class BatchJobResult
{
    public int Index { get; init; }
    public bool Success { get; init; }
    public BmFontResult? Result { get; init; }
    public Exception? Error { get; init; }
    public TimeSpan Elapsed { get; init; }
}
```

Internal implementation:
1. If `FontCache` is provided, use it. Otherwise create a temporary one.
2. Pre-load all fonts from jobs into the cache (deduplicating by font source)
3. Run jobs with `Parallel.ForEach` using `MaxParallelism`
4. Each job failure is captured, not thrown — batch continues
5. Return all results

### 4. Make CLI BatchCommand a thin wrapper

After the library has `GenerateBatch`, the CLI's `BatchCommand` becomes:
1. Parse .bmfc files into `BatchJob` list
2. Check output collisions (CLI concern, stays in CLI)
3. Call `BmFont.GenerateBatch(jobs, options)`
4. Print progress from results
5. Write files to disk

---

## Tasks

### Phase 1 — Static SystemFontProvider (quick win)
- [ ] Change `BmFont.GenerateFromSystem()` to use a static `Lazy<DefaultSystemFontProvider>`
- [ ] Verify thread safety of `DefaultSystemFontProvider` (it has `_lock` with double-checked locking, should be safe)
- [ ] This alone should drop repeated `GenerateFromSystem` calls from 800ms to <1ms

### Phase 2 — FontCache class
- [ ] Create `src/Bmfontier/Config/FontCache.cs` (use root `Bmfontier` namespace per convention)
- [ ] LoadFile, LoadSystemFont, Add, Get, Contains, Count, Clear
- [ ] Use `ConcurrentDictionary<string, byte[]>` internally
- [ ] Use the static `DefaultSystemFontProvider` for system font resolution
- [ ] Thread-safe for parallel batch usage

### Phase 3 — Batch API types
- [ ] Create `src/Bmfontier/Config/BatchJob.cs`
- [ ] Create `src/Bmfontier/Config/BatchOptions.cs`
- [ ] Create `src/Bmfontier/Output/BatchResult.cs` and `BatchJobResult.cs`
- [ ] All types in appropriate namespaces per CLAUDE.md conventions

### Phase 4 — BmFont.GenerateBatch() implementation
- [ ] Add `GenerateBatch(IReadOnlyList<BatchJob>, BatchOptions?)` to `BmFont.cs`
- [ ] Internal font cache: deduplicate font loading across jobs
- [ ] Parallel execution via `Parallel.ForEach` with `MaxDegreeOfParallelism`
- [ ] Capture per-job exceptions without stopping other jobs
- [ ] Populate BatchResult with timing, success/failure counts

### Phase 5 — Simplify CLI BatchCommand
- [ ] Refactor `BatchCommand` to build `BatchJob` list from parsed .bmfc files
- [ ] Call `BmFont.GenerateBatch()` instead of custom parallel loop
- [ ] Keep collision detection and progress output in CLI
- [ ] Keep file writing (ToFile) in CLI — library returns in-memory results
- [ ] Verify identical output/behavior

---

## Architectural Considerations

### Thread Safety
- `DefaultSystemFontProvider` already has `_lock` with double-checked locking for font enumeration — safe for static singleton
- `FontCache` uses `ConcurrentDictionary` — safe for parallel access
- Each `BmFont.Generate()` call creates its own `FreeTypeRasterizer` — no shared mutable state
- Parallel batch is safe as long as jobs don't share mutable options objects

### Namespace Placement
- `FontCache`, `BatchJob`, `BatchOptions` -> `Bmfontier` namespace (Config/ directory)
- `BatchResult`, `BatchJobResult` -> `Bmfontier.Output` namespace (Output/ directory)
- `BmFont.GenerateBatch()` -> existing `BmFont` class

### API Design Principle
The library API returns in-memory results (`BmFontResult`, `BatchResult`). File I/O (`ToFile`) is a convenience method on the result. The batch API follows this — it generates in memory, the consumer decides what to write to disk.

---

## Success Criteria

1. `BmFont.GenerateFromSystem("Arial", ...)` called 18 times takes <2s total (vs ~15s before)
2. `BmFont.GenerateBatch()` with 18 jobs + parallel 4 completes in <500ms
3. `FontCache` pre-loads 3 system fonts in <1s, subsequent `Get()` calls are <1ms
4. CLI `BatchCommand` uses library batch API — no custom parallel code in CLI
5. All existing tests pass unchanged

---

## Related Plans

- **phase-13-batch-cli.md** — CLI batch command (will become thin wrapper)
- **phase-14-benchmarking-profiling.md** — PipelineMetrics already in library
