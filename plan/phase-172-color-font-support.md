# Phase 172 — Native Rasterizer: Color Font Support

> **Status**: In progress — COLRv0 + CPAL (Wave 1) complete; COLRv1 (Wave 2) next; CBDT/sbix blocked on an open question
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch)

## Goal

Render color glyphs in the Native rasterizer: COLR v0 layered glyphs with CPAL palettes first, then the COLR v1 paint graph.

## Current state

- `SupportsColorFonts = true`. With `ColorFont` on, `GlyphRenderers.SelectForFace` wraps the mode renderer in `ColorLayerRenderer` (`Internal/Pipeline/`) when the face has a usable `CPAL`; `Internal/Tables/CpalTable.cs` / `ColrTable.cs` parse, `Internal/Color/ColorGlyphs.cs` resolves layers and holds the selected palette.
- Core calls `SelectColorPalette(index)` only when the capability is true and the index is non-zero; Native stores it and never reads `RasterOptions.ColorPaletteIndex`. An index the font lacks (or a font with no usable `CPAL`) throws `ArgumentOutOfRangeException`, as FreeType errors.
- Fixtures (`tests/KernSmith.Tests/Fixtures/Color/`, OFL texts beside them): `BungeeColor-Regular.ttf` (COLR v0, CPAL v0, 9 palettes) and `ReemKufiInk-Regular.ttf` (COLR v1 paints only, no v0 records). They sit in a subfolder because core `ColorFontTests` picks the first color font directly in `Fixtures/` and expects NotoColorEmoji.
- Behavior, matched against FreeType (the reference) where noted:
  - Output is `Rgba32`, **straight alpha** (compositing is premultiplied internally). FreeType returns premultiplied values that core then treats as straight, so FreeType's colored edges come out darker; tests premultiply Native before comparing.
  - Canvas = union of the layer boxes; advance = the base glyph's. A base glyph with an empty outline still draws its layers; layers that are all blank give a 0x0 RGBA glyph (FreeType).
  - Foreground entry `0xFFFF` = opaque black, or white when the selected palette is flagged `USABLE_WITH_DARK_BACKGROUND` (FreeType's documented default; FreeType itself only applies the flag inside `FT_Palette_Select` and reads the previous palette's flags there).
  - Layers are read in order and stop at the first invalid one (glyph past `maxp`, palette entry past the palette, record past the array); if the first is invalid the glyph renders as plain coverage (FreeType). Malformed `COLR`/`CPAL`, or `COLR` without `CPAL`, are ignored — plain coverage, no error.
  - Glyphs without v0 layers, including COLRv1-only glyphs, render as plain grayscale coverage. FreeType throws on COLRv1 glyphs with `FT_LOAD_COLOR`.
  - `AntiAlias = None` thresholds each layer. SDF + color: color layers win for COLR glyphs and the mode renderer handles the rest (FreeType ignores SDF for COLR glyphs); today core rejects SDF on Native before that matters. Bold/Italic: layers go through `GlyphTransforms.For`, so Phase 167 transforms will apply per layer; until then core's bitmap Bold/Italic post-processors run on the RGBA glyph (FreeType drops emboldening on layered glyphs, keeping only the wider advance).
  - Bitmap-only color fonts (`CBDT`/`sbix`, no `glyf`) are rejected at `LoadFont` by `FontValidator` with a `RasterizationException` pointing at FreeType.
- Known decode-stage difference (not color-specific): FreeType moves a glyph so its `hmtx` left side bearing is honored when it disagrees with `glyf` xMin; Native draws the outline where it is. Bungee Color's '4' inline layer is off by 350 units because of it; Roboto has 52 glyphs off by one unit.

## Work items — COLR v0 + CPAL (Wave 1)

Done; see Current state. CPAL v1 palette/entry label name IDs are not read (UI-only strings).

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

**Regression coverage**: `tests/bmfont-compare/gum-bmfont/color-colr.bmfc` (Bungee Color, `colorFont=1`) and `color-colr-palette.bmfc` (same with `colorPalette=6`). The harness tolerates a backend failing a config, so `NativeColorFontTests` asserts Native produces a colored RGBA atlas.

## Success Criteria

- [x] COLR v0 + CPAL fixtures committed
- [x] CPAL parsed; palette selection works
- [x] COLR v0 layered rendering matches FreeType
- [ ] COLR v1 paint graph (gradients, transforms, composites) renders
- [x] `SupportsColorFonts = true`; `ColorFont` no longer throws
- [ ] CBDT/sbix decision recorded (implement or drop)
- [ ] All tests pass; `regression_check.py` run with the COLR config
