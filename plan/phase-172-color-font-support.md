# Phase 172 — Native Rasterizer: Color Font Support (COLR/CPAL)

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration)

## Goal

Implement color font rendering for COLR v0 (layered color glyphs) and COLR v1 (paint graph), enabling emoji and decorative color fonts.

## Scope

### CPAL Table Parser
- Parse color palettes: array of RGBA color entries
- Support multiple palettes (palette index from `RasterOptions.ColorPaletteIndex`)
- Parse palette type flags and labels (v1)

### COLR v0 — Layered Color Glyphs
- Parse base glyph records: map base glyph ID → (first layer index, num layers)
- Parse layer records: array of (glyph ID, palette index) pairs
- Rendering: for each layer, rasterize the referenced glyph, colorize with palette color, composite bottom-to-top using alpha-over blending
- Output: `PixelFormat.Rgba32`

### COLR v1 — Paint Graph
- Parse paint tables recursively (tree structure):
  - `PaintColrLayers`: ordered list of paint layers
  - `PaintSolid`: solid fill color (palette index + alpha)
  - `PaintLinearGradient`: linear gradient between two points with color stops
  - `PaintRadialGradient`: radial gradient between two circles
  - `PaintSweepGradient`: angular sweep gradient
  - `PaintGlyph`: clip rendering to a glyph outline (mask)
  - `PaintColrGlyph`: reuse another color glyph definition
  - `PaintTransform`: apply 2×3 affine matrix
  - `PaintTranslate`, `PaintScale`, `PaintRotate`, `PaintSkew`: specific transforms
  - `PaintComposite`: Porter-Duff compositing modes
- Implement paint tree walker that evaluates recursively
- Gradient color stops: interpolate in sRGB space (per spec)
- Porter-Duff modes: Clear, Src, Dest, SrcOver, DestOver, SrcIn, DestIn, SrcOut, DestOut, SrcAtop, DestAtop, Xor, Plus

### Rendering Architecture

```
PaintGraph Walker
  → For PaintGlyph: rasterize glyph outline as alpha mask
  → For PaintSolid/Gradient: generate color fill
  → For PaintComposite: composite layers with Porter-Duff mode
  → For PaintTransform: transform coordinate space
  → Intermediate: render to RGBA temp buffers
  → Final: composite all layers → output RGBA bitmap
```

### Integration
- Detect color glyphs via COLR table during `RasterizeGlyph`
- If codepoint has a COLR entry, use color rendering path
- If no COLR entry, fall back to standard grayscale rasterization
- Output format: `PixelFormat.Rgba32` for color glyphs
- Update `IRasterizerCapabilities.SupportsColorFonts = true`

**Palette selection**: The `IRasterizer.SelectColorPalette(int paletteIndex)` method is the stateful setter called by the main pipeline before rasterization begins. `RasterOptions.ColorPaletteIndex` is read by the main pipeline and forwarded via `SelectColorPalette()`. The Native rasterizer stores the palette index from `SelectColorPalette()` and uses it during color glyph rendering. The rasterizer does NOT read `RasterOptions.ColorPaletteIndex` directly — the pipeline handles that translation.

### Bitmap Color Fonts (CBDT/CBLC, sbix) — Lower Priority
- Parse CBLC index to find bitmap strike closest to requested size
- Extract PNG/bitmap data from CBDT
- Parse sbix table for Apple bitmap glyphs
- Scale bitmap to requested size if exact strike not available
- This is a stretch goal — COLR is the primary target

## Testing

- COLR v0: render a color emoji font, verify layered composition
- COLR v1: render paint graph glyphs, verify gradient fills and compositing
- Palette selection: switch palettes, verify color changes
- Transparent layers: verify alpha blending
- Fallback: non-color glyphs from a color font should render in grayscale
- Compare against FreeType color output

## Success Criteria

- [ ] CPAL palettes parsed correctly
- [ ] COLR v0 layered rendering works
- [ ] COLR v1 paint graph renders (at least PaintSolid, PaintGlyph, PaintColrLayers)
- [ ] Gradients render correctly (linear, radial, sweep)
- [ ] Porter-Duff compositing modes work
- [ ] `SupportsColorFonts = true` in capabilities
- [ ] All tests pass
