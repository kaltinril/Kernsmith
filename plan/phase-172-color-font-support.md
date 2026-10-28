# Phase 172 — Native Rasterizer: Color Font Support

> **Status**: Future — COLRv0 + CPAL Wave 1; COLRv1 Wave 2; CBDT/sbix stretch (blocked on an open question)
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch)

## Goal

Render color glyphs in the Native rasterizer: COLR v0 layered glyphs with CPAL palettes first, then the COLR v1 paint graph.

## Starting point

- `NativeCapabilities.SupportsColorFonts = false`. A `ColorFont = true` request reaches Native and **throws `NotSupportedException`** from `RejectUnsupported` — it does not fall back to monochrome.
- Core calls `SelectColorPalette(index)` only when the capability is true and the index is non-zero; the rasterizer stores it and must not read `RasterOptions.ColorPaletteIndex` itself.
- FreeType (COLR/CPAL) is the reference backend.
- **No COLR fixture is committed.** `NotoColorEmoji.ttf` is CBDT/CBLC only.

## Work items — COLR v0 + CPAL (Wave 1)

- **Add fixtures first**: a small OFL font with COLR v0 + CPAL (several palettes), and one with COLR v1, each with a `FONT-LICENSES.md` entry.
- CPAL: palettes, color records (BGRA), palette count; v1 type/label fields parsed but unused.
- COLR v0: base glyph records (binary search by glyph ID) → layer records (glyph ID, palette index; `0xFFFF` = foreground color — render it white and check how core's fill tint treats RGBA glyphs, matching FreeType's behavior).
- Render: decode each layer glyph through the normal pipeline (so transforms and variations apply), rasterize coverage into a shared canvas sized to the union bbox, tint by palette color, composite bottom-to-top with source-over (premultiplied internally, straight alpha out).
- Output `PixelFormat.Rgba32` for color glyphs; glyphs with no COLR entry fall through to grayscale coverage.
- Color branch in the render-mode dispatch; remove the `ColorFont` throw; `SupportsColorFonts = true`.

## Work items — COLR v1 (Wave 2)

- Paint graph: `PaintColrLayers`, `PaintSolid`, `PaintLinearGradient`, `PaintRadialGradient`, `PaintSweepGradient`, `PaintGlyph` (clip), `PaintColrGlyph`, `PaintTransform` and the translate/scale/rotate/skew variants (including around-center forms), `PaintComposite`.
- ColorLine extend modes (pad/repeat/reflect); color-stop interpolation per spec.
- Composite modes: the Porter-Duff set plus the blend modes the spec lists.
- ClipList for bounds; cycle detection on `PaintColrGlyph` recursion.
- Variable paints (`Var*` forms) use the Phase 171 ItemVariationStore when variations are active.

## Bitmap color (CBDT/CBLC, sbix) — stretch, blocked

- CBLC strike selection nearest the requested ppem, CBDT/sbix image extraction, scaling to size.
- **Open question for Jeremy**: these glyphs are PNGs, so Native needs a PNG decoder. Core already depends on StbImageSharp (third-party, managed). Either Native may use it through core, or Native needs our own managed PNG/zlib decoder under the pure-managed rule. Not decided — do not start this part until it is.

## Testing

- COLR v0: layered composition matches FreeType (SSIM on RGBA)
- Palette switch changes colors; foreground-color layers match FreeType
- Transparent layers blend correctly; non-color glyphs in a color font stay grayscale
- COLR v1: solid, each gradient type and extend mode, transforms, composite modes, `PaintColrGlyph` reuse
- Malformed/cyclic paint graphs fail gracefully

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/color-colr.bmfc` with `fontFile` pointing at the committed COLR fixture, `colorFont=1` (and a second palette via `colorPalette=1`); force-add it. Today Native throws on it and the harness tolerates that, so the test must assert Native produced RGBA glyphs.

## Success Criteria

- [ ] COLR v0 + CPAL fixtures committed
- [ ] CPAL parsed; palette selection works
- [ ] COLR v0 layered rendering matches FreeType
- [ ] COLR v1 paint graph (gradients, transforms, composites) renders
- [ ] `SupportsColorFonts = true`; `ColorFont` no longer throws
- [ ] CBDT/sbix decision recorded (implement or drop)
- [ ] All tests pass; `regression_check.py` run with the COLR config
