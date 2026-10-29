# Phase 177 — Native Rasterizer: Benchmarks & Performance Optimization

> **Status**: In progress — harness, baseline, scanline fill, dropout control, metrics shortcut, per-glyph pooling and Brotli done; remaining: feature paths, parallelism
> **Created**: 2026-04-01
> **Depends on**: nothing for the harness; the feature phases (166–174) for the optimisation pass

## Goal

1. **Harness (Wave 0)** — a BenchmarkDotNet comparison of Native vs StbTrueType vs FreeType, so each wave's cost is visible as it lands.
2. **Optimisation (Wave 3)** — reach the release target: within 2× of StbTrueTypeSharp for common workloads (Phase 160 Q9 set 3× for the MVP).

## Starting point

- Native allocates per glyph: outline command and edge lists, `glyf` flag arrays. The scanline fill allocates only the output bitmap, which is intentionally fresh (Phase 160 D11); its delta buffer comes from `ArrayPool`.
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

1. **Scanline fill** dominated time at every size — done, see Scanline fill below.
2. **Per-glyph allocation** was 13–30× StbTrueType's, spread across parse (flag/coordinate/point arrays), extract and flatten (`List<T>` + `ToArray`) — done, see Metrics, pooling and Brotli below.
3. **Mono dropout control** (`DropoutControl`) dominated `None` — done, see Dropout control below.
4. **`GetGlyphMetrics`** was ~9× StbTrueType because it ran the full `glyf` parse + outline extract to get a bounding box (2.9 KB/glyph); it drives `BmFont.QueryAtlasSize` — done, see Metrics, pooling and Brotli below.

## Scanline fill (done)

`ScanlineRasterizer` is an independently derived exact-area accumulator (derivation in its class doc comment): one pooled per-row delta buffer whose prefix sum is the coverage, filled in bands of at most 64K cells by walking the edges in input order — no edge copies, sort or active-edge list — and resolved four rows at a time so the serial running sums overlap. The fill allocates only the output bitmap (`ScanlineRasterizerTests` pins this). A `Vector<float>` level-conversion pass measured within noise and is not used.

Output vs the previous fill (Roboto and Source Serif ASCII, 18 sizes 8-96px): grayscale and LCD differ by at most 1 level on 0.01-0.23% of pixels (float rounding of the same exact-area coverage); `None` is identical except one pixel whose coverage is exactly on the 50% threshold. Coverage truncates after a 1/1024-level bias; without it about 3% of solid pixels truncate to 254.

Before/after, `RasterizeAll` ASCII per glyph, both runs on the reference machine in the same session (other builds were running, so absolute numbers sit above the baseline table; compare within a row):

| Size | AA | Native before | Native after | Stb (after run) | Native / Stb | Native alloc before → after |
|---|---|---|---|---|---|---|
| 12 | Grayscale | 2.81 µs | 2.19 µs | 2.39 µs | 0.92× | 5,486 → 4,637 B |
| 32 | Grayscale | 4.10 µs | 3.49 µs | 5.34 µs | 0.65× | 6,081 → 4,981 B |
| 72 | Grayscale | 9.42 µs | 6.84 µs | 15.20 µs | 0.45× | 7,841 → 6,314 B |
| 12 | None | 8.12 µs | 8.33 µs | 2.10 µs | 3.96× | 11,696 → 10,772 B |
| 32 | None | 17.33 µs | 18.95 µs | 6.18 µs | 3.07× | 21,827 → 20,653 B |
| 72 | None | 37.19 µs | 43.66 µs | 16.77 µs | 2.60× | 42,835 → 41,234 B |

`None` is dominated by dropout control (hotspot 3); its time moved within the run's error bars. `BmFont.Generate` ASCII + Latin-1 32px: 6.14 ms / 1,987 KB before, 5.42 ms / 1,758 KB after.

## Dropout control (done)

`DropoutControl` buckets each edge once under the row and column scanlines its extent reaches (a counting sort into pooled offset/index arrays), so a scanline visits only the edges that can cross it; crossings go into pooled buffers and are sorted with `MemoryExtensions.Sort` (keys plus a struct payload, no comparer delegate). Both half-open crossing tests ride on one crossing as two winding steps, so each scanline is sorted and swept once instead of twice plus a merge. Crossings at equal positions are applied together, so exactly abutting contours form one span rather than two. It allocates nothing (`ScanlineRasterizerTests` pins `None` too).

Output is byte-identical to the previous implementation over Roboto, Source Serif and Source Code (ExtraLight default and CFF2 wght 900), printable ASCII at every size 6-96px, unhinted, hinted and synthetic bold+italic (102,648 glyphs).

`RasterizeAll` ASCII `None` per glyph on the reference machine, ShortRun; Stb from the after run. Other builds shared the machine, so compare within a row:

| Size | Native before | Native after | Stb | Native / Stb | Native alloc before → after |
|---|---|---|---|---|---|
| 12 | 8.12 µs | 4.22 µs | 2.03 µs | 2.08× | 10,772 → 4,687 B |
| 32 | 14.38 µs | 7.46 µs | 4.58 µs | 1.63× | 20,653 → 5,031 B |
| 72 | 42.92 µs | 14.64 µs | 12.81 µs | 1.14× | 41,234 → 6,364 B |

`None` costs about 2.7-2.9× `Grayscale` (it fills the same coverage, then walks every row and column scanline) and allocates the same as `Grayscale` plus its horizontal edges.

## Metrics, pooling and Brotli (done)

- **Metrics shortcut**: an untransformed, unhinted, default-instance simple `glyf` glyph is measured from its header box and contour end points without decoding (`IOutlineDecoder.TryDecodeBounds` → `GlyphPipeline.TryPrepareBox`, same `GlyphBox.Compute` and `GlyphLimits`), only for renderers whose metrics depend on the box alone (`IBoxMeasuringRenderer.MeasuresFromBox`: coverage except `None`, which crops to ink; SDF; MSDF). Composites, CFF, styles, hinting, LCD, color and varied instances take the full path. `NativeMetricsEquivalenceTests` compares metrics with rendered metrics for every mapped codepoint of Roboto, Source Serif, Roboto Flex (default and wght 1000) and Bungee Color in 11 modes. Caching prepared glyphs was rejected: core calls metrics (`QueryAtlasSize`) and rasterization (`Generate`) on separate rasterizer instances, so a glyph is never decoded twice by one instance.
- **Pooling**: simple `glyf` glyphs decode straight into commands through a per-decoder `GlyfScratch` (instances are single-threaded; core creates one per generation and `NativeThreadSafetyTests` covers concurrent generations); the extractor and flattener rent from `ArrayPool<T>.Shared` because renderers are shared singletons; the hint stage's closure is only built for hinted requests. A glyph now allocates its bitmap, outline commands, the exact edge array the fill takes and small result objects (`NativeAllocationTests`). Composite glyphs still go through `ParsedGlyph`.
- **cmap**: not optimised — the memoised `Dictionary` lookup is a small part of a 90 ns metrics call.
- **Brotli** (`Font/Brotli/`): whole-word bit refills, literal runs decoded with the bit buffer in locals and tables resolved per (block type, context) once per meta-block, a split context lookup instead of a per-literal mode switch, a precomputed command-length table, doubling copies for overlapping matches and a byte-reverse table for prefix-code building. Measured and dropped: a locals-based command reader and unchecked table reads (both within noise). Output is identical (round-trip corpus, truncation and bit-flip tests).

Before (custom_kern) / after, same session, ShortRun; Stb from the after run. Other agents' builds shared the machine, so compare within a row:

| Benchmark | Native before | Native after | Stb | Native alloc before → after |
|---|---|---|---|---|
| `GetGlyphMetrics` ASCII 32px, per glyph | 938 ns | 90 ns | 102 ns | 2,980 → 72 B |
| `RasterizeAll` 12px Grayscale, per glyph | 2.45 µs | 1.94 µs | 3.12 µs | 4,637 → 1,145 B |
| `RasterizeAll` 32px Grayscale, per glyph | 3.38 µs | 3.35 µs | 6.66 µs | 4,981 → 1,489 B |
| `RasterizeAll` 72px Grayscale, per glyph | 6.76 µs | 7.14 µs | 18.12 µs | 6,314 → 2,822 B |
| `RasterizeAll` 32px None, per glyph | 11.34 µs | 12.10 µs | 7.51 µs | 4,997 → 1,505 B |
| `BmFont.Generate` ASCII + Latin-1 32px | 5.86 ms | 5.45 ms | 6.66 ms | 1,751 → 1,081 KB (Gen1 31 → 16) |
| Managed Brotli, Roboto WOFF2 stream (126 KB → 257 KB) | 3.07 ms (2.34× BCL) | 2.55 ms (1.54× BCL) | BCL 1.31 / 1.65 ms | 517 → 537 KB |

Rasterization time is dominated by the fill and moved within the error bars; the remaining metrics allocation is ASCII's composite glyphs. Brotli is about 1.5× faster (3.74 → 2.35 ms in an earlier quieter run); the steady-state baseline was 2.3× the BCL decoder, not the ~6× first measured, so a 2× speedup would have meant matching native code.

Run the WOFF2 benchmark with `--filter *Woff2Decode*` (`Woff2DecodeBenchmarks`: managed vs BCL Brotli on the same stream, plus `ReadFontInfo` WOFF2 vs TTF).

## Work items — optimisation (Wave 3)

Profile first (`dotnet-trace`/PerfView); do only what the profile justifies.

- **Parsing**: composite `glyf` glyphs through the scratch path; lazy table parsing.
- **Feature paths**: SDF segment bucketing (Phase 169), `gvar` decode caching (Phase 171), COLR layer canvas reuse (Phase 172), as the harness shows them hot.
- **Parallelism**: optional and only outside the browser (`OperatingSystem.IsBrowser()` guard); one buffer set per worker. Core already parallelizes post-processing, so measure whether backend-level parallelism adds anything.

## Testing

- Low-variance benchmark runs; results recorded here
- Zero per-glyph scratch allocations in the coverage hot path (`GC.GetAllocatedBytesForCurrentThread`)
- No quality change: SSIM and golden masters unchanged; `regression_check.py` identical
- Browser path executes no parallel code

**Regression coverage**: optimisation must be output-neutral, so the existing configs must report Native identical to `main`; no new config needed. The one exception is the scanline-fill rewrite, whose rounding-level grayscale deltas are measured above.

## Success Criteria

- [x] Harness compares Native, StbTrueType and FreeType with memory diagnostics; baseline recorded
- [x] Within 2× of StbTrueType for ASCII at 32px
- [x] No per-glyph scratch allocations in the hot path
- [ ] SIMD paths only where measured faster
- [ ] No output change; WASM-safe
- [ ] All tests pass
