# Phase 167 — Native Rasterizer: Synthetic Bold & Italic

> **Status**: Complete
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (staged pipeline: transform list, outline-derived bbox)

## Goal

Synthetic bold and italic as outline transforms in font units (Phase 160 D6), so the glyph is styled before rasterization — and before SDF, stroke and hinting — rather than dilated or sheared as a bitmap.

## Contract with core

- `PreparedRasterization.Prepare` clears `Bold`/`Italic` when the font is already bold/italic unless `ForceSyntheticBold`/`ForceSyntheticItalic` is set. Native applies the transform whenever the flag it receives is true and never re-checks the font's style.
- Native reports `SupportsSyntheticBold`/`SupportsSyntheticItalic = true`, so core skips a caller-supplied `BoldPostProcessor`/`ItalicPostProcessor` for it, as for every other backend.

## Parity targets (FreeType backend)

- **Bold strength**: `FreeTypeRasterizer.EmboldenGlyph` — `max(32, y_ppem * 64 / 24)` in 26.6, i.e. `max(0.5px, ppem/24)` with FreeType's whole-pixel `y_ppem`. `BoldTransform.StrengthInFontUnits` reproduces the integer arithmetic exactly and divides by Native's pixel scale. The advance grows by the strength.
- **Bold geometry**: FreeType's `FT_Outline_EmboldenXY`, ported in `OutlineEmboldener` (FTL notice in `THIRD-PARTY-NOTICES.md`): one global orientation from the total signed area, so outer contours grow and counters shrink; each control-polygon vertex moves strength/2 along the miter bisector; no lateral shift for turns sharper than ~160°; inward-corner shift capped at `shortestEdge / sin(turn)`; then the whole outline shifts by strength/2 up and right, so the left and bottom edges stay put. Native offsets its cubic control polygon where FreeType offsets the quadratic one — the residual difference is a fraction of a pixel.
- **Italic**: `x += y · 0x0366A/65536` (FreeType's 16.16 tan 12°, `FT_GlyphSlot_Oblique`) on every point, about the baseline; advance unchanged.
- **Order**: bold, then italic (FreeType's), so the expansion is uniform and the result is slanted as a whole.

## Implementation

- `Internal/Pipeline/BoldTransform.cs`, `ItalicTransform.cs`; `GlyphTransforms.For(options, unitsPerEm)` returns `[]` for unstyled requests (keeping the `glyf` header bbox and the characterized output), otherwise bold then italic.
- Bounds come from the transformed outline (`OutlineBounds.Tight` via `WithOutline`), so slanted and emboldened glyphs are never clipped; `GetGlyphMetrics` runs the same stages and reports the styled box and advance.
- An outline with no signed area is left unstyled, advance included (FreeType's embolden rejects it and the backend then skips its metric adjustment). A blank glyph (space) still grows its advance, as in FreeType.

## Known difference from FreeType

FreeType sizes its bitmap from the outline's control box; Native uses the tight bounds, so a styled FreeType bitmap can carry a blank column or two that Native's lacks. Ink placement agrees within a pixel. FreeType's own `GetGlyphMetrics` reports unsheared bearings for italic (its slot metrics are not updated by `FT_GlyphSlot_Oblique`), so it is not a reference for styled bearings.

## Testing

- `SyntheticStyleTransformTests` — exact geometry: a square grows by the full strength from its lower-left corner in either winding; a square ring's counter closes by the strength; a circle grows by the strength without distortion; an acute apex does not spike; coincident control points stay finite; a blank glyph's advance grows; the shear maps `(x, y) → (x + y·tan12°, y)` including cubic controls; strength equals FreeType's at 32/48/6/17.5px and 96 DPI; transform list order.
- `NativeSyntheticStyleTests` — against FreeType (hinting off) for bold, italic and bold+italic at 12/16/24/32/48/96px: ink bounds and advance within ±1px over the metrics sample; SSIM ≥ 0.95 and above the unstyled glyph's score; `GetGlyphMetrics` equals the rendered metrics; bold `'o'`/`'e'`/`'B'` counters stay open at 12px.
- `SyntheticStyleFallbackTests` — the no-capability gate runs against a stub backend; Native skips the fallback for bold and italic; Native's bold `'A'` is wider than regular through `BmFont.Generate`.
- `NativeOutputCharacterizationTests` stays green: default options never reach a transform.
- **Regression**: `regression_check.py` — the `isBold`/`isItalic` configs and the synthesized `plain-bold`/`plain-italic` variants change for Native only.
