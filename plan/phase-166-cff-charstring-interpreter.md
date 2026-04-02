# Phase 166 — Native Rasterizer: CFF/CFF2 Charstring Interpreter

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration)

## Goal

Add CFF (Compact Font Format) and CFF2 support so the Native rasterizer can handle .otf fonts with PostScript outlines, not just TrueType .ttf fonts.

## Background

CFF outlines use cubic Bezier curves (vs TrueType's quadratic) encoded as Type 2 charstrings — a stack-based bytecode format. CFF is common in professional fonts and all Adobe fonts.

Since Phase 163 already established cubic Beziers as the internal representation, CFF outlines feed directly into the existing pipeline without conversion.

## Scope

### CFF Table Parser
- Parse CFF header (major/minor version, header size, offSize)
- Parse INDEX structures (Name INDEX, Top DICT INDEX, String INDEX, Global Subr INDEX)
- Parse Top DICT to find: CharStrings offset, Private DICT offset/size, charset offset, encoding offset
- Parse Private DICT to find: defaultWidthX, nominalWidthX, Local Subr INDEX offset
- Parse CharStrings INDEX (one entry per glyph)
- Parse charset (Format 0, 1, 2) for glyph name mapping

### Type 2 Charstring Interpreter

Stack machine with operand stack (max 48 entries per spec):

**Movement operators**:
- `rmoveto` (21): dx dy → start new contour
- `hmoveto` (22): dx → horizontal move
- `vmoveto` (4): dy → vertical move

**Line operators**:
- `rlineto` (5): {dx dy}+ → relative line(s)
- `hlineto` (6): dx {dy dx}* → alternating horizontal/vertical lines
- `vlineto` (7): dy {dx dy}* → alternating vertical/horizontal lines

**Curve operators**:
- `rrcurveto` (8): {dx1 dy1 dx2 dy2 dx3 dy3}+ → cubic Bezier(s)
- `hhcurveto` (27): dy1? {dx1 dx2 dy2 dx3}+ → horizontal start curves
- `vvcurveto` (26): dx1? {dy1 dx2 dy2 dy3}+ → vertical start curves
- `hvcurveto` (31): alternating h→v curves
- `vhcurveto` (30): alternating v→h curves
- `rcurveline` (24): curves followed by a line
- `rlinecurve` (25): lines followed by a curve

**Hint operators** (parse but skip for unhinted rendering):
- `hstem` (1), `vstem` (3): declare stem hints
- `hstemhm` (18), `vstemhm` (23): declare stem hints (hint mask follows)
- `hintmask` (19), `cntrmask` (20): hint/counter masks (skip N bytes based on stem count)

**Subroutine operators**:
- `callsubr` (10): call local subroutine
- `callgsubr` (29): call global subroutine
- `return` (11): return from subroutine

**Other**:
- `endchar` (14): end of charstring (implicit close)
- Numbers: 1-byte, 2-byte, and 5-byte (16.16 fixed) encodings

**Subroutine bias**: index = raw_index + bias, where bias depends on subr count:
- count < 1240: bias = 107
- count < 33900: bias = 1131
- else: bias = 32768

### CFF2 Extensions (for variable font support in Phase 171)
- `blend` operator: interpolates N values using variation deltas
- `vsindex` operator: selects variation data index
- Simplified structure (no Name/String INDEX, single Top DICT)
- Mark as TODO/stub for Phase 171 to complete

### IOutlineDecoder for CFF

```csharp
internal sealed class CffOutlineDecoder : IOutlineDecoder
{
    public GlyphOutline? DecodeGlyph(int glyphIndex);
}
```

Outputs the same `GlyphOutline` (cubic Bezier commands) as the TrueType decoder.

### Auto-Detection

- Check sfnt version: `OTTO` → use CFF decoder, `0x00010000` → use TrueType decoder
- `NativeRasterizer.LoadFont` selects the appropriate decoder
- Update capabilities: CFF fonts now accepted (no more `RasterizationException`)

## Key Implementation Details

- **Width handling**: First number before first operator may be glyph width. If present: `width = nominalWidthX + value`. If absent: `width = defaultWidthX`.
- **Implicit closepath**: CFF charstrings don't have an explicit close command — `endchar` and the next `moveto` implicitly close the current contour.
- **Hint counting**: Must track stem count to know how many bytes `hintmask`/`cntrmask` consume: `maskBytes = ceil(stemCount / 8)`.
- **Subroutine nesting**: Allow up to 10 levels of subroutine nesting (per spec).
- **Operator encoding**: Single byte (0-31) or two-byte (12 xx). Numbers use variable-length encoding.

## Testing

- Parse CFF table from a PostScript-outline .otf font
- Interpret charstrings for basic Latin glyphs
- Compare output outlines against FreeType (control point positions)
- Verify subroutine calls resolve correctly
- Verify width calculation (nominal vs default)
- End-to-end: generate BMFont from .otf file
- Edge cases: deeply nested subroutines, glyphs with many hints, empty charstrings

## Success Criteria

- [ ] CFF table parsed correctly
- [ ] Type 2 charstring interpreter handles all operators
- [ ] Subroutine calls (local and global) work correctly
- [ ] Output outlines match FreeType for CFF fonts
- [ ] End-to-end BMFont generation works with .otf files
- [ ] CFF2 structure parsed (blend/vsindex stubbed for Phase 171)
- [ ] All tests pass
