# Phase 169 — Native Rasterizer: SDF Generation

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration)

## Goal

Implement Signed Distance Field (SDF) generation directly from glyph outlines, producing higher-quality SDF output than stb_truetype's brute-force approach.

## Background

SDF encodes the distance from each texel to the nearest glyph edge. Positive = outside, negative = inside, zero = on the edge. At render time, a simple threshold gives crisp edges at any scale. SDF is critical for game engines and GPU text rendering.

## Scope

### Direct Distance Computation

For each texel center, compute the signed distance to the nearest outline segment:

1. **Point-to-line-segment distance**: Standard geometric formula
2. **Point-to-cubic-Bezier distance**: Find closest point on curve by solving derivative equation
   - Flatten cubic to line segments for distance computation (simpler, fast enough)
   - Or use analytic approach: minimize `|P - B(t)|²` → solve 5th degree polynomial
   - Recommended: flatten to segments (matches quality, much simpler)

3. **Sign determination**: Use winding number / ray casting
   - Cast horizontal ray from texel center
   - Count edge crossings (non-zero winding rule)
   - Inside = positive distance, outside = negative distance
   - This matches stb_truetype's convention where inside glyphs = high values (> onEdgeValue)

4. **Output encoding**: Map distance to [0, 255]:
   ```
   value = onEdgeValue + signedDistance * (128 / range)
   ```
   Where `signedDistance` is positive inside the glyph and negative outside. This means inside pixels have values > 128 (bright) and outside pixels have values < 128 (dark), matching stb_truetype and common shader expectations. `range` is the SDF range in pixels (default: 8).

### SDF Parameters

- `sdfRange`: distance range in pixels (default: 8 — values beyond ±range clamp to 0/255)
- `sdfScale`: output texel size relative to glyph size (default: 1.0)
- `onEdgeValue`: the byte value at the exact edge (default: 128)

### Integration

- When `RasterOptions.Sdf = true`, use SDF generation instead of coverage rasterization
- Output format: `PixelFormat.Grayscale8` (same as normal rasterization)
- Update `IRasterizerCapabilities.SupportsSdf = true`

### Performance Considerations

- Brute force (stb_truetype): O(texels × segments) — acceptable for typical glyphs
- For complex glyphs (CJK, emoji outlines): partition segments into a grid for O(texels × nearby_segments)
- Start with brute force (simplest), optimize if profiling shows bottleneck
- SDF typically renders at lower resolution than bitmap (1/4 to 1/2 size), so total work is less than it seems

## Testing

- SDF output for 'A': verify positive outside, negative inside, smooth gradient at edges
- SDF at edge: verify value ≈ onEdgeValue (128) at exact outline boundary
- SDF inside: verify values > 128 (bright = inside glyph body)
- SDF outside: verify values < 128 (dark = outside glyph body)
- Compare against StbTrueType SDF output
- Threshold test: applying threshold at 128 should produce clean binary image matching rasterized glyph
- Multiple sizes and ranges
- Round-trip: SDF → threshold → compare with rasterized bitmap (should be very similar)

## Success Criteria

- [ ] SDF generation produces correct signed distance values
- [ ] Edge values ≈ 128 (±1)
- [ ] Inside negative, outside positive
- [ ] Smooth distance gradient (no discontinuities)
- [ ] `SupportsSdf = true` in capabilities
- [ ] All tests pass
