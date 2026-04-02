# Phase 162 — Native Rasterizer: Glyph Table Parsers (glyf, loca, maxp)

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 161 (project scaffold, binary reader, core table parsers)

## Goal

Parse the three tables required to extract glyph outline data from TrueType fonts: `maxp` (glyph count/limits), `loca` (glyph offsets), and `glyf` (glyph outlines).

## Scope

### maxp Table Parser
- Version 0.5 (CFF): just `numGlyphs`
- Version 1.0 (TrueType): `numGlyphs`, `maxPoints`, `maxContours`, `maxCompositePoints`, `maxCompositeContours`, `maxComponentElements`, `maxComponentDepth`
- Use `maxComponentDepth` for composite glyph recursion limit (default cap: 64)

### loca Table Parser
- Read `head.indexToLocFormat` (parsed in Phase 161) to determine format:
  - Format 0 (short): offsets are `uint16 * 2` (array of numGlyphs+1 entries)
  - Format 1 (long): offsets are `uint32` (array of numGlyphs+1 entries)
- `GetGlyphOffset(int glyphIndex) → (int offset, int length)`
- Empty glyphs (offset[i] == offset[i+1]): return null/empty — these are space-like glyphs

### glyf Table Parser

#### Simple Glyphs (numberOfContours >= 0)
- Read `numberOfContours`, `xMin`, `yMin`, `xMax`, `yMax`
- Read `endPtsOfContours[numberOfContours]`
- Read `instructionLength`, skip instructions (no hinting)
- Read flags (packed with repeat flag):
  - Bit 0: ON_CURVE_POINT
  - Bit 1: X_SHORT_VECTOR
  - Bit 2: Y_SHORT_VECTOR
  - Bit 3: REPEAT_FLAG
  - Bit 4: X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR
  - Bit 5: Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR
  - Bit 6: OVERLAP_SIMPLE (informational, no effect on rasterization)
- Read X coordinates (delta-encoded, variable-width based on flags)
- Read Y coordinates (delta-encoded, variable-width based on flags)

#### Composite Glyphs (numberOfContours < 0, specifically -1)
- Read component loop:
  - `flags` (uint16)
  - `glyphIndex` (uint16)
  - Arguments: point indices or XY offsets (int8/int16 based on flags)
  - Optional transform: scale (F2Dot14), XY scale, or 2×2 matrix
  - Flags: `ARG_1_AND_2_ARE_WORDS`, `ARGS_ARE_XY_VALUES`, `ROUND_XY_TO_GRID`, `WE_HAVE_A_SCALE`, `MORE_COMPONENTS`, `WE_HAVE_AN_X_AND_Y_SCALE`, `WE_HAVE_A_TWO_BY_TWO`, `WE_HAVE_INSTRUCTIONS`, `USE_MY_METRICS`, `OVERLAP_COMPOUND`, `SCALED_COMPONENT_OFFSET`, `UNSCALED_COMPONENT_OFFSET`
- Recursively resolve components (with depth limit from maxp)
- Apply transforms to component points

### Output Types
```csharp
// Raw parsed glyph data
internal readonly record struct GlyphPoint(float X, float Y, bool OnCurve);

internal sealed class GlyphContour
{
    public GlyphPoint[] Points { get; }
}

internal sealed class ParsedGlyph
{
    public int GlyphIndex { get; }
    public GlyphContour[] Contours { get; }  // null for empty glyphs
    public short XMin, YMin, XMax, YMax;     // bounding box in font units
    public bool IsComposite { get; }
}
```

## Key Implementation Details

- **Implicit on-curve points**: Between two consecutive off-curve points, insert midpoint `((x1+x2)/2, (y1+y2)/2)` as on-curve. This is a critical TrueType behavior that's easy to miss.
- **Delta decoding**: X and Y coordinates are delta-encoded from the previous point. First point's delta is absolute.
- **Composite recursion limit**: Use `maxp.maxComponentDepth` or cap at 64.
- **Point matching** (composite): When `ARGS_ARE_XY_VALUES` is NOT set, arguments are point indices to align. Rarely used but must handle.
- **Empty glyphs**: When loca[i] == loca[i+1], the glyph has no outline (e.g., space character). Return empty contours, not an error.

## Testing

- Parse Roboto-Regular 'A' (simple glyph): verify contour count, point count, on/off-curve flags
- Parse Roboto-Regular space: verify empty glyph handled
- Parse composite glyph (e.g., 'Ã', accented characters): verify components assembled
- Verify point counts match `maxp` limits
- Verify bounding box matches `head` global bounds
- Round-trip: parse → reconstruct coordinates → verify against expected values
- Edge cases: zero-contour simple glyphs, deeply nested composites, composite with 2×2 matrix

## Success Criteria

- [ ] maxp table parsed correctly for Roboto-Regular
- [ ] loca table parsed correctly (both short and long formats tested)
- [ ] Simple glyphs parsed with correct contour/point data
- [ ] Composite glyphs recursively resolved with transforms applied
- [ ] Implicit on-curve midpoints inserted correctly
- [ ] Empty glyphs handled without error
- [ ] All tests pass
