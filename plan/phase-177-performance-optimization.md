# Phase 177 — Native Rasterizer: Benchmarks & Performance Optimization

> **Status**: Future — benchmark harness Wave 0; optimisation Wave 3
> **Created**: 2026-04-01
> **Depends on**: nothing for the harness; the feature phases (166–174) for the optimisation pass

## Goal

1. **Harness (Wave 0)** — a BenchmarkDotNet comparison of Native vs StbTrueType vs FreeType, so each wave's cost is visible as it lands.
2. **Optimisation (Wave 3)** — reach the release target: within 2× of StbTrueTypeSharp for common workloads (Phase 160 Q9 set 3× for the MVP; neither has been measured yet — the Phase 164 benchmark was deferred to here).

## Starting point

- `benchmarks/KernSmith.Benchmarks/` references only core and the FreeType backend.
- Native allocates per glyph: outline command and edge lists, the prepared/active edge arrays, `glyf` flag arrays. Only the scanline `area`/`cover` buffers come from `ArrayPool`. The output bitmap is intentionally fresh (Phase 160 D11).
- WASM rule (Phase 160): no `Parallel.ForEach`, `.Result`/`.Wait()` or `Reflection.Emit` in Native.

## Work items — harness (Wave 0)

- Add `ProjectReference`s to the StbTrueType and Native backends.
- Scenarios, each parameterized by backend: ASCII (95 glyphs) at 16/32/64px; Latin Extended at 32px; a large CJK-style glyph set at 24px if a suitable committed font exists; single-glyph latency; font load.
- `[MemoryDiagnoser]` on all; report allocated bytes per glyph.
- Rasterizer-only benchmarks (calling `IRasterizer.RasterizeAll`) separate from full `BmFont.Generate`, so pipeline cost doesn't mask backend cost.
- Record the baseline ratios in this doc as the load-bearing numbers the targets are judged against.

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

- [ ] Harness compares Native, StbTrueType and FreeType with memory diagnostics; baseline recorded
- [ ] Within 2× of StbTrueType for ASCII at 32px
- [ ] No per-glyph scratch allocations in the hot path
- [ ] SIMD paths only where measured faster
- [ ] No output change; WASM-safe
- [ ] All tests pass
