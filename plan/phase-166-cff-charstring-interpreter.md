# Phase 166 — Native Rasterizer: CFF/CFF2 Charstring Interpreter

> **Status**: **Next**
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration) — **complete**

> **Input contract from Phase 165** (complete — see `done/phase-165-irasterizer-integration.md`):
> - **This phase's entry point already exists as a throw.** `NativeRasterizer.LoadFont` calls `NativeFontFace.Load`, then rejects the face with a `RasterizationException` when `face.HasGlyfOutlines` is false (`src/KernSmith.Rasterizers.Native/NativeRasterizer.cs:55-59`). CFF detection is therefore **already done and already tested** — the work here is to replace that throw with a decoder, not to add detection.
> - **Detection plumbing is in place**: `TableProvider.IsCff` (sfnt version `OTTO`), `FontValidator` already requires `CFF `/`CFF2` to be present on a CFF face, and `NativeFontFace.Loca`/`.Glyf` are **nullable and null for CFF faces** with `MaxpTable` handling the v0.5 (`0x00005000`) short form. So a CFF font parses cleanly all the way to `LoadFont` today; only the outline source is missing.
> - The "Auto-Detection" section below is consequently **mostly already shipped**. What remains is selecting the decoder and deleting the throw. The capability flags are *not* involved — there is no `SupportsCff` capability; the rejection is the `LoadFont` throw and nothing else.
> - **There is no `IOutlineDecoder` in the code.** Phase 165's pipeline calls `OutlineExtractor.Extract(parsed)` directly on a `glyf`-derived `ParsedGlyph` (`NativeRasterizer.RasterizeGlyph`). Introducing the interface below is new work in this phase, and it means refactoring the TrueType path onto it too.
> - ⚠️ **`GlyphBox.Compute(ParsedGlyph glyph, GlyphOutline outline, float scale)` takes a `ParsedGlyph`** and prefers the glyph's **`glyf` header bounding box**, falling back to `GlyphOutline`'s conservative control-hull box only when the header box is degenerate. **CFF charstrings carry no per-glyph declared bbox**, so the CFF path will land on the control-hull fallback — which is correct but looser, and may cost a row/column of padding versus FreeType. Either widen `GlyphBox` to accept a bbox-less source explicitly, or compute a tight box from the flattened outline. Do not assume the `ParsedGlyph` signature survives this phase.
> - Metrics are already backend-complete and must keep working unchanged: `hmtx` supplies advance, and `GetFontMetrics` reads **OS/2 win metrics** (not `hhea` — a deliberate Phase 165 deviation that makes `.fnt` `base`/`lineHeight` match FreeType exactly). CFF's `defaultWidthX`/`nominalWidthX` widths are a *charstring* concern; they must not displace `hmtx` as the advance source.
> - `NativeMetricsAgreementTests` compares against a **FreeType `ProjectReference` in the Native test project** at 12/16/24/32/48/96 px with a ±1 tolerance. Reuse that harness for CFF rather than inventing a new baseline. Note the repo still has **no real CFF fixture** — Phase 165 added `SyntheticFonts.cs`, which fabricates an `OTTO`/`CFF ` face from Roboto's tables purely to exercise the rejection path; its `CFF ` table is **not a valid charstring stream** and cannot be used to test decoding. A genuine .otf fixture is needed.
> - ⚠️ `ScanlineRasterizerSsimTests` still carries an `'O'` @ 12px case at **0.9509** against a 0.95 floor. CFF outlines are cubic and feed the same `OutlineFlattener`; any tolerance change made for CFF must re-run that suite.

## Goal

Add CFF (Compact Font Format) and CFF2 support so the Native rasterizer can handle .otf fonts with PostScript outlines, not just TrueType .ttf fonts.

## Background

CFF outlines use cubic Bezier curves (vs TrueType's quadratic) encoded as Type 2 charstrings — a stack-based bytecode format. CFF is common in professional fonts and all Adobe fonts.

Phase 163 **established** cubic Beziers as the internal representation (`OutlineCommandType.CubicTo`; TrueType quadratics are elevated on the way in), so CFF outlines feed directly into the existing flatten → rasterize pipeline without conversion.

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

`IOutlineDecoder` **does not exist yet** — Phase 165 wired `OutlineExtractor.Extract(parsed)` straight into `RasterizeGlyph`. Introducing it here also means moving the TrueType path behind it.

```csharp
internal sealed class CffOutlineDecoder : IOutlineDecoder
{
    public GlyphOutline? DecodeGlyph(int glyphIndex);
}
```

Outputs the same `GlyphOutline` (cubic Bezier commands) as the TrueType decoder. Note that `GlyphBox.Compute` currently requires a `ParsedGlyph` for its `glyf` header box — see the input-contract note above; the decoder abstraction has to account for the bbox-less CFF case.

### Auto-Detection

Mostly shipped in Phase 165; what remains is the decoder switch.

- ✅ Already done: sfnt version detection (`TableProvider.IsCff`, `OTTO` vs `0x00010000`), `CFF `/`CFF2` presence validation, nullable `glyf`/`loca`, maxp v0.5 handling
- ⬜ To do: `NativeRasterizer.LoadFont` selects the appropriate decoder instead of throwing
- ⬜ To do: **remove the `RasterizationException` at `NativeRasterizer.cs:55-59`** (this is the only place CFF is rejected — there is no capability flag to flip), and update its XML doc comment
- ⬜ To do: refresh the "not supported" wording in `src/KernSmith.Rasterizers.Native/README.md`, `KernSmith.Rasterizers.Native.csproj` `<Description>`, `COMPARISON.md`, `docs/rasterizers/native.md` and `reference/REF-12-rasterizer-backends.md`, all of which currently state that CFF/OTF is rejected

## Key Implementation Details

- **Width handling**: First number before first operator may be glyph width. If present: `width = nominalWidthX + value`. If absent: `width = defaultWidthX`.
- **Implicit closepath**: CFF charstrings don't have an explicit close command — `endchar` and the next `moveto` implicitly close the current contour.
- **Hint counting**: Must track stem count to know how many bytes `hintmask`/`cntrmask` consume: `maskBytes = ceil(stemCount / 8)`.
- **Subroutine nesting**: Allow up to 10 levels of subroutine nesting (per spec).
- **Operator encoding**: Single byte (0-31) or two-byte (12 xx). Numbers use variable-length encoding.

## Testing

- ⚠️ **A real .otf fixture must be added first.** `tests/.../SyntheticFonts.cs` (Phase 165) only fabricates an `OTTO` wrapper around Roboto's tables to test the rejection path — its `CFF ` table is not a decodable charstring stream.
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
