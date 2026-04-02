# Phase 176 — Native Rasterizer: Supersampling & Height Stretch

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 164 (scanline rasterizer core)

## Goal

Implement native supersampling and height stretch directly in the rasterizer for higher quality output and BMFont-compatible vertical scaling.

## Scope

### Native Supersampling

The main pipeline already supports supersampling (render at Nx, downscale), but it happens OUTSIDE the rasterizer. Implementing it natively allows:
- Single rasterization pass at higher resolution (not separate render + scale)
- Better buffer management (rent larger buffer once)
- Potential for rotated-grid supersampling (RGSS)

**Implementation**:
1. When `RasterOptions.SuperSample > 1`, multiply target size by factor
2. Rasterize at the larger size using the standard scanline rasterizer
3. Box-filter downscale in a single pass:
   ```csharp
   for each output pixel (ox, oy):
       sum = 0
       for dy in 0..factor-1:
           for dx in 0..factor-1:
               sum += superBitmap[(oy*factor+dy) * superWidth + (ox*factor+dx)]
       output[oy * width + ox] = (byte)(sum / (factor * factor))
   ```
4. Use premultiplied alpha for RGBA downscaling

> **FontStashSharp insight:** stb_truetype includes lightweight 1D box prefilter kernels (`stbtt__h_prefilter`, `stbtt__v_prefilter`) as a cheaper alternative to full NxN supersampling. These blur along a single axis for subpixel positioning quality. Consider offering this as a "light" supersampling option (e.g., `SuperSample = 0` for prefilter-only) that gives most of the quality benefit of 2x supersampling at a fraction of the cost.

**RGSS (Rotated Grid Super Sampling)** — optional enhancement:
- Instead of regular NxN grid, sample at rotated positions
- Better diagonal quality with only 2×2 samples (4 samples vs 16 for 4x regular)
- More complex implementation, defer to optimization phase

### Native Height Stretch

Vertical scaling of rasterized output to match BMFont `heightPercent`:
1. Rasterize at normal size
2. Scale bitmap vertically using bilinear interpolation
3. Update metrics (height, bearingY) to match

Currently handled by `HeightStretchPostProcessor` outside the rasterizer. Moving it inside allows single-pass rendering.

### Integration

- Both features use the existing `RasterOptions` fields (`SuperSample`, plus height percent from `FontGeneratorOptions`)
- The main pipeline detects `HandlesOwnSizing` or similar capability to avoid double-processing

## Testing

- Supersampling 2x, 4x: verify smoother output vs 1x
- Downscale quality: verify no banding artifacts
- Height stretch: verify vertical scaling correct
- Performance: measure overhead of supersampling at each level
- Compare native supersampling vs pipeline supersampling (should be identical output)

## Success Criteria

- [ ] Native supersampling produces correct downscaled output
- [ ] Box filter downscale with premultiplied alpha for RGBA
- [ ] Height stretch produces correct vertically scaled output
- [ ] Performance acceptable (2x = ~4x slower, 4x = ~16x slower)
- [ ] All tests pass
