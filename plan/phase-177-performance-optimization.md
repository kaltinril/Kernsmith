# Phase 177 — Native Rasterizer: Benchmarks & Performance Optimization

> **Status**: In progress — benchmark harness and baseline done; optimisation Wave 3
> **Created**: 2026-04-01
> **Depends on**: nothing for the harness; the feature phases (166–174) for the optimisation pass

## Goal

1. **Harness (Wave 0)** — a BenchmarkDotNet comparison of Native vs StbTrueType vs FreeType, so each wave's cost is visible as it lands.
2. **Optimisation (Wave 3)** — reach the release target: within 2× of StbTrueTypeSharp for common workloads (Phase 160 Q9 set 3× for the MVP).

## Starting point

- Native allocates per glyph: outline command and edge lists, the prepared/active edge arrays, `glyf` flag arrays. Only the scanline `area`/`cover` buffers come from `ArrayPool`. The output bitmap is intentionally fresh (Phase 160 D11).
- WASM rule (Phase 160): no `Parallel.ForEach`, `.Result`/`.Wait()` or `Reflection.Emit` in Native.

## Harness (Wave 0)

`benchmarks/KernSmith.Benchmarks/NativeComparisonBenchmarks.cs`, every class parameterized by backend (Native, StbTrueType, FreeType), Roboto-Regular, `[MemoryDiagnoser]`:

- `NativeComparisonLoadFontBenchmarks` — create + `LoadFont` + dispose.
- `NativeComparisonRasterizeBenchmarks` — `IRasterizer.RasterizeAll` over ASCII (95) at 12/32/72px × Grayscale/None; `OperationsPerInvoke = 95`, so Mean and Allocated are per glyph.
- `NativeComparisonMetricsBenchmarks` — `GetGlyphMetrics` over ASCII at 32px, per glyph.
- `NativeComparisonEndToEndBenchmarks` — full `BmFont.Generate`, ASCII + Latin-1 (224) at 32px, so pipeline cost is visible next to backend cost.

Run (about 4 minutes): `dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeComparison* --job short`

Not covered: a CJK-scale glyph set (no committed CJK fixture).

## Baseline

Baseline on the reference dev machine: AMD Ryzen 7 5800X, .NET 10.0.14 x64 RyuJIT (AVX2), BenchmarkDotNet ShortRun (3 iterations — error bars are wide, treat differences under ~15% as noise). FreeType renders hinted (the `RasterOptions` default); Native and StbTrueType are unhinted.

Per-glyph `RasterizeAll`, ASCII:

| Size | AA | Native | StbTrueType | FreeType | Native / Stb | Native alloc | Stb alloc |
|---|---|---|---|---|---|---|---|
| 12 | Grayscale | 3.03 µs | 1.92 µs | 3.72 µs | 1.57× | 5,247 B | 174 B |
| 12 | None | 2.57 µs | 2.32 µs | 4.86 µs | 1.11× | 5,247 B | 174 B |
| 32 | Grayscale | 4.60 µs | 4.38 µs | 3.89 µs | 1.05× | 5,841 B | 435 B |
| 32 | None | 5.15 µs | 4.94 µs | 5.24 µs | 1.04× | 5,841 B | 435 B |
| 72 | Grayscale | 10.17 µs | 15.81 µs | 8.11 µs | 0.64× | 7,602 B | 1,626 B |
| 72 | None | 10.96 µs | 15.68 µs | 10.35 µs | 0.70× | 7,602 B | 1,626 B |

| Scenario | Native | StbTrueType | FreeType |
|---|---|---|---|
| `LoadFont` (create + load + dispose) | 20 µs / 32 KB | 97 µs / 299 KB | 75 µs / 299 KB |
| `GetGlyphMetrics` 32px, per glyph | 716 ns / 2,781 B | 113 ns / 0 B | 3,104 ns / 0 B |
| `BmFont.Generate` ASCII + Latin-1 32px | 4.85 ms / 1,930 KB | 4.38 ms / 928 KB | 5.11 ms / 924 KB |

The 2× ASCII-at-32px target is already met at baseline; the work is keeping it through the feature waves and closing the allocation gap. Rasterization is roughly a fifth of end-to-end `Generate` time (224 glyphs × ~4.6 µs ≈ 1 ms of ~4.9 ms); the rest is pipeline.

### Hotspot candidates

Per-stage split of a Native ASCII glyph (Stopwatch + `GC.GetAllocatedBytesForCurrentThread` around each stage; instrumentation inflates the small stages):

| Stage | 12px | 32px | 72px | Alloc / glyph (32px) |
|---|---|---|---|---|
| `glyf` parse | 20% | 13% | 7% | 1,424 B |
| Outline extract | 10% | 7% | 3% | 1,357 B |
| Flatten | 9% | 8% | 5% | 1,526 B |
| Scanline fill (incl. output bitmap) | 60% | 70% | 84% | 1,479 B |
| cmap + glyph box | <2% | <2% | <1% | 0 B |

1. **Scanline fill** dominates time at every size: per-glyph `new RasterEdge[]` for both the prepared and the active edge arrays, `Array.Sort` through an `IComparer<RasterEdge>`, and the per-row accumulate/sweep — the Scanline items below.
2. **Per-glyph allocation** is 13–30× StbTrueType's and spread evenly across parse (flag/coordinate/point arrays), extract and flatten (`List<T>` + `ToArray`), and scanline edge arrays; it doubles end-to-end `Generate` allocation and promotes to Gen1 — the Pooling item below.
3. **`GetGlyphMetrics`** is ~6× StbTrueType because it runs the full `glyf` parse + outline extract to get a bounding box (2.8 KB/glyph); it drives `BmFont.QueryAtlasSize`. A cached parse or a bbox-only path fixes it.

## Work items — optimisation (Wave 3)

Profile first (`dotnet-trace`/PerfView); do only what the profile justifies.

- **Pooling**: every internal per-glyph scratch buffer (edges, active edges, outline commands, parse temporaries) rented from `ArrayPool<T>.Shared` or reused on the instance (instances are single-threaded, D9); output bitmap stays unpooled.
- **Parsing**: span-based table access; cache cmap lookups; lazy table parsing; optional parsed-outline cache for repeated glyphs.
- **Scanline**: edge sort without LINQ; active-edge insertion sort; skip empty rows; vectorize the accumulate/clamp/convert pass and buffer clears with `Vector128/256` behind `IsHardwareAccelerated` (managed intrinsics only — no native code).
- **Feature paths**: SDF segment bucketing (Phase 169), `gvar` decode caching (Phase 171), COLR layer canvas reuse (Phase 172), as the harness shows them hot.
- **Parallelism**: optional and only outside the browser (`OperatingSystem.IsBrowser()` guard); one buffer set per worker. Core already parallelizes post-processing, so measure whether backend-level parallelism adds anything.

## Testing

- Low-variance benchmark runs; results recorded here
- Zero per-glyph scratch allocations in the coverage hot path (`GC.GetAllocatedBytesForCurrentThread`)
- No quality change: SSIM and golden masters unchanged; `regression_check.py` identical
- Browser path executes no parallel code

**Regression coverage**: optimisation must be output-neutral, so the existing configs must report Native identical to `main`; no new config needed.

## Success Criteria

- [x] Harness compares Native, StbTrueType and FreeType with memory diagnostics; baseline recorded
- [ ] Within 2× of StbTrueType for ASCII at 32px
- [ ] No per-glyph scratch allocations in the hot path
- [ ] SIMD paths only where measured faster
- [ ] No output change; WASM-safe
- [ ] All tests pass
