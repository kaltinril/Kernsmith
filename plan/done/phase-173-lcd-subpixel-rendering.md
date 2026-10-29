# Phase 173 — Native Rasterizer: LCD Subpixel Rendering & AA-Mode Negotiation

> **Status**: Complete — negotiation, Native LCD with public filter/subpixel-order options, LCD rendered at final size under super sampling
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (render-mode dispatch); Phase 174 for `Light`

## Goal

LCD subpixel output from the Native rasterizer, and core anti-alias-mode negotiation so no backend is ever handed a mode it does not advertise.

## Core AA-mode negotiation

- `AntiAliasNegotiation.Resolve` (`src/KernSmith/Rasterizer/`) runs in `PreparedRasterization.Prepare` and `BmFont.QueryAtlasSize`: an advertised mode passes through, `None` always passes, an unadvertised `Light`/`Lcd` becomes `Grayscale`, and a backend advertising neither the mode nor `Grayscale` gets the request unchanged.
- Degradation is **silent** (decided): no warning, no strict mode. It is the only policy that keeps existing `Light` configs on GDI/DirectWrite/StbTrueType generating unchanged.
- Degradation never crosses the `None` boundary, so `.fnt` `smooth` and the outline effect's smoothing (both `AntiAlias != None` on the caller's options) always describe the pixels. The caller's `FontGeneratorOptions` is not rewritten.
- Every in-repo backend's declared modes were checked against what it renders: GDI, DirectWrite and StbTrueType advertise `None`/`Grayscale` and already rendered `Light`/`Lcd` as grayscale; FreeType advertises and honors all four. Negotiation therefore changes no existing output.

## Native LCD

- `LcdRenderer` (`Internal/Pipeline/`): fills the outline at 3x resolution across the panel's stripes (flattened at a third of the usual tolerance), filters along that axis with an `LcdFilterKernel`, and writes `PixelFormat.Rgba32` with R/G/B = subpixels in panel order and A = max(R, G, B) — every pixel stays a valid premultiplied colour, and full coverage is the same white-plus-alpha a grayscale glyph becomes on an RGBA page.
- The bitmap grows by the filter's spill along the subpixel axis only: `Measure` and `Render` share one span computation from the outline's tight extent, so they match exactly and no blank column (row) is added.
- `Gamma` is not applied to LCD, matching the FreeType backend. With `ColorFont`, a glyph with `COLR` layers renders its color layers and ignores `Lcd` (as on FreeType); glyphs without layers still render LCD. Synthetic bold/italic apply before the LCD renderer. LCD is never hinted; `Light` is grayscale plus Phase 174's auto-hinting.

## Public options

- `LcdFilter` (`Default`, `Light`, `Smooth`, `None`; FreeType/fontconfig preset names) and `LcdSubpixelOrder` (`Rgb`, `Bgr`, `VRgb`, `VBgr`; fontconfig `rgba` names) on `FontGeneratorOptions` and `RasterOptions`, `BmFontBuilder.WithLcdFilter`/`WithLcdSubpixelOrder`, `.bmfc` `lcdFilter=`/`lcdOrder=` (written only when not the default; unknown values read as the default), CLI `--lcd-filter`/`--lcd-order` (unknown values are an error).
- Native kernels: `Default` and `Light` = one-pixel box [1,1,1]/3 (equal to three renders shifted by 1/3 px; what the FreeType backend's LCD output measures as, SSIM 0.98–1.00), so existing LCD output is unchanged; `Smooth` = [1,2,3,2,1]/9; `None` = raw one-third-pixel coverage.
- `Bgr`/`VBgr` swap R and B. `VRgb`/`VBgr` run the same code in a transposed frame (the outline is mirrored across the pixel diagonal *before* flattening, because the flattener drops horizontal edges), so a vertical render is exactly the horizontal render of the mirrored glyph, transposed.
- Other backends ignore both options (StbTrueType/GDI/DirectWrite degrade `Lcd` to grayscale; the FreeType backend renders its own LCD).

## LCD and super sampling

- `PreparedRasterization.Prepare` skips super sampling when the *effective* mode is `Lcd` (and no distance field): the backend gets the final size and `SuperSample = 1`, so the subpixel pattern is not box-filtered away. Output equals the `SuperSampleLevel = 1` LCD output exactly. A backend that degraded `Lcd` to grayscale still supersamples. This applies to the FreeType backend's `Lcd` too.

## Out of scope

- The FreeType backend's own LCD output (three bytes per pixel labelled `Grayscale8`) is being fixed separately; it is not Native work.

## Testing

- `LcdFilterTests`: taps sum to the denominator and are symmetric; impulse response is the kernel; pixel box equals three 1/3-px-shifted renders; each public preset maps to its kernel.
- `LcdRendererTests`: analytic filtered coverage of rectangles at subpixel offsets for every kernel and order (vertical orders down each column); horizontal (vertical) edges stay neutral on horizontal (vertical) panels; a vertical render equals the transposed horizontal render of the mirrored glyph, byte for byte; BGR = RGB with R/B swapped; `Measure == Render`; no ink lost; per-channel SSIM vs FreeType > 0.99.
- `LcdOptionsTests` (Native): dispatch builds the requested renderer, undefined values throw, options are ignored outside `Lcd`, metrics match renders for every combination, `Default` = `Light` = pre-option output, options reach `BmFont.Generate`, and LCD with super sampling 2–4 equals the non-supersampled output.
- `LcdOptionsTests` (core): defaults, `FromGeneratorOptions`, builder and `FromConfig`, `.bmfc` round trips, super sampling skipped only for an effective `Lcd`, StbTrueType and FreeType output unchanged by the options. CLI tests cover the flags, config overlay and rejection.
- `LcdInteractionTests`, `AntiAliasNegotiationTests`: color/style interplay and the negotiation policy.
- Golden masters: `roboto/lcd-{smooth,none,bgr,vrgb,vbgr}/{12,16,32}px`, `roboto/lcd-vbgr-smooth/24px/bold-italic`, `sourceserif/lcd-vrgb/24px`, `notosans-cff/lcd-smooth/24px`, `generate/notosans/lcd-24-vrgb-smooth`.
- Harness: `lcd.bmfc`, `lcd-bgr.bmfc`, `lcd-vrgb-smooth.bmfc`, `lcd-supersample.bmfc` (Georgia 24).

## Success Criteria

- [x] Core negotiates AA mode against `SupportedAntiAliasModes`; metadata follows the effective mode; degradation is silent
- [x] Native pixel-box (Harmony) LCD, five-tap and unfiltered kernels, RGB/BGR and vertical orders
- [x] LCD filter and subpixel order user-selectable (API, builder, `.bmfc`, CLI)
- [x] LCD renders at final size under super sampling
- [x] `Lcd` advertised by Native
- [x] All tests pass; `regression_check.py` run with the LCD configs
