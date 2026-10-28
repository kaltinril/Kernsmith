# Phase 166 — Native Rasterizer: Staged Pipeline & CFF/CFF2 Charstring Interpreter

> **Status**: Next — Part 1 (staged pipeline) complete; Part 2 (CFF) is Wave 1
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (complete). Part 2 depends on Part 1.

## Goal

1. **Part 1** — restructure `NativeRasterizer.RasterizeGlyph` into explicit stages behind an `IOutlineDecoder`, so every later Native phase plugs into a stage instead of editing one long method.
2. **Part 2** — add CFF (and CFF2 structure) outlines so `.otf` fonts with PostScript outlines render, replacing today's `LoadFont` rejection.

## Part 1 — Staged pipeline refactor (Wave 0)

Complete. `RasterizeGlyph` and `GetGlyphMetrics` both run `GlyphPipeline.Prepare` (stages 1–4), then the renderer from `GlyphRenderers.Select` (stage 5); the contributor guide to the stages is the "Glyph pipeline" section of `src/KernSmith.Rasterizers.Native/README.md`. All types are internal.

1. **Decode** — `IOutlineDecoder.Decode(glyphIndex, options) → DecodedGlyph` (cubic `GlyphOutline`, advance and optional declared bbox, all in font units). `TrueTypeOutlineDecoder` wraps the `glyf` parse + `OutlineExtractor`; `OutlineDecoders.ForFace` picks the decoder once in `LoadFont` and returns null for CFF, which `LoadFont` turns into its rejection. Variation deltas (Phase 171) belong inside the decoder.
2. **Transform** — `GlyphTransforms.For(options, unitsPerEm)` returns the ordered `IGlyphTransform` list — bold then italic (Phase 167), empty for unstyled requests; 168 (stroke, small caps, condensed) adds entries; a transform adjusts the advance through `DecodedGlyph.WithAdvanceWidth`.
3. **Bounds** — `GlyphBox.Compute(outline, declaredBounds, scale)`. With no declared bbox (CFF, or any transform that replaced the outline) the grid comes from `OutlineBounds.Tight` (curve extrema). Untransformed `glyf` glyphs keep the header bbox and its control-hull fallback, which keeps their output identical.
4. **Scale / hint** — font units → pixels via `PreparedGlyph.ToPixels`, then `GlyphHinter.Apply` (no-op until Phase 174).
5. **Render-mode dispatch** — `GlyphRenderers.Select` returns `CoverageRenderer` or throws today's `NotSupportedException` for color/SDF; SDF (169/170), LCD (173) and color (172) add `IGlyphRenderer`s there.

Output is pinned by `NativeOutputCharacterizationTests` (SHA-256 of Roboto output across sizes, DPI and anti-alias modes, composite and empty glyphs included); seams are covered by `GlyphPipelineTests` and `GlyphBoxTests`.

## Part 2 — CFF/CFF2 (Wave 1)

### Starting point

- CFF detection already exists: `TableProvider.IsCff` (sfnt `OTTO`), `FontValidator` requires `CFF `/`CFF2` on a CFF face, `NativeFontFace.Loca`/`.Glyf` are null for CFF faces, and `MaxpTable` handles the v0.5 short form. A CFF font parses all the way to `LoadFont`.
- The only rejection is the `RasterizationException` thrown in `NativeRasterizer.LoadFont` when `OutlineDecoders.ForFace` returns null (no `glyf`). There is no capability flag involved.
- Metrics stay as they are: `hmtx` supplies advance and `GetFontMetrics` reads OS/2 win metrics. CFF `defaultWidthX`/`nominalWidthX` widths are a charstring detail and must not displace `hmtx`.
- `SyntheticFonts.cs` in the Native test project fabricates an `OTTO` wrapper around Roboto's tables purely to test rejection; its `CFF ` table is not a decodable charstring stream.

### CFF table parser

- Header (major/minor version, header size, offSize)
- INDEX structures (Name, Top DICT, String, Global Subr)
- Top DICT: CharStrings offset, Private DICT offset/size, charset offset, encoding offset, FontMatrix
- Private DICT: defaultWidthX, nominalWidthX, Local Subr INDEX offset
- CharStrings INDEX (one entry per glyph)
- CID-keyed fonts: FDArray / FDSelect, so each glyph gets the right Private DICT and local subrs

### Type 2 charstring interpreter

Stack machine, operand stack max 48 entries.

- **Movement**: `rmoveto` (21), `hmoveto` (22), `vmoveto` (4)
- **Lines**: `rlineto` (5), `hlineto` (6), `vlineto` (7)
- **Curves**: `rrcurveto` (8), `hhcurveto` (27), `vvcurveto` (26), `hvcurveto` (31), `vhcurveto` (30), `rcurveline` (24), `rlinecurve` (25)
- **Flex**: `flex`, `hflex`, `hflex1`, `flex1` (12 xx) — emit as two cubics
- **Hints** (parsed, not applied): `hstem` (1), `vstem` (3), `hstemhm` (18), `vstemhm` (23), `hintmask` (19), `cntrmask` (20) — mask bytes = `ceil(stemCount / 8)`, and an implicit `vstem` precedes the first mask when operands remain
- **Subroutines**: `callsubr` (10), `callgsubr` (29), `return` (11); bias 107 if count < 1240, 1131 if count < 33900, else 32768; nesting limit 10
- **End**: `endchar` (14) closes the contour; a `moveto` also implicitly closes the previous one
- **Numbers**: 1-, 2-, 3- (28) and 5-byte (255, 16.16 fixed) encodings
- **Width**: an odd leading operand on the first stack-clearing operator is the width (`nominalWidthX + value`), otherwise `defaultWidthX`; parsed and discarded (see metrics above)
- Malformed input (stack overflow/underflow, runaway subroutine recursion, out-of-range indices) throws `FontFormatException`, surfaced as `RasterizationException` per glyph, never a crash or hang

### CFF2

- Parse the CFF2 header, single Top DICT, FDArray/FDSelect and the variation store pointer
- `blend` and `vsindex` are parsed and stubbed (default instance only); Phase 171 completes them in Wave 2

### Integration

- `CffOutlineDecoder : IOutlineDecoder` produces cubic `GlyphOutline`s (no quadratic elevation needed); bbox comes from the outline (Part 1)
- `OutlineDecoders.ForFace` returns the CFF decoder, so `LoadFont` no longer throws; update its XML doc comment
- Refresh the "CFF is rejected" wording in `src/KernSmith.Rasterizers.Native/README.md`, the csproj `<Description>`, `COMPARISON.md`, `README.md`, `docs/rasterizers/native.md`, `reference/REF-12-rasterizer-backends.md`, and the comment above the Native entry in `tests/bmfont-compare/GenerateAll/Program.cs`

## Testing

- **Add a real CFF fixture first**: a committed OFL `.otf` with PostScript outlines (and its entry in `tests/KernSmith.Tests/Fixtures/FONT-LICENSES.md`). A CFF2 variable OFL font is needed too if CFF2 parsing is to be tested beyond structure.
- Charstrings for basic Latin decode; outlines match FreeType control points
- Local and global subroutines resolve; width parsing (nominal vs default); flex operators; CID-keyed font
- Fuzz-style cases: truncated charstrings, recursion bombs, stack overflow — graceful errors
- `NativeMetricsAgreementTests` (FreeType reference, ±1px) extended to the `.otf` fixture
- End-to-end: generate a BMFont from the `.otf` through the CLI with `--rasterizer native`

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/otf-plain.bmfc` pointing `fontFile` at the committed `.otf` (force-add it — the folder is gitignored). Part 1 relies on the existing configs reporting Native output identical to `main`. Because the harness tolerates a failing (config, backend) pair, the end-to-end test above must assert Native actually produced glyphs for the `.otf`.

## Success Criteria

- [x] Part 1: staged pipeline + `IOutlineDecoder` landed, output-identical for `glyf` fonts
- [ ] CFF (including CID-keyed) parsed; Type 2 interpreter handles all operators, including flex and hint masks
- [ ] Output outlines match FreeType for the `.otf` fixture; metrics within ±1px
- [ ] `.otf` fonts generate end to end; the `LoadFont` rejection is gone
- [ ] CFF2 structure parsed (`blend`/`vsindex` stubbed for Phase 171)
- [ ] Malformed CFF data fails gracefully
- [ ] All tests pass; `regression_check.py` run with `otf-plain.bmfc`
