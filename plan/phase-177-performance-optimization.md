# Phase 177 — Native Rasterizer: Benchmarks & Performance Optimization

> **Status**: In progress — harness, baseline, scanline fill, dropout control, metrics shortcut, pooling, Brotli, the feature-path and core-pipeline passes and their measured results done; remaining: composite/varied `glyf` scratch, parallelism
> **Created**: 2026-04-01
> **Depends on**: nothing for the harness; the feature phases (166–174) for the optimisation pass

## Goal

1. **Harness (Wave 0)** — a BenchmarkDotNet comparison of Native vs StbTrueType vs FreeType, so each wave's cost is visible as it lands.
2. **Optimisation (Wave 3)** — reach the release target: within 2× of StbTrueTypeSharp for common workloads (Phase 160 Q9 set 3× for the MVP).

## Starting point

- Native allocates per glyph: outline command and edge lists, `glyf` flag arrays. The scanline fill allocates only the output bitmap, which is intentionally fresh (Phase 160 D11); its delta buffer comes from `ArrayPool`.
- WASM rule (Phase 160): no `.Result`/`.Wait()` or `Reflection.Emit` in Native; parallelism only behind an opt-out option that is forced off on browser-wasm and other single-threaded targets.

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

- **Metrics shortcut**: an untransformed, unhinted, default-instance simple `glyf` glyph is measured from its header box and contour end points without building its outline (`IOutlineDecoder.TryDecodeBounds` → `GlyphPipeline.TryPrepareBox`, same `GlyphBox.Compute` and `GlyphLimits`), only for renderers whose metrics depend on the box alone (`IBoxMeasuringRenderer.MeasuresFromBox`: coverage except `None`, which crops to ink; SDF; MSDF). Composites, CFF, styles, hinting, LCD, color and varied instances take the full path. To catch a stale header box, the shortcut also reads the glyph's stored coordinates (one fused min/max pass, no outline) and falls back to the full path when the header misses them; that costs about 320 ns/glyph against stb_truetype's 106 ns at ASCII 32px (a separate run from the table below, which predates the check). `NativeMetricsEquivalenceTests` compares metrics with rendered metrics for every mapped codepoint of Roboto, Source Serif, Roboto Flex (default and wght 1000) and Bungee Color in 11 modes. Caching prepared glyphs was rejected: core calls metrics (`QueryAtlasSize`) and rasterization (`Generate`) on separate rasterizer instances, so a glyph is never decoded twice by one instance.
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

## Feature paths (done)

Profiled per path (stage timing in a scratch harness, `dotnet-trace` samples, allocation per stage), then changed only what the profile showed. Every change is output-neutral: byte-identical fingerprints of rendered bitmaps and metrics for every mode over five fonts (variable instances included), 7-72 px, ASCII + Latin-1, plus a 510,543-glyph `None` sweep (6-96 px, fractional sizes, hinted, bold-italic), and goldens unchanged.

| Path | Profile finding | Change | Allocation per glyph |
|---|---|---|---|
| `None` | dropout control ~60% of the glyph: three `Reach` evaluations per edge per pass, padded scanline ranges that tripled bucket entries, a general sort per scanline | each edge oriented once per pass; ranges trimmed to the scanlines the crossing test accepts; insertion sort; branchless 50% threshold | unchanged |
| CBDT (Noto Color Emoji, 32 px) | resample ~60% and a 278 KB premultiplied copy of the strike (large-object heap); decode ~35% | one premultiplied source row at a time, pooled filter buffer, alpha table; pooled IDAT and scanline buffers; 4-byte palette copies | 464 → 83 KB (the rest is the decoder's RGBA result) |
| Light hinting | the fit (~2.2 µs) copied its key lists on every attempted placement | in-place key map with rollback; pooled per-slot arrays | 6.5 → 5.1 KB (metrics 5.7 → 4.3 KB) |
| `COLR` v0 metrics | base and every layer fully decoded to union the layer boxes | layers measured by the header-box shortcut, composites prepared | 7.3 → 5.4 KB |
| LCD | filter ~35%: a bounds-tested tap loop and an integer division per subpixel | whole-row filter, level table built with the same rounding | unchanged |
| MSDF | artifact correction ~50%: ~12,700 probes per glyph | probes skipped where two channels at all four corners fix the median's sign (exact in float arithmetic) | unchanged |
| SDF (and MSDF's true distance) | `Splat`'s column loop | lane-wise `Vector<float>` over columns, scalar fallback where not accelerated; vector path held bit-identical to scalar by test | unchanged |
| `COLR` v1 | `Math.Pow` per channel in sRGB encode (~13 µs of the glyph) | 255 thresholds derived from the formula, binary search; checked equal over every float in [0, 1] | unchanged |
| Metrics: CFF2, bold | charstring stacks (513 doubles) per glyph; emboldener lists and arrays per contour | pooled | CFF2 6.3 → 0.7 KB, bold 6.3 → 1.4 KB |
| Grayscale | flattened edges copied to an exact-size array per glyph | renderers read the pooled buffer | 1,465 → 1,072 B |

Timings for these paths are in [Measured results](#measured-results) below.

Measured and not kept:

- **Vector outlines** (`OutlineLayer`): sub-sample distances and inside tests are ~60%, building the distance shape ~23% (much of it allocation). Batched inside tests per sub-sample line and nearest-segment-first pruning (44% fewer distance evaluations) both measured slower; the bookkeeping cost more than the evaluations saved.
- **`gvar` scratch pooling**: 17% less allocation but slower, per-tuple clearing and the pooling bracket outweighing the small arrays.
- **Probe-table precomputation** in MSDF correction: within noise.

Remaining, with the profile behind each: composite and varied `glyf` glyphs still decode through `ParsedGlyph` (3.5 KB per composite, 2.5 KB per `gvar` glyph); the MSDF field's own `Splat` (~30%) is per-channel and not vectorized; `ScanlineRasterizer.Resolve` column-interleaved SIMD, band-bucketed edges for multi-band glyphs, SDF buffer pooling, `GlyphTransforms` caching and PNG CRC slicing are untried.

## Measured results

BenchmarkDotNet default job, `net10.0-windows`, the reference machine idle. Before is `1e028fc` (after the pooling and Brotli pass); after is `81add73`, with the feature-path and core-pipeline passes (the core pass — packing, GPOS kerning, parsing, effects and compositing — is in the CHANGELOG). Rows not listed moved within ±6% in one run; the rows that looked slower there (`LoadFont` on every backend, FreeType 12 px `None`, and Native gvar, italic 30° and LCD metrics, 5-21%) measured 0.97-1.00× over five alternating rounds. This run caught one regression, fixed since: the `COLR` v0 metrics shortcut read each v1 glyph's paint graph only to decline it (+14%, 1,696 → 2,120 B); it now checks for a v1 record instead, and that row is the mean of three alternating runs.

`BmFont.Generate`, Roboto 32 px:

| Backend | ASCII | ASCII + Latin-1 | Allocation, ASCII + Latin-1 |
|---|---|---|---|
| Native | 1.22 → 0.37 ms (3.3×) | 3.95 → 1.02 ms (3.9×) | 1,089 → 819 KB |
| StbTrueType | 1.38 → 0.60 ms (2.3×) | 4.14 → 1.35 ms (3.1×) | 928 → 739 KB |
| FreeType | 1.65 → 0.74 ms (2.2×) | 4.48 → 1.56 ms (2.9×) | 924 → 735 KB |
| DirectWrite | 2.61 → 1.97 ms (1.3×) | 7.61 → 3.95 ms (1.9×) | 1,050 → 861 KB |
| GDI | 4.31 → 3.60 ms (1.2×) | 10.09 → 6.97 ms (1.4×) | 948 → 759 KB |

Native feature paths, per glyph (`RasterizeAll`; metrics are `GetGlyphMetrics`):

| Path | Time | Allocation |
|---|---|---|
| Grayscale 32 px | 1.85 → 1.69 µs | 1,489 → 1,136 B |
| `None` 12 / 32 / 72 px | 4.18 → 3.00, 7.86 → 5.60, 15.3 → 11.4 µs | 1,182 → 862, 1,505 → 1,102, 2,800 → 2,254 B |
| LCD | 8.10 → 6.87 µs | 3,617 → 3,064 B |
| Light hinted (metrics) | 4.58 → 4.24 µs (2.74 → 2.66 µs) | 6,525 → 4,783 B (5,710 → 4,319 B) |
| SDF 12 / 32 / 72 px | 41.6 → 22.2, 67.1 → 35.5, 107 → 60.2 µs | unchanged |
| MSDF, MTSDF | 377 → 243 µs | unchanged |
| CBDT emoji, 32 px | 160 → 113 µs | 453 → 81 KB |
| `COLR` v1 (metrics) | 12.5 → 5.9 µs (643 → 640 ns) | 8,814 → 8,507 B (1,696 → 1,696 B) |
| `COLR` v0 metrics | 2.80 → 1.84 µs | 7,258 → 5,381 B |
| CFF (metrics) | 2.74 → 2.46 µs (876 → 785 ns) | 3,893 → 1,329 B (2,962 → 848 B) |
| CFF2 wght 900 (metrics) | 2.63 → 2.27 µs (1,032 → 877 ns) | 7,171 → 1,224 B (6,323 → 734 B) |
| Bold (metrics) | 3.35 → 2.93 µs (1.52 → 1.32 µs) | 7,225 → 1,946 B (6,334 → 1,414 B) |

`BmFont.Generate` ASCII 32 px with a feature: Native SDF 7.03 → 3.37 ms, MSDF 31.2 → 19.7 ms, outline 2 as effect 1.69 → 0.73 ms (FreeType 2.08 → 1.10, StbTrueType 1.90 → 0.95), outline 2 as `OutlineMode.Vector` 12.1 → 11.3 ms.

Cold start (`KernSmith.ColdStart`, Roboto ASCII 32 px, 20 fresh processes per cell, medians; steady is calls 30-50 of one process, font bytes to PNG-encoded pages, so not comparable with the warm numbers above):

| Backend | JIT 1st call | ReadyToRun 1st | NativeAOT 1st | JIT steady | ReadyToRun steady | NativeAOT steady |
|---|---|---|---|---|---|---|
| Native | 128 → 109 ms | 62.6 → 45.4 ms | 11.6 → 3.7 ms | 8.4 → 7.0 ms | 6.5 → 4.1 ms | 4.1 → 2.9 ms |
| StbTrueType | 111 → 92 ms | 55.4 → 38.2 ms | 11.7 → 3.7 ms | 7.9 → 6.7 ms | 6.4 → 4.0 ms | 4.2 → 3.0 ms |
| FreeType | 109 → 89 ms | 58.9 → 39.9 ms | 12.8 → 4.6 ms | 7.8 → 6.2 ms | 6.6 → 3.9 ms | 4.3 → 2.9 ms |
| DirectWrite | 130 → 111 ms | 68.5 → 50.6 ms | 13.6 → 5.9 ms | 8.9 → 7.7 ms | 8.5 → 5.7 ms | 5.6 → 4.4 ms |
| GDI | 111 → 91 ms | 60.5 → 43.1 ms | 14.7 → 6.9 ms | 11.6 → 9.5 ms | 10.0 → 7.0 ms | 7.0 → 5.8 ms |

Under JIT the first call is 10-15× steady state, most of it compiling 830-1,170 methods; ReadyToRun halves it and NativeAOT removes it (1-2× steady). Process start to first result, after: 146-169 ms JIT, 93-104 ms ReadyToRun, 17-20 ms NativeAOT.

## Work items — optimisation (Wave 3)

Profile first (`dotnet-trace`/PerfView); do only what the profile justifies.

- **Parsing**: composite and varied `glyf` glyphs through the scratch path; lazy table parsing.
- **Feature paths**: done (see Feature paths above); what remains is listed there with its profile.
- **Parallelism** (decided, not started): allowed where it helps, behind an opt-out option — on by default where supported, forced off on browser-wasm (`OperatingSystem.IsBrowser()`) and any other single-threaded target; one buffer set per worker. Core already parallelizes post-processing, so measure first whether backend-level parallelism adds anything.

## Testing

- Low-variance benchmark runs; results recorded here
- Zero per-glyph scratch allocations in the coverage hot path (`GC.GetAllocatedBytesForCurrentThread`)
- No quality change: SSIM and golden masters unchanged; `regression_check.py` identical
- Browser and other single-threaded targets execute no parallel code, whatever the option says; turning the option off gives byte-identical output

**Regression coverage**: optimisation must be output-neutral, so the existing configs must report Native identical to `main`; no new config needed. The one exception is the scanline-fill rewrite, whose rounding-level grayscale deltas are measured above.

## Success Criteria

- [x] Harness compares Native, StbTrueType and FreeType with memory diagnostics; baseline recorded
- [x] Within 2× of StbTrueType for ASCII at 32px
- [x] No per-glyph scratch allocations in the hot path
- [x] SIMD paths only where measured faster
- [ ] No output change; WASM-safe
- [ ] All tests pass
