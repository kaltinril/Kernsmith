# Phase 163 — Native Rasterizer: Outline Extraction & Bezier Processing

> **Status**: **COMPLETE** (2026-07-31)
> **Created**: 2026-04-01
> **Depends on**: Phase 162 (glyf/loca/maxp parsers)

> **Note**: Phase 162 is **COMPLETE** (merged 2026-07-30) — `loca`/`glyf`/`maxp` parsing and the `ParsedGlyph` model are in place, so this phase was unblocked.

## What Shipped

| Item | Where |
|------|-------|
| `OutlineCommandType` / `OutlineCommand` / `GlyphOutline` command model | `src/KernSmith.Rasterizers.Native/Internal/Outlines/GlyphOutline.cs` |
| `ParsedGlyph` → outline commands, quadratic-to-cubic elevation | `Internal/Outlines/OutlineExtractor.cs` |
| `EdgeSegment` directed line segment | `Internal/Outlines/EdgeSegment.cs` |
| Font-unit → pixel scale + Y-flip transform | `Internal/Outlines/OutlineTransform.cs` |
| Adaptive De Casteljau flattening + edge generation | `Internal/Outlines/OutlineFlattener.cs` |
| 24 tests (14 extraction + 10 flattening) | `tests/KernSmith.Rasterizers.Native.Tests/OutlineTests.cs`, `OutlineFlattenerTests.cs` |

**Deviations from the plan as written**

- `Close` **carries the contour's start point** in its endpoint fields and implies the closing segment. The plan's step 5 said only "close each contour with `Close`"; carrying the start means consumers (the flattener, and Phase 164's edge feed) never have to track the contour start themselves, and no redundant `LineTo` back to the start is emitted.
- `GlyphOutline`'s bounding box is computed from the **emitted commands, control points included**, not from the `glyf` declared box. A cubic never leaves its control hull, so the box is a conservative control-hull box: it can over-estimate the ink extent, never clip it.
- Quadratic elevation is written `(p + 2c) / 3` rather than `p + 2/3 * (c - p)`. Algebraically identical, but it avoids rounding the `2/3` factor, so the elevation is bit-exact for whole-unit font coordinates.
- Contours that **start on an off-curve point** are handled explicitly (start = the contour's last on-curve point, or the implicit midpoint when that is also off-curve). The plan's processing-step list omitted this case, which real fonts do use.

## Goal

Convert raw parsed glyph data into clean outline commands (MoveTo/LineTo/CurveTo) and implement Bezier curve flattening for the rasterizer.

## Scope

### Outline Extraction

Convert `ParsedGlyph` contours into a normalized command list:

```csharp
internal enum OutlineCommandType { MoveTo, LineTo, CubicTo, Close }

internal readonly record struct OutlineCommand(
    OutlineCommandType Type,
    float EndX, float EndY,       // endpoint for all command types
    float Ctrl1X, float Ctrl1Y,   // control point 1 (CubicTo only)
    float Ctrl2X, float Ctrl2Y);  // control point 2 (CubicTo only)

internal sealed class GlyphOutline
{
    public OutlineCommand[] Commands { get; }
    public float XMin, YMin, XMax, YMax;  // bounding box in font units
}
```

Processing steps:
1. Walk contour points, interpreting on/off-curve sequences
2. On→On: emit `LineTo`
3. On→Off→On: quadratic Bezier → elevate to cubic, emit `CubicTo`
4. On→Off→Off→...→On: insert implicit midpoints, emit sequence of `CubicTo`
5. Close each contour with `Close` command
6. First point of each contour: emit `MoveTo`

### Quadratic-to-Cubic Elevation

Convert TrueType quadratic Bezier (P0, P1, P2) to cubic (P0, C1, C2, P2):
```
C1 = P0 + 2/3 * (P1 - P0)
C2 = P2 + 2/3 * (P1 - P2)
```

This is exact — no approximation error.

### Scaling

- `GlyphOutline` stores coordinates in font units
- Scale factor: `pixelSize / unitsPerEm`
- Scaling applied lazily or at flattening time, NOT stored in outline
- Y-axis flip: TrueType Y-up → bitmap Y-down: `y_pixel = ascent - y_font_scaled`

### Bezier Curve Flattening

Convert cubic Bezier curves to line segments for the rasterizer.

**Algorithm**: Adaptive De Casteljau subdivision
1. Estimate flatness: max distance of control points from chord (P0→P3 line)
2. If flatness < tolerance (default 0.25 pixels), output line segment P0→P3
3. Otherwise, split at t=0.5 via De Casteljau and recurse on both halves

**De Casteljau split for cubic** (P0, P1, P2, P3):
```
M01 = (P0+P1)/2, M12 = (P1+P2)/2, M23 = (P2+P3)/2
M012 = (M01+M12)/2, M123 = (M12+M23)/2
M0123 = (M012+M123)/2  ← the split point

Left half:  (P0, M01, M012, M0123)
Right half: (M0123, M123, M23, P3)
```

**Recursion depth limit**: 16 levels (4^16 = 4 billion — more than enough).

**Output**: `EdgeSegment[]` — directed line segments for the rasterizer.

```csharp
internal readonly record struct EdgeSegment(float X0, float Y0, float X1, float Y1);
```

### Edge Generation

- For each `LineTo`: create one `EdgeSegment`
- For each `CubicTo`: flatten via De Casteljau, create N `EdgeSegment`s
- Keep edges in their original winding direction (do NOT re-direct). The signed-area trapezoid algorithm in Phase 164 uses the original direction to compute sign. An edge going from lower Y to higher Y contributes positive coverage; higher to lower contributes negative.
- Discard horizontal edges (Y0 == Y1) — they contribute no coverage

## Testing

- Outline extraction: verify command sequence for known glyphs ('I' = simple lines, 'O' = curves, 'A' = mixed)
- Quadratic-to-cubic: verify exact elevation (known test vectors)
- Flattening: verify output line segments approximate the curve within tolerance
- Scaling: verify font unit → pixel conversion at multiple sizes
- Edge generation: verify edge count, directionality, horizontal edges discarded
- Visual test: render flattened outline as SVG for manual inspection

## Success Criteria

- [x] Outline commands correctly extracted from simple and composite glyphs ('I', 'A', 'B', 'O' from Roboto-Regular, plus every glyph checked for MoveTo-opened / Close-terminated contours)
- [x] Quadratic-to-cubic elevation is exact (asserted bit-exactly on whole-unit control points; implicit midpoints between consecutive off-curve points verified)
- [x] Bezier flattening produces edges within tolerance (measured against the true cubic; tighter tolerance verified to trade edge count for accuracy, and the depth cap verified with an unreachable tolerance)
- [x] Scaling and Y-flip correct (`pixelSize / unitsPerEm` scale and baseline placement verified synthetically and against the real face's `hhea` ascender)
- [x] Edge segments properly directed for winding rule (reversed contour produces reversed edges; horizontal edges dropped; real glyph flattens to a closed chain)
- [x] All tests pass — 89 in `KernSmith.Rasterizers.Native.Tests` (net8.0 + net10.0), 24 of them new here
