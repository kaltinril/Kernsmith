# Phase 162 — Native Rasterizer: Glyph Table Parsers (glyf, loca, + maxp profile fields)

> **Status**: **COMPLETE** (2026-07-29)
> **Created**: 2026-04-01
> **Depends on**: Phase 161 (project scaffold, binary reader, core table parsers)

> **Note**: Phase 161 is **COMPLETE** (merged 2026-06-09) — the binary reader, table directory parser, and core table parsers (`head`, `hhea`, `hmtx`, `maxp`, `OS/2`, `cmap`) are in place, so this phase is unblocked.

## What Shipped

| Item | Where |
|------|-------|
| `maxp` v1.0 extended profile + `ComponentDepthLimit` (capped at 64) | `src/KernSmith.Rasterizers.Native/Internal/Tables/MaxpTable.cs` |
| `loca` parser (short/long, monotonic validation, empty-glyph ranges) | `Internal/Tables/LocaTable.cs` |
| `GlyphPoint` / `GlyphContour` / `ParsedGlyph` outline model | `Internal/Tables/ParsedGlyph.cs` |
| `glyf` parser — simple + composite glyphs, implicit midpoints | `Internal/Tables/GlyfTable.cs` |
| `NativeFontFace.Loca` / `.Glyf` / `.HasGlyfOutlines` / `.GetGlyph(int)` | `Internal/NativeFontFace.cs` |
| 16 tests (real font + synthetic `glyf`) | `tests/KernSmith.Rasterizers.Native.Tests/GlyphTableTests.cs` |

**Deviations from the plan as written**

- `ParsedGlyph.Contours` is an **empty array** for outline-less glyphs, not `null` — the plan's own "Key Implementation Details" asked for "empty contours, not an error", and a non-null array keeps every caller free of null checks. `IsEmpty` exposes the distinction.
- Implicit on-curve midpoints are inserted **after** a composite is fully assembled, not during simple-glyph parsing. Composite point-matching arguments index the *raw* point stream, so inserting midpoints first would shift those indices and mis-place components.
- Direct and indirect composite cycles are both caught by the recursion depth limit rather than by a separate self-reference guard.
- CFF (`OTTO`) faces expose `Loca`/`Glyf` as `null`; `GetGlyph` throws a clear `FontFormatException` pointing at the later CFF phase (Phase 166).

## Goal

Extract glyph outline data from TrueType fonts: parse `loca` (glyph offsets) and `glyf` (glyph outlines), and extend the existing `maxp` parser with the profile fields needed to bound composite recursion.

## Scope

### maxp Table Parser — EXTEND (numGlyphs landed in Phase 161)
- `numGlyphs` is **already parsed and in use** (`Internal/Tables/MaxpTable.cs`, wired at `NativeFontFace.Load`). The version field (Fixed, 4 bytes) precedes `numGlyphs` in both v0.5 (CFF) and v1.0 (TrueType), so the existing parser reads it correctly for either version without a version check.
- Remaining work — the v1.0 extended profile fields: `maxPoints`, `maxContours`, `maxCompositePoints`, `maxCompositeContours`, `maxComponentElements`, `maxComponentDepth`
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

- [x] maxp `numGlyphs` parsed correctly for Roboto-Regular (satisfied by Phase 161 — `CoreTableTests.cs`)
- [x] maxp v1.0 extended profile fields (`maxPoints`, `maxContours`, `maxCompositePoints`, `maxCompositeContours`, `maxComponentElements`, `maxComponentDepth`) parsed correctly for Roboto-Regular
- [x] loca table parsed correctly (both short and long formats tested, plus a v0.5 maxp and a decreasing-offset rejection)
- [x] Simple glyphs parsed with correct contour/point data (delta decoding verified against a synthetic square)
- [x] Composite glyphs recursively resolved with transforms applied (scale + unscaled offset verified exactly; depth limit verified)
- [x] Implicit on-curve midpoints inserted correctly (asserted across **every** glyph in Roboto-Regular)
- [x] Empty glyphs handled without error
- [x] All tests pass — 61 in `KernSmith.Rasterizers.Native.Tests` (net8.0 + net10.0), full solution green, `bmfont-compare` regression identical (175/175 FNT)
