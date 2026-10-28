# Phase 173 — Native Rasterizer: LCD Subpixel Rendering & AA-Mode Negotiation

> **Status**: Future — Wave 2
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch); Phase 174 for `Light`

## Goal

LCD subpixel output from the Native rasterizer, and core anti-alias-mode negotiation so no backend is ever handed a mode it does not advertise.

## Starting point

- **Core does no AA-mode negotiation.** `IRasterizerCapabilities.SupportedAntiAliasModes` is never read in `src/KernSmith`; `RasterOptions.AntiAlias` is passed through exactly as requested.
- Native advertises `None` and `Grayscale`, but `ScanlineRasterizer` thresholds only for `None` and renders **every other mode — `Light` and `Lcd` included — as grayscale**, silently.
- FreeType honors `Light` (`FT_RENDER_MODE_LIGHT`) and `Lcd` (`FT_RENDER_MODE_LCD`).

## Work items

### Core AA-mode negotiation (do first)

1. In `PreparedRasterization.Prepare`, resolve the effective mode against `SupportedAntiAliasModes` before building `RasterOptions`: pass a supported mode through; degrade an unsupported one along `Lcd → Grayscale`, `Light → Grayscale`; `None` is always supported.
2. Decide whether degradation is silent or surfaced (warning in `PipelineMetrics`/result, or an exception in a strict mode) — ask Jeremy; the default must not break existing configs that request `Light` on GDI/DirectWrite/StbTrueType today.
3. Use the effective mode everywhere downstream (`.fnt` `smooth`, `OutlineEffect` smoothing) so metadata matches pixels.
4. Check every backend's `SupportedAntiAliasModes` is truthful before turning negotiation on, and run `regression_check.py` — any backend that currently receives an unadvertised mode will change output.

### Native LCD

- Harmony method first: render coverage three times with the outline shifted −1/3, 0, +1/3 px horizontally; R/G/B = the three coverages.
- ClearType-style alternative: 3× horizontal render + 5-tap FIR (1/9, 2/9, 3/9, 2/9, 1/9), sample every third value.
- Output `PixelFormat.Rgba32`, RGB = subpixel coverage, A = max (or mean) of the three — decide by checking how FreeType's LCD output reaches the atlas today, so Native matches.
- Configuration: RGB/BGR order, horizontal orientation (vertical optional). Gamma: FreeType's backend applies `RasterOptions.Gamma`; decide whether Native honors `Gamma` for LCD only or for all modes.
- Advertise `Lcd` in `NativeCapabilities.SupportedAntiAliasModes`.

### `Light`

- Native advertises `Light` only once Phase 174 provides light hinting; until then negotiation degrades it to `Grayscale`.

## Testing

- Negotiation: requesting `Lcd`/`Light` on a backend without them yields the degraded mode in both pixels and `.fnt` metadata; supported modes pass through unchanged
- `'l'` at 12–16px: three distinct channel coverages; BGR swaps channels
- Harmony output shows no color fringing on flat edges; FIR variant matches its kernel
- SSIM vs FreeType LCD (Phase 179 threshold > 0.85)

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/lcd.bmfc` (Georgia, `antiAlias=lcd`) and force-add it. Run the harness once with negotiation alone (expect changes only where a backend was handed an unadvertised mode) and again with Native LCD. Assert in a test that Native's LCD output is actually RGB-distinct, not grayscale.

## Success Criteria

- [ ] Core negotiates AA mode against `SupportedAntiAliasModes`; metadata follows the effective mode
- [ ] Native Harmony LCD (and FIR variant) with RGB/BGR
- [ ] `Lcd` advertised by Native
- [ ] All tests pass; `regression_check.py` run with `lcd.bmfc`
