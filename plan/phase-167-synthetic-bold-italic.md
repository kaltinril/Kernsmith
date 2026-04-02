# Phase 167 — Native Rasterizer: Synthetic Bold & Italic

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration)

## Goal

Implement synthetic bold and synthetic italic at the outline level, operating on glyph outlines before rasterization. This is a key advantage of owning the rasterizer — transforms happen in vector space for maximum quality.

## Scope

### Synthetic Bold (Outline Expansion)

**Algorithm**: Perpendicular normal offset

For each contour:
1. Determine winding direction (signed area of contour):
   - Clockwise (outer contour in TrueType): expand outward
   - Counter-clockwise (inner contour/counter): shrink inward
2. For each on-curve point:
   - Compute the outward-facing normal (average of adjacent segment normals)
   - Offset the point along the normal by `boldStrength` pixels (in font units)
3. For off-curve control points:
   - Offset along interpolated normal direction
4. Update glyph advance width in font units: `advance += boldStrength_fu` (small increase for weight). Pixel advance is recalculated after scaling, same as non-bold glyphs.

**Computing normals at a point**:
- Get tangent vectors from adjacent segments (incoming and outgoing)
- Rotate each tangent 90° to get normals
- Average the two normals
- Normalize to unit length
- Scale by bold strength
- For miter joins at sharp corners: scale by `1 / cos(halfAngle)`, capped at 2× to prevent spikes

**Bold strength**: Controlled by a configurable parameter. Default: `Math.Max(1, pixelSize / 24)` pixels.

Conversion to font units: `strength_fu = strength_px * unitsPerEm / pixelSize`. All offset operations happen in font units (per Phase 160 D4). The pixel-domain default is only used to compute a reasonable font-unit value.

**Winding direction detection**:
```csharp
// Signed area via Shoelace formula — in TrueType's Y-up coordinate system:
// Positive result = counter-clockwise (inner contour / counter)
// Negative result = clockwise (outer contour)
float signedArea = 0;
for (int i = 0; i < points.Length; i++)
{
    var p0 = points[i];
    var p1 = points[(i + 1) % points.Length];
    signedArea += p0.X * p1.Y - p1.X * p0.Y;
}
bool isOuter = signedArea < 0; // clockwise in Y-up = outer contour
```

### Synthetic Italic (Shear Transform)

**Algorithm**: Affine shear applied to all outline points

```csharp
float shearFactor = MathF.Tan(italicAngle * MathF.PI / 180f);
// Default angle: 12 degrees (tan ≈ 0.2126)

foreach (ref var point in outline.Points)
{
    point.X += point.Y * shearFactor;
    // Y unchanged — shear is horizontal only
}
```

- Apply BEFORE rasterization (in font units)
- Update bounding box after transform
- Advance width unchanged (italic doesn't change advance)
- This is exact — Bezier curves remain valid after affine transforms

### Integration with RasterOptions

- `RasterOptions.Bold = true` → apply synthetic bold (unless font is natively bold AND `ForceSyntheticBold = false`)
- `RasterOptions.Italic = true` → apply synthetic italic (unless font is natively italic AND `ForceSyntheticItalic = false`)
- Update `IRasterizerCapabilities`:
  - `SupportsSyntheticBold = true`
  - `SupportsSyntheticItalic = true`

### Outline Transform Pipeline

```
GlyphOutline (font units)
  → Synthetic Bold (if requested)
    → Synthetic Italic (if requested)
      → Scale to pixels
        → Flatten to edges
          → Rasterize
```

Bold applied before italic so the expansion is uniform (not sheared).

## Testing

- Bold 'O': verify outer contour expands, inner contour shrinks (counter stays open)
- Bold 'I': verify stem thickens uniformly
- Bold at multiple strengths (1px, 2px, 3px): verify no artifacts
- Italic 'A': verify correct shear angle
- Bold + Italic combined: verify both transforms applied
- Compare against FreeType synthetic bold/italic output
- Edge cases: glyphs with many counters ('B', 'e'), sharp corners, small sizes
- Regression: verify bold doesn't fill in counters at 12px

## Success Criteria

- [ ] Synthetic bold expands outlines correctly (outer out, inner in)
- [ ] Synthetic italic shears outlines at correct angle
- [ ] Bold + italic compose correctly
- [ ] No counter fill-in at small sizes
- [ ] Capabilities updated (`SupportsSyntheticBold/Italic = true`)
- [ ] All tests pass
