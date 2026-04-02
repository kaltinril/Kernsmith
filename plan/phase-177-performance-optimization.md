# Phase 177 — Native Rasterizer: Performance Optimization

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phases 161–176 (all feature phases)

## Goal

Optimize the Native rasterizer for production performance. Target: within 2x of StbTrueTypeSharp for common workloads, competitive with FreeType for large glyph sets.

## Performance Target History

- **Phase 160 (initial)**: Within 3x of StbTrueTypeSharp — acceptable for first implementation
- **Phase 177 (this phase)**: Tighten to within 2x of StbTrueTypeSharp — production-ready target

This phase supersedes Phase 160 Q9's initial 3x target. The 3x target applies during Phases 161-165 development; this phase tightens it for release.

## Scope

### Profiling & Benchmarking

Before optimizing, establish baselines:
1. Add BenchmarkDotNet benchmarks to `benchmarks/KernSmith.Benchmarks/`
2. Benchmark scenarios:
   - ASCII set (95 glyphs) at 16, 32, 64 px
   - Latin Extended set (~600 glyphs) at 32 px
   - CJK subset (1000 glyphs) at 24 px
   - Single glyph rasterization (per-glyph latency)
3. Compare against FreeType and StbTrueType backends
4. Profile with `dotnet-trace` / PerfView to find hotspots

### Memory Optimization

- **Buffer pooling**: Ensure ALL internal buffers use `ArrayPool<T>.Shared`
- **Coverage buffer reuse**: Single pair of area/cover buffers reused across all scanlines and glyphs
- **Span-based parsing**: Zero-allocation font table access via `ReadOnlySpan<byte>`
- **Struct-based types**: Ensure edge segments, points, commands are value types (no heap allocation per glyph)
- **Pre-allocate edge list**: Estimate edge count from maxPoints, pre-allocate array

### Algorithmic Optimization

- **Edge sorting**: Use `Array.Sort` with custom comparer (avoid LINQ)
- **Active edge list**: Use insertion sort (list is nearly sorted between scanlines)
- **Empty scanline skip**: Track min/max active edge Y, skip scanlines outside range
- **Glyph bbox clipping**: Only allocate bitmap for glyph bounding box, not full em square
- **Cache cmap lookups**: Build a dictionary for O(1) codepoint→glyphIndex

### SIMD Acceleration (if measurable benefit)

- **Cumulative sum**: The final scanline pass is a perfect SIMD target
  ```csharp
  // The prefix sum itself is sequential, but the clamp + byte conversion
  // can be vectorized. The main SIMD win is in the final pass:
  // clamp |area[x] + runningCover| to [0,255] and convert to byte.
  // Also vectorize the buffer reset (zeroing area/cover arrays).
  ```
- **Bezier flattening**: Parallel evaluation of multiple curves (batch mode)
- **Box filter downscale**: SIMD averaging for supersampling
- Use `System.Runtime.Intrinsics` with `Vector128`/`Vector256` and `IsHardwareAccelerated` guard

### Font Loading Optimization

- **Lazy table parsing**: Only parse tables when first accessed
- **Glyph cache**: Cache parsed outlines for glyphs requested more than once
- **Outline pool**: Reuse `GlyphOutline` arrays across rasterization calls

### Parallel Rasterization (non-WASM only)

- Add optional parallel mode for batch rasterization:
  ```csharp
  if (!IsWasm && glyphCount > parallelThreshold)
      Parallel.ForEach(glyphs, glyph => RasterizeGlyph(glyph));
  ```
- Each parallel task gets its own buffer set (no sharing)
- Guard with `OperatingSystem.IsBrowser()` check for WASM safety

## Testing

- Benchmark suite produces consistent results (low variance)
- Performance within 2x of StbTrueType for ASCII at 32px
- Memory allocation: measure with `GC.GetAllocatedBytesForCurrentThread()`
- No regressions in output quality (SSIM unchanged)
- WASM: verify no parallel code paths execute

## Success Criteria

- [ ] Comprehensive benchmark suite established
- [ ] Performance within 2x of StbTrueType baseline
- [ ] Zero per-glyph heap allocations in hot path
- [ ] SIMD acceleration provides measurable speedup (if applicable)
- [ ] No quality regressions
- [ ] WASM-safe (no parallel paths)
- [ ] All tests pass
