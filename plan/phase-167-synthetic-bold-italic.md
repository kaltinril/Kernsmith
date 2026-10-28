# Phase 167 — Native Rasterizer: Synthetic Bold & Italic

> **Status**: Future — Wave 1
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (staged pipeline: transform list, outline-derived bbox)

## Goal

Synthetic bold and italic as outline transforms in font units (Phase 160 D6), so the glyph is styled before rasterization — and before SDF, stroke and hinting — rather than dilated or sheared as a bitmap.

## Starting point

- `NativeCapabilities` reports `SupportsSyntheticBold`/`SupportsSyntheticItalic = false`, and `RasterizeGlyph` ignores `RasterOptions.Bold`/`Italic`.
- Core already decides *whether* to style: `PreparedRasterization.Prepare` clears `Bold`/`Italic` when the font is already bold/italic unless `ForceSyntheticBold`/`ForceSyntheticItalic` is set. The rasterizer applies the transform whenever the flag it receives is true — it must not re-check font style.
- Core already runs a caller-supplied `BoldPostProcessor`/`ItalicPostProcessor` only when the backend lacks the capability (Phase 150 Issue 5). Flipping the flags makes core skip them for Native, so the outline transform must actually work before the flags flip.

## Parity targets

Match the existing backends so switching to Native doesn't change weight or slant:

- **Bold strength**: FreeType's `EmboldenGlyph` — `max(0.5px, ppem / 24)` total, and the advance grows by that strength. StbTrueType uses `effectiveSize / 24`. Convert to font units: `strength_fu = strength_px * unitsPerEm / ppem`.
- **Italic**: horizontal shear `x += y * tan(12°)` (`0.2126`), the fixed angle FreeType's `FT_GlyphSlot_Oblique` and StbTrueType use. No `post` table is needed for this default; reading `post.italicAngle` for a configurable angle is a Phase 168 item.

## Work items

### Synthetic bold — outline offset

1. Per contour, determine winding (signed area, shoelace). With non-zero winding the outer and inner contours have opposite direction, so offsetting every point along its contour's outward normal (relative to winding) grows strokes and shrinks counters.
2. For each on-curve point: average the adjacent segment normals, normalize, scale by `strength_fu / 2`; at sharp corners scale by `1 / cos(halfAngle)`, capped at 2× to prevent spikes.
3. Off-curve control points move along the interpolated normal.
4. Advance width grows by `strength_fu`.

### Synthetic italic — shear

- Shear all points (on- and off-curve); exact for Béziers. Advance unchanged.

### Pipeline

- Add `BoldTransform` and `ItalicTransform` to the Phase 166 transform list, bold before italic (expansion stays uniform, not sheared).
- Bbox comes from the transformed outline (Phase 166 Part 1), so slanted glyphs are not clipped.
- `GetGlyphMetrics` reports the styled bbox and advance.
- Flip `SupportsSyntheticBold`/`SupportsSyntheticItalic` to `true`; update the `NativeRasterizer`/`NativeCapabilities` XML comments, the Native README, `docs/rasterizers/native.md` and REF-12.
- `SyntheticStyleFallbackTests` uses Native as its "backend without the capability" case. Move that case to a test-stub rasterizer that reports `false` (the core gate still needs coverage), and add a Native case asserting the fallback is now skipped.

## Testing

- Bold `'O'`: outer contour grows, counter shrinks but stays open; bold `'I'`: stem thickens uniformly
- Strengths at 12/32/72px: no spikes, no self-intersection artifacts at sharp corners
- Italic `'A'`: shear angle correct; bbox contains all ink
- Bold + italic combined
- Metrics and SSIM against FreeType synthetic bold/italic at the `NativeMetricsAgreementTests` sizes
- No counter fill-in at 12px (`'e'`, `'B'`)

**Regression coverage**: `GenerateAll` already synthesizes `plain-bold` / `plain-italic` variants from `plain.bmfc`; they start exercising Native's outline path. Add an assertion (unit or integration) that Native's bold output is wider than its regular output, since the harness can't tell "styled" from "silently unstyled".

## Success Criteria

- [ ] Synthetic bold expands outlines (outer out, counters in) at FreeType's strength
- [ ] Synthetic italic shears at 12°
- [ ] Bold + italic compose; bbox and advance correct
- [ ] No counter fill-in at small sizes
- [ ] Capabilities report `true`; fallback tests updated
- [ ] All tests pass; `regression_check.py` run
