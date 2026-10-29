# Phase 166 — Native Rasterizer: Staged Pipeline & CFF/CFF2 Charstring Interpreter

> **Status**: Complete
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (complete). Part 2 depends on Part 1.

## Goal

1. **Part 1** — restructure `NativeRasterizer.RasterizeGlyph` into explicit stages behind an `IOutlineDecoder`, so every later Native phase plugs into a stage instead of editing one long method.
2. **Part 2** — add CFF (and CFF2 structure) outlines so `.otf` fonts with PostScript outlines render, replacing the `LoadFont` rejection.

## Part 1 — Staged pipeline refactor (Wave 0)

Complete. `RasterizeGlyph` and `GetGlyphMetrics` both run `GlyphPipeline.Prepare` (stages 1–4), then the renderer from `GlyphRenderers.Select` (stage 5); the contributor guide to the stages is the "Glyph pipeline" section of `src/KernSmith.Rasterizers.Native/README.md`. All types are internal.

1. **Decode** — `IOutlineDecoder.Decode(glyphIndex, options) → DecodedGlyph` (cubic `GlyphOutline`, advance and optional declared bbox, all in font units). `TrueTypeOutlineDecoder` wraps the `glyf` parse + `OutlineExtractor`; `OutlineDecoders.ForFace` picks the decoder once in `LoadFont`. Variation deltas (Phase 171) belong inside the decoder.
2. **Transform** — `GlyphTransforms.For(options, unitsPerEm)` returns the ordered `IGlyphTransform` list — width (Phase 168), bold, then italic at `ItalicAngle` (Phases 167-168), empty for unstyled requests; a transform adjusts the advance through `DecodedGlyph.WithAdvanceWidth`.
3. **Bounds** — `GlyphBox.Compute(outline, declaredBounds, scale)`. With no declared bbox (CFF, or any transform that replaced the outline) the grid comes from `OutlineBounds.Tight` (curve extrema). Untransformed `glyf` glyphs keep the header bbox and its control-hull fallback, which keeps their output identical.
4. **Scale / hint** — font units → pixels via `PreparedGlyph.ToPixels`, then `GlyphHinter.Apply` (no-op until Phase 174).
5. **Render-mode dispatch** — `GlyphRenderers.Select` returns `CoverageRenderer` or throws today's `NotSupportedException` for color/SDF; SDF (169/170), LCD (173) and color (172) add `IGlyphRenderer`s there.

Output is pinned by `NativeOutputCharacterizationTests` (SHA-256 of Roboto output across sizes, DPI and anti-alias modes, composite and empty glyphs included); seams are covered by `GlyphPipelineTests` and `GlyphBoxTests`.

## Part 2 — CFF/CFF2 (Wave 1)

Complete. `OutlineDecoders.ForFace` returns `CffOutlineDecoder` for any face without `glyf`; the table is parsed in the constructor, so a malformed one fails `LoadFont` with a `FontFormatException` naming the table. All types are internal (`Internal/Cff/`, plus the decoder in `Internal/Outlines/`).

### Table parser (`CffFont`, `CffIndex`, `CffDict`)

- CFF: header, Name / Top DICT / String / Global Subr INDEXes (16-bit counts); Top DICT CharStrings, Private, charset, FontMatrix, ROS, FDArray, FDSelect; only Type 2 charstrings (`CharstringType` 2).
- CFF2: 5-byte header, inline Top DICT, 32-bit INDEX counts, FDArray (always), FDSelect (formats 0, 3, 4), and the VariationStore — only each ItemVariationData's region count is read, which is all `blend` needs to size its operands.
- Private DICT: `Subrs` (relative to the Private DICT), `defaultWidthX`, `nominalWidthX`, CFF2 `vsindex`; a DICT `blend` keeps the default values.
- CID-keyed CFF: every glyph gets the Private DICT (local subrs, widths) of the Font DICT FDSelect (format 0 or 3) assigns it.
- FontMatrix: normalized so its y scale is 1, then applied only if not the identity — charstring units are font units and `head.unitsPerEm` sets the em. This is FreeType's reading (verified against it for absent, uniform and stretched matrices). A CID Font DICT matrix is concatenated with the Top DICT's only when both set one.
- Every INDEX offset, DICT operand and FDSelect entry is validated at parse; counts are checked against the bytes present before anything is allocated.

### Type 2 interpreter (`Type2Charstring`)

- All path operators including `flex`/`hflex`/`hflex1`/`flex1` (two cubics each); hints parsed for stem count only; `hintmask`/`cntrmask` skip `ceil(stems / 8)` bytes, counting an implicit `vstem` from leftover operands.
- `callsubr`/`callgsubr` with the 107 / 1131 / 32768 bias; `return`; `endchar` (also the `seac` form, resolved through StandardEncoding and the charset — name-keyed CFF only).
- Width: the odd leading operand of the first stack-clearing operator is `nominalWidthX + w`, else `defaultWidthX` (CFF only). The decoder takes the advance from `hmtx`, as FreeType does; the charstring width equals `hmtx` for every Source Serif 4 glyph.
- Arithmetic/storage operators (`and` … `roll`, `put`/`get`, `ifelse`) are supported; `random` and reserved operators throw. `dotsection` is ignored.
- CFF2: no width or `endchar`, a 513-entry stack, `vsindex`, and `blend` keeping the default values. `CharstringContext.BlendScalars` applies per-region scalars when set — the hook [Phase 171](phase-171-variable-font-support.md) Wave 2 fills from the `FontVariations` that `OutlineDecoders.ForFace` hands `CffOutlineDecoder`, so CFF2 renders at the requested instance.
- Lazy `moveto`: a contour opens at the first drawing operator, so a moveto that draws nothing adds no point to the bounds.
- Limits, each a `FontFormatException`: operand stack 48 (CFF) / 513 (CFF2), subroutine nesting 10, 2^20 operations per glyph (stops shallow but exponential subroutine fan-out), 2^16 path commands, coordinates within ±65536.

## Testing

- Fixtures: `SourceSerif4-Regular.otf` (CFF, subroutinized, hint masks) and `SourceCodeVF-Upright.otf` (CFF2 variable), OFL, in `tests/KernSmith.Tests/Fixtures/` with their licences. No small OFL CID-keyed font was available, so CID-keyed CFF, FDSelect formats 0/3, `seac` and FontMatrix are covered with synthetic tables (`SyntheticCff` in the Native test project); the CFF2 table from the OpenType spec's example is decoded verbatim.
- `Type2CharstringTests`: every operator against hand-computed outlines, subroutine bias at each threshold, hint mask byte counting, width parsing, limits, arithmetic, `blend`/`vsindex`, `seac`.
- `CffTableTests`: INDEX/DICT parsing and hardening, Private DICT widths and subrs, CID FDSelect, FontMatrix vs FreeType, the CFF2 spec example.
- `CffFixtureTests`: four glyph outlines match fontTools' interpreter point for point (CFF and CFF2); every glyph decodes; charstring widths equal `hmtx`; seeded fuzzing (byte flips, truncation, random charstrings, mutated fonts through the rasterizer) allows only `FontFormatException`/`RasterizationException`.
- `NativeCffAgreementTests`: metrics within ±1px of unhinted FreeType at 12–96px for both fixtures; grayscale bitmaps vs FreeType (SSIM ≥ 0.90 per glyph, ≥ 0.98 mean) and vs stb_truetype for CFF in grayscale and monochrome; monochrome vs FreeType by mean SSIM.
- End to end: `NativeCffEndToEndTests` and `CliTests.Generate_NativeRasterizer_FromOtf_ProducesInkedGlyphs` assert inked glyphs from both `.otf` fixtures.
- CFF in WOFF2: `NotoSans-Regular-Latin-CFF.woff2` (fontTools, from the committed `.otf`; Source fonts are not used because their Reserved Font Name forbids a reformatted copy) decodes to the `.otf`'s tables byte for byte and renders identically on FreeType, StbTrueType, GDI, DirectWrite (`Woff2CffTests`) and Native (`NativeWoff2Tests`).
- `COLR` v0 layers drawn with CFF glyphs render through the CFF decoder (`NativeCffEndToEndTests`, a synthetic COLR/CPAL pair on Source Serif 4).
- Variation axes on a CFF2 font (`NativeCff2VariationTests`): no failure, default-instance output, and the face's `FontVariations` reaches `CffOutlineDecoder` for the CFF2 `blend` wave.
- Regression harness: `tests/bmfont-compare/gum-bmfont/otf-plain.bmfc` (CFF) and `otf-cff2-plain.bmfc` (CFF2).

## Success Criteria

- [x] Part 1: staged pipeline + `IOutlineDecoder` landed, output-identical for `glyf` fonts
- [x] CFF (including CID-keyed) parsed; Type 2 interpreter handles all operators, including flex and hint masks
- [x] Output outlines match a reference interpreter for the `.otf` fixtures; metrics within ±1px of FreeType
- [x] `.otf` fonts generate end to end; the `LoadFont` rejection is gone
- [x] CFF2 structure parsed; `blend`/`vsindex` keep the default values here, with the scalar hook Phase 171 fills
- [x] Malformed CFF data fails gracefully
- [x] All tests pass; `regression_check.py` run with the `.otf` configs
