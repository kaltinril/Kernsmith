# Phase 169 — Native Rasterizer: SDF Generation

> **Status**: Future — Wave 1
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch)

## Goal

Signed distance field output computed directly from the glyph outline (Phase 160 Q4 option C), not from a rasterized bitmap.

## Starting point

- `NativeCapabilities.SupportsSdf = false`; `PreparedRasterization.Prepare` throws `NotSupportedException` for SDF on such a backend, and `GenerateAll` skips the (sdf.bmfc, native) pair.
- Existing options: `RasterOptions.Sdf`, `SdfSpread` (pixels, default 8), `SdfScale` (render-scale, applied by core — it multiplies `Size` and box-averages back down, so the backend never reads it).
- Behavioral baseline is **FreeType**, which honors `SdfSpread`. StbTrueType ignores `SdfSpread` (fixed padding 5, `pixel_dist_scale` 64), so it is a visual reference only.

## Work items

### Distance computation

1. Flatten the outline (existing `OutlineFlattener`, pixel space) and compute, per texel center, the unsigned distance to the nearest segment.
2. Sign from the non-zero winding number at the texel center (Phase 160 Q8), so overlapping contours are inside, not double-counted.
3. Encode with **inside positive / bright** (Phase 160 D13): `value = 128 + clamp(d / SdfSpread, -1, 1) * 127.5`, `d > 0` inside. Values beyond ±`SdfSpread` clamp to 0 / 255. Verify the exact mapping (128 edge, scale per pixel) against FreeType's SDF output before fixing it.
4. Pad the bitmap by `ceil(SdfSpread)` on every side and adjust `BearingX`/`BearingY` to match, as FreeType does, so the field is not clipped.
5. Output `PixelFormat.Grayscale8`.

### Performance

- Start brute force (texels × segments); add a uniform-grid segment bucket if Phase 177's harness shows SDF dominating. SDF configs usually render small (core `SdfScale` downsampling), so measure before optimising.

### Integration

- SDF branch in the Phase 166 render-mode dispatch; remove the `Sdf` case from `RejectUnsupported`.
- Works on any decoded/transformed outline, so bold/italic/variations compose for free.
- `SupportsSdf = true`; update Native README, `docs/rasterizers/native.md`, REF-12, and the README backend table.

## Testing

- `'A'`: edge ≈ 128 (±1), inside > 128, outside < 128, monotone gradient across the edge
- Threshold at 128 reproduces the coverage bitmap (SSIM vs the Native grayscale glyph)
- `SdfSpread` 4/8/16: clamp distance scales accordingly
- Compare against FreeType SDF at the same `SdfSpread` (SSIM; the threshold in Phase 179 is > 0.90)
- Overlapping-contour glyph: no false inside/outside seam

**Regression coverage**: the existing `sdf.bmfc` (Arial 48, `sdfSpread=6`, `sdfScale=2`) starts running on Native once `SupportsSdf` flips — it is currently skipped. Add a test that asserts Native produces SDF glyphs for that configuration, since the harness tolerates a failing pair.

## Success Criteria

- [ ] Signed distances correct; edge ≈ 128; inside bright (D13)
- [ ] `SdfSpread` honored; field padded, metrics adjusted
- [ ] No discontinuities; overlapping contours handled
- [ ] `SupportsSdf = true`; `sdf.bmfc` runs on Native
- [ ] All tests pass; `regression_check.py` run
