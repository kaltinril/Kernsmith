# Phase 168 — Native Rasterizer: Synthetic Outline/Stroke & Advanced Transforms

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 167 (synthetic bold/italic)

## Goal

Implement outline stroke generation and additional synthetic transforms (small caps, condensed/extended) at the outline level.

## Scope

### Outline Stroke Generation

Generate a stroked (outlined) version of a glyph — a hollow ring around the original shape.

**Algorithm**: Dual offset curves
1. Offset the original contour OUTWARD by `strokeWidth / 2`
2. Offset the original contour INWARD by `strokeWidth / 2`
3. Reverse the inner offset contour winding
4. Result: the area between the two offsets = the stroke

**Reuse**: The outward offset is the same code as synthetic bold (Phase 167). The inward offset reverses the direction.

**Join styles** (at corners):
- **Miter**: extend edges until they meet (with miter limit to prevent spikes)
- **Round**: arc cap at corners (approximate with line segments)
- **Bevel**: straight cut across the corner

Default: Miter with limit of 2×strokeWidth.

**Output**: A new `GlyphOutline` containing the stroke contours. This can be:
- Rasterized standalone (just the outline)
- Composited with the original glyph (outline behind, fill on top)

### Integration with Existing Effects

The outline stroke at the rasterizer level replaces the bitmap-level `OutlineEffect` when using the Native backend:
- `IRasterizerCapabilities.SupportsOutlineStroke = true`
- When the main pipeline sees `SupportsOutlineStroke`, it can request the rasterizer to generate the stroke directly
- Falls back to existing `OutlineEffect` (EDT-based) for other backends

### New RasterOptions Fields

These transforms need user-facing configuration. Add to `RasterOptions` or `FontGeneratorOptions`:

- `float WidthScale = 1.0f` — horizontal scaling (< 1 = condensed, > 1 = extended)
- `bool SmallCaps = false` — enable small caps transform
- Stroke width already exists as `FontGeneratorOptions.Outline`
- Bold/italic already exist as `RasterOptions.Bold`/`Italic`
- Italic angle: use font's `post.italicAngle` as default, or add `float SyntheticItalicAngle = 12f` to `RasterOptions`

### Small Caps Transform

Scale uppercase glyph outlines to simulate small caps:
1. Read x-height and cap-height from OS/2 table
2. Scale factor = x-height / cap-height (typically ~0.68-0.72)
3. Scale all outline points uniformly: `x *= scale, y *= scale`
4. Optional: slight boldening to compensate for thinner strokes after scaling

### Condensed / Extended Transform

Horizontal-only scaling:
```csharp
foreach (ref var point in outline.Points)
{
    point.X *= widthScale;  // < 1.0 = condensed, > 1.0 = extended
}
advanceWidth *= widthScale;
```

### Transform Pipeline (Updated)

```
GlyphOutline (font units)
  → Condensed/Extended (if requested)
    → Small Caps (if requested)
      → Synthetic Bold (if requested)
        → Synthetic Italic (if requested)
          → Outline Stroke (if requested, generates separate stroke contours in font units)
            → Scale to pixels
              → Flatten & Rasterize
```

Stroke width is specified in pixels by the user but converted to font units before applying: `strokeWidth_fu = strokeWidth_px * unitsPerEm / pixelSize` (same conversion as bold strength in Phase 167).

## Testing

- Stroke 'O': verify hollow ring, correct width
- Stroke with sharp corners: verify miter join behavior
- Small caps: verify scaling factor matches x-height/cap-height ratio
- Condensed/extended: verify horizontal scaling only
- Combined transforms: bold + italic + condensed
- Visual comparison of stroke vs bitmap-level OutlineEffect

## Success Criteria

- [ ] Outline stroke generates correct hollow ring
- [ ] Join styles work (miter default)
- [ ] Small caps transform uses OS/2 metrics
- [ ] Condensed/extended scales horizontally only
- [ ] `SupportsOutlineStroke = true` in capabilities
- [ ] All tests pass
