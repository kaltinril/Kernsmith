# Phase 169 — Native Rasterizer: SDF Generation

> **Status**: Complete
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch)

## Goal

Signed distance field output computed directly from the glyph outline (Phase 160 Q4 option C), not from a rasterized bitmap.

## Design

- `SdfRenderer` (`Internal/Pipeline/`) is returned by `GlyphRenderers.Select` when `RasterOptions.Sdf` is set; color is still checked (and rejected) first. `NativeCapabilities.SupportsSdf = true`.
- `DistanceShape` (`Internal/Distance/`) flattens the outline in field coordinates with Wang's bound at `spread/512` px (clamped to 1/1024..1/32 px), so every distance is within a quarter code value of the distance to the true curve. Horizontal segments are kept (unlike the coverage flattener).
- Distances are measured to the **boundary of the filled region** under the non-zero rule: contours are split wherever they cross, touch or overlap collinearly, and each piece survives only if exactly one side is filled. Overlapping contours therefore leave no interior seam; nested same-direction contours vanish.
- Sign is the non-zero winding number at each texel centre (`WindingIndex`, one-pixel bands), so contour direction (TrueType vs CFF) does not matter.
- Encoding matches FreeType's measured output: `128 ± ⌊|d|·128/spread⌋`, inside bright, clamped to 0..255.
- Spread = `SdfSpread` rounded half away from zero (min 1, no upper clamp — FreeType clamps to 2..32), the same rounding as the `.fnt` `sdfSpread` metadata. The glyph box is padded by the spread on every side and the bearings move to match; `Measure` reports exactly what `Render` produces.
- `SdfScale` stays a core concern (render at `Size × SdfScale`, box-filter down); the spread is in render pixels, as for FreeType.

## Performance

Each boundary piece updates only the texels inside its capsule of radius `spread` (row-clipped to the band around its line), so cost scales with outline length × spread, not texels × segments. Throwaway Stopwatch, Roboto printable ASCII, hinting off: 5-8x faster than FreeType's SDF renderer (e.g. 64px/spread 8: 60 ms vs 445 ms; 256px/spread 16: 374 ms vs 3142 ms).

## Testing

`SdfRendererTests`: exact fields for a rectangle (spreads 4/8/16), a cubic circle and a square ring; reversed contours give identical output; overlapping bars equal the single-contour plus; collinear overlaps and nested same-direction contours equal the union rectangle; padding per spread; `GetGlyphMetrics` equals the rendered glyph for every ASCII glyph; 1-Lipschitz field (no seams) at spreads 4/8/16; `'A'` monotone across each edge; threshold at 128 agrees with coverage wherever coverage is unambiguous; FreeType comparison (identical boxes and bearings, SSIM > 0.99, max |diff| ≤ 2); end-to-end generation at the `sdf.bmfc` settings with `sdfScale` 2 and 1. `NativeVariableDistanceFieldTests`: RobotoFlex at wght 900 — SDF and MSDF follow the instance, match FreeType where contours do not overlap, and show no interior seam where they do (FreeType's field does).

**Regression coverage**: `sdf.bmfc` (Arial 48, spread 6, `sdfScale=2`) and `sdf-scale1.bmfc` (spread 4, `sdfScale=1`) run on Native.

## Success Criteria

- [x] Signed distances correct; edge ≈ 128; inside bright (D13)
- [x] `SdfSpread` honored; field padded, metrics adjusted
- [x] No discontinuities; overlapping contours handled
- [x] `SupportsSdf = true`; `sdf.bmfc` runs on Native
- [x] All tests pass; `regression_check.py` run
