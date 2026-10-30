# Phase 172 — Native Rasterizer: Color Font Support

> **Status**: Complete — COLRv0 + CPAL, COLRv1, and CBDT/CBLC + sbix color bitmaps
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch)

## Goal

Render color glyphs in the Native rasterizer: COLR v0 layered glyphs with CPAL palettes first, then the COLR v1 paint graph, then CBDT/CBLC and sbix PNG bitmap glyphs.

## Current state

- `SupportsColorFonts = true`. With `ColorFont` on, `GlyphRenderers.SelectForFace` wraps the mode renderer in `ColorLayerRenderer` (`Internal/Pipeline/`) when the face has a usable `CPAL`; it sends version 1 glyphs to `PaintGraphRenderer` and draws version 0 layers itself. `Internal/Tables/CpalTable.cs` / `ColrTable.cs` (v0) / `ColrV1Table.cs` (v1 lists, clip boxes, variation store) parse; `Internal/Color/ColorGlyphs.cs` holds the selected palette, resolves v0 layers and reads v1 graphs through `ColrPaintReader` into resolved `ColrPaint` nodes.
- Core calls `SelectColorPalette(index)` only when the capability is true and the index is non-zero; Native stores it and never reads `RasterOptions.ColorPaletteIndex`. An index the font lacks (or a font with no usable `CPAL`) throws `ArgumentOutOfRangeException`, as FreeType errors.
- Fixtures (`tests/KernSmith.Tests/Fixtures/Color/`, OFL texts beside them): `BungeeColor-Regular.ttf` (COLR v0, CPAL v0, 9 palettes) and `ReemKufiInk-Regular.ttf` (COLR v1 paints only, no v0 records; only its Arabic marks and digits are cmap-reachable color glyphs, and its gradients sit on GSUB-only glyphs). There is no variable COLRv1 fixture; tests hand-build v1 tables (`SyntheticColrV1`) on Roboto and RobotoFlex. They sit in a subfolder because core `ColorFontTests` picks the first color font directly in `Fixtures/` and expects NotoColorEmoji.
- Behavior, matched against FreeType (the reference) where noted:
  - Output is `Rgba32`, **straight alpha** (compositing is premultiplied internally). FreeType returns premultiplied values that core then treats as straight, so FreeType's colored edges come out darker; tests premultiply Native before comparing.
  - Canvas = union of the layer boxes; advance = the base glyph's. A base glyph with an empty outline still draws its layers; layers that are all blank give a 0x0 RGBA glyph (FreeType).
  - Foreground entry `0xFFFF` = opaque black, or white when the selected palette is flagged `USABLE_WITH_DARK_BACKGROUND` (FreeType's documented default; FreeType itself only applies the flag inside `FT_Palette_Select` and reads the previous palette's flags there).
  - Layers are read in order and stop at the first invalid one (glyph past `maxp`, palette entry past the palette, record past the array); if the first is invalid the glyph renders as plain coverage (FreeType). Malformed `COLR`/`CPAL`, or `COLR` without `CPAL`, are ignored — plain coverage, no error.
  - Glyphs with neither a v1 paint record nor v0 layers render as plain grayscale coverage.
  - `AntiAlias = None` thresholds each layer; `Lcd` is ignored for color glyphs (they render as grayscale-coverage color). SDF + color: color glyphs (v0 and v1) stay color and the SDF renderer handles the rest (FreeType ignores SDF for COLR glyphs); MSDF/MTSDF + color is rejected. Bold/Italic: v0 layers and v1 glyph clips go through `GlyphTransforms.For`, so the Phase 167 transforms apply to them (v1 clip boxes are styled the same way so they do not cut the styled ink) and core skips its bitmap Bold/Italic post-processors for Native (FreeType drops emboldening on layered glyphs, keeping only the wider advance).
- Layer glyphs, like every TrueType outline, are placed by their `hmtx` left side bearing phantom point (pp1 = xMin − lsb), as FreeType does, even where it disagrees with `glyf` xMin.

- COLR v1 behavior (written from the spec; FreeType's backend draws v1 glyphs as plain grayscale coverage since it has no paint-graph renderer, so there is no reference renderer — tests check spec formulas at known pixels, palette colors, and the alpha silhouette against FreeType's plain coverage):
  - A v1 `BaseGlyphList` record wins over a v0 record for the same glyph. A malformed v1 header or list drops v1 only; v0 still works.
  - Every Paint format 1–32 is read; the transform formats reduce to one affine matrix. `PaintColrGlyph` is inlined, with the referenced glyph's clip box applied to it.
  - Canvas: the glyph's `ClipList` box (quantized outward), else the bounds of its `PaintGlyph` outlines as transformed, combined per the spec's composite-mode boundedness rules (an untransformed glyph clip uses the pipeline's box for it). An unbounded graph with no clip box renders nothing (0x0 RGBA, base advance).
  - Gradient interpolation and all compositing (layers and `PaintComposite`) run on premultiplied **linear-light** values, as the COLR and CPAL chapters require; output is converted back to 8-bit sRGB, straight alpha. Gradients are sampled at pixel centers; glyph clips use the coverage rasterizer (`AntiAlias = None` thresholds them).
  - Invalid paints (unknown format, bad offset, glyph past `maxp`, palette entry past the palette, missing `PaintColrGlyph` record, a cycle) are skipped and the rest renders; ill-formed gradients (coincident points, parallel p0p2, equal circles, zero-length repeat/reflect lines) draw nothing; unrecognized composite modes are CLEAR and extend modes PAD.
  - Limits: more than 64 nested paints or 10,000 paints read (shared sub-graphs counted per use) rejects the glyph's v1 graph — it falls back to v0 layers or plain coverage.
  - Variable paints, `VarColorStop`s and `ClipBoxFormat2` read deltas from the COLR `ItemVariationStore` at the selected instance, through the `DeltaSetIndexMap` or the implicit (outer, inner) mapping; F2DOT14/Fixed deltas apply to the raw integers.

## Work items — COLR v0 + CPAL (Wave 1)

Done; see Current state. CPAL v1 palette/entry label name IDs are not read (UI-only strings).

## Work items — COLR v1 (Wave 2)

Done; see Current state.

## Bitmap color (CBDT/CBLC, sbix)

Done. PNGs decode through our own `src/KernSmith.Imaging` (`PngDecoder`; Jeremy chose our own decoder over StbImageSharp, a C port on unsafe pointers, since font PNGs are untrusted input). Code: `Internal/Bitmaps/` (`CbdtTable`, `SbixTable`, `BitmapGlyphs`, `BitmapResampler`), `Internal/Pipeline/BitmapGlyphRenderer.cs`, `Internal/Outlines/BitmapOnlyOutlineDecoder.cs`.

- Tables: CBLC index subtable formats 1-5 with CBDT PNG image formats 17, 18, 19; sbix `'png '` records with origin offsets and `'dupe'` (followed up to 4 hops). Other image formats (EBDT 1-9, uncompressed BGRA), `'jpg '`/`'tiff'`/`'pdf '`/`'mask'`, zero-length and malformed per-glyph records mean "no bitmap in this strike". Small metrics are read as horizontal whatever the strike flags say; sbix `ppi` and the "draw outlines" flag are not used.
- A face with no `glyf`/CFF loads when it has `CBLC`+`CBDT` or `sbix`; its outlines are empty and advances come from `hmtx`. Malformed or empty bitmap tables fail such a face at `LoadFont` (`FontFormatException`) and are ignored on a face with outlines.
- Strike choice, per glyph: the smallest strike with the glyph at or above the requested ppem, else the largest (the CBDT/sbix chapters' recommendation). The bitmap is scaled to the request: exact area averaging when shrinking, bilinear when enlarging, separable in premultiplied 8-bit sRGB; a scale of 1 at whole-pixel bearings copies it. The box is the outward-rounded scaled rectangle.
- Placement: CBDT bearings; sbix origin offsets from the pen, or from the `glyf` box corner (xMin, yMin) when the glyph has a contour. Advance: the unstyled `hmtx` advance at the requested size.
- FreeType's backend cannot scale strikes: it returns the 109 ppem bitmaps of Noto Color Emoji at any requested size. At the strike size Native equals it (metrics exact, premultiplied pixels within 1); at 16/32/64 px Native matches FreeType's strike scaled by an independent supersampler (SSIM ≥ 0.999 per channel).
- Precedence with `ColorFont`: `COLR` v1, `COLR` v0, bitmap, outline. Without `ColorFont` a face with outlines draws outlines; a bitmap-only face draws `Grayscale8` ink coverage, `alpha × (1 − linear Rec. 709 luminance)` (FreeType converts color bitmaps to gray too; SSIM 0.998 against it at 109 px).
- Options: bold/italic change neither pixels nor advance (FreeType's behavior); LCD and light hinting do not apply; `None` thresholds alpha (or coverage) at 50% and crops to the ink, like Native's COLR glyphs (FreeType ignores `None` for bitmaps); single-channel SDF keeps bitmap glyphs color; MSDF/MTSDF with color is rejected; a distance field without color on a bitmap-only face is a `NotSupportedException`.
- Errors: a PNG that does not decode, exceeds 4096 px per side, or disagrees with its CBDT metrics is a `RasterizationException`; the scaled box goes through `GlyphLimits`.

## Testing

- COLR v0: layered composition matches FreeType (SSIM on RGBA)
- Palette switch changes colors; foreground-color layers match FreeType
- Transparent layers blend correctly; non-color glyphs in a color font stay grayscale
- COLR v1: solid, each gradient type and extend mode, transforms, composite modes, `PaintColrGlyph` reuse
- Malformed/cyclic paint graphs fail gracefully
- Bitmaps (`NativeBitmapGlyphTests`, `NativeBitmapEmojiTests`): every index/image format, strike selection, scaling, sbix placement/dupe/skips, precedence, option interactions and hostile PNGs on hand-built tables; Noto Color Emoji against FreeType at 16/32/64/109 px; fuzz profiles for Noto Color Emoji and synthetic CBDT/sbix fonts; golden cases `notoemoji/*`

**Regression coverage**: `tests/bmfont-compare/gum-bmfont/color-colr.bmfc` (Bungee Color, `colorFont=1`), `color-colr-palette.bmfc` (same with `colorPalette=6`), `color-colrv1.bmfc` (Reem Kufi Ink's color marks and digits, `colorFont=1`) and `color-cbdt.bmfc` (Noto Color Emoji at 32 px, `colorFont=1`). The harness tolerates a backend failing a config, so `NativeColorFontTests` and `NativeBitmapEmojiTests` assert Native produces colored RGBA output.

## Success Criteria

- [x] COLR v0 + CPAL fixtures committed
- [x] CPAL parsed; palette selection works
- [x] COLR v0 layered rendering matches FreeType
- [x] COLR v1 paint graph (gradients, transforms, composites) renders
- [x] `SupportsColorFonts = true`; `ColorFont` no longer throws
- [x] CBDT/sbix implemented with our own PNG decoder
- [x] All tests pass; `regression_check.py` run with the COLR and CBDT configs
