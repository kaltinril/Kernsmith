# Phase 173 — Native Rasterizer: LCD Subpixel Rendering & AA-Mode Negotiation

> **Status**: In progress — negotiation and Native LCD (pixel-box filter, RGB) shipped; filter/order options and the degradation policy await Jeremy
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch); Phase 174 for `Light`

## Goal

LCD subpixel output from the Native rasterizer, and core anti-alias-mode negotiation so no backend is ever handed a mode it does not advertise.

## Core AA-mode negotiation (done)

- `AntiAliasNegotiation.Resolve` (`src/KernSmith/Rasterizer/`) runs in `PreparedRasterization.Prepare` and `BmFont.QueryAtlasSize`: an advertised mode passes through, `None` always passes, an unadvertised `Light`/`Lcd` becomes `Grayscale`, and a backend advertising neither the mode nor `Grayscale` gets the request unchanged.
- Degradation never crosses the `None` boundary, so `.fnt` `smooth` and the outline effect's smoothing (both `AntiAlias != None` on the caller's options) always describe the pixels. The caller's `FontGeneratorOptions` is not rewritten.
- Every in-repo backend's declared modes were checked against what it renders: GDI, DirectWrite and StbTrueType advertise `None`/`Grayscale` and already rendered `Light`/`Lcd` as grayscale; FreeType advertises and honors all four. Negotiation therefore changes no existing output (harness: identical outside Native's LCD column).

## Native LCD (done)

- `LcdRenderer` (`Internal/Pipeline/`): fills the outline at 3x horizontal resolution (flattened at a third of the usual tolerance), filters each row with an `LcdFilter`, and writes `PixelFormat.Rgba32` with R/G/B = subpixels in panel order and A = max(R, G, B) — every pixel stays a valid premultiplied colour, and full coverage is the same white-plus-alpha a grayscale glyph becomes on an RGBA page.
- Default filter `LcdFilter.PixelBox` ([1,1,1]/3): a one-pixel window per subpixel, which equals the "Harmony" three-renders-shifted-by-1/3-px method. The FreeType backend's LCD output measures as exactly this filter (per-channel SSIM 0.98–1.00 on Roboto, 12–32 px unhinted). `LcdFilter.Triangle` ([1,2,3,2,1]/9, ClearType-style) and `LcdSubpixelOrder.Bgr` exist internally and are tested.
- The bitmap widens by the filter's spill: `Measure` and `Render` share one column computation from the outline's tight horizontal extent, so they match exactly and no blank column is added.
- `Gamma` is not applied to LCD, matching the FreeType backend (it gamma-corrects grayscale coverage only). Native ignores `Gamma` in every mode.
- With `ColorFont`, a glyph with `COLR` layers renders its color layers and ignores `Lcd` (as the FreeType backend does, byte-identical to the `Grayscale` color result); glyphs without layers still render LCD. Synthetic bold/italic transforms apply before the LCD renderer, and to every color layer.
- `Light` renders as grayscale coverage; Native does not advertise it, so negotiation degrades it before Native sees it. It becomes real with Phase 174's light hinting.

## Remaining

- **Public LCD options** — expose filter (`PixelBox`/`Triangle`) and subpixel order (RGB/BGR) through `RasterOptions`/`FontGeneratorOptions`, `.bmfc` and the CLI. Needs an API decision; vertical (LCD_V) layouts too, if wanted (the FreeType backend exposes neither).
- **Degradation policy** (open for Jeremy) — today silent. Alternatives: record the effective mode in `PipelineMetrics`/the result as a warning, or an opt-in strict mode that throws. Silent is the only option that keeps existing `Light` configs on GDI/DirectWrite/StbTrueType generating unchanged.
- **FreeType backend LCD output** (not Native) — it copies FreeType's raw LCD buffer as `Grayscale8` with three bytes per pixel, so glyphs land 3x wide in the atlas and `GetGlyphMetrics` width disagrees with the bitmap. Fixing it changes FreeType output; separate decision.
- LCD with `SuperSampleLevel > 1` box-filters the subpixel pattern down with the glyph (same for any backend); decide whether LCD should render at final size instead.

## Testing

- `LcdFilterTests`: taps sum to the denominator and are symmetric; impulse response is the kernel; pixel box equals three 1/3-px-shifted pixel renders.
- `LcdRendererTests`: analytic filtered coverage of rectangles at subpixel offsets (both filters, both orders); horizontal edges stay neutral; `'l'` at 12–16 px has three distinct channels; BGR = RGB with R/B swapped; `Measure == Render`; no ink lost; per-channel SSIM vs FreeType > 0.99; an LCD atlas is RGB-distinct with `smooth=1`.
- `LcdInteractionTests`: `COLR` glyphs render identically under `Lcd` and `Grayscale` (Native and FreeType), unlayered glyphs stay LCD with `ColorFont` on, and bold/italic reach LCD glyphs and color layers.
- `AntiAliasNegotiationTests`: the policy table, the effective mode reaching `RasterizeAll`/`GetFontMetrics`/`GetGlyphMetrics`, and StbTrueType `Light`/`Lcd` output byte-identical to `Grayscale`.
- Harness: `tests/bmfont-compare/gum-bmfont/lcd.bmfc` (Georgia 24, `antiAlias=lcd`).

## Success Criteria

- [x] Core negotiates AA mode against `SupportedAntiAliasModes`; metadata follows the effective mode
- [x] Native pixel-box (Harmony) LCD; FIR variant and BGR implemented internally
- [ ] LCD filter and subpixel order user-selectable
- [x] `Lcd` advertised by Native
- [x] All tests pass; `regression_check.py` run with `lcd.bmfc`
