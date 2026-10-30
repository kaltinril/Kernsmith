# Phase 174 — Native Rasterizer: Auto-Hinting

> **Status**: Complete — y-only light hinting for `AntiAlias = Light` with mono dropout control; hinting stays `Light`-only and x fitting is not planned (both decided)
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (scale/hint hook); Phases 167/168 (hinting must run on the transformed outline)

## Goal

Heuristic auto-hinting (no TrueType bytecode — Phase 160 Q2/D5) that snaps blue zones and stems to the pixel grid, sharpening small sizes.

## Current state

- `GlyphHinter` (stage 4) runs `FontHintMetrics` (blue zones + standard horizontal stroke from reference glyphs, cached per face/size/style/instance) and `GridFitter` (y-only fitting, monotone interpolation) for `AntiAlias = Light` with `EnableHinting` on; the Native README "Hinting" section describes the behaviour. Every other request renders unhinted.
- Pipeline position is fixed by Phase 160 D14: after scaling to pixels, before flattening. Synthetic transforms and variation deltas happen earlier, in font units, and the zones are measured from the transformed reference glyphs.

## Decisions

- **Hinting stays `Light`-only** (decided: option (a)). `RasterOptions.EnableHinting` defaults to **true**, so hinting every `EnableHinting` request would change Native's default grayscale (and `None`) output, its `NativeOutputCharacterizationTests` hashes and every harness config with `useHinting=1`. The gate is `GlyphHinter.Applies`.
- **No x fitting** (decided): `Light` is y-only by design, as in FreeType's light mode, so advances and horizontal positions stay exactly as unhinted. Vertical-stem widths (`l`/`i`/`t` sharing a width) are therefore out of scope.

## Work items

### Per-font analysis (once per font load, cached; scaled per size)

- **Blue zones**: baseline, x-height, cap height, ascender, descender — from OS/2 `sxHeight`/`sCapHeight` and `hhea`, refined by measuring reference glyphs (`x`, `H`, `d`, `p`, `o` for overshoot).
- **Standard stem widths**: horizontal, measured from `H`/`e`/`o`.

### Per-glyph grid fitting

1. Detect near-horizontal and near-vertical segments; pair them into stems.
2. Snap stem edges in a blue zone to the zone's pixel edge (with overshoot suppression).
3. Round stem widths to whole pixels, preferring the standard width so `l`/`i`/`t` match.
4. Interpolate untouched points between hinted neighbors (IUP-like), off-curve points with their on-curve neighbors.
5. Fitting is y-only (the `Light` mode); there is no x-fitting mode.

### Integration

- `EnableHinting = false` (and every non-`Light` request) keeps output byte-identical.
- CFF fonts use the same heuristics (charstring hints are ignored); the fitter reads ink side from winding, so CFF's counter-clockwise outlines work.
- `AntiAliasMode.Light` is advertised in `SupportedAntiAliasModes` (with `None`, `Grayscale`, `Lcd`).
- Distance fields (SDF, MSDF, MTSDF) and `Lcd` are never hinted; `COLR` version 0 layers and version 1 glyph clips are (they go through the same pipeline).
- Mono dropout control (`AntiAlias = None`) applies to every outline format and to `COLR` version 1 glyph clips.
- Advances are not touched (x is never fitted); `GetGlyphMetrics` runs the same stages as the bitmap.

## Testing

- `'H'`/`'z'`/`'p'` outermost rows solid at 12–18px; fewer partial pixels than unhinted; mean SSIM against hinted FreeType improves at 12/14/16/18px (`NativeHintingTests`)
- Blue zones on Roboto match `OS/2` heights; synthetic stems round to whole pixels on the grid (`AutoHinterTests`)
- `Light` without hinting identical to grayscale; advances unchanged; `GetGlyphMetrics` equals the bitmap
- Interplay (`NativeHintingInterplayTests`): CFF 'x' x-height row snaps; SDF/MSDF/MTSDF and `Lcd` unchanged by hinting; `COLR` v0/v1 alpha equals hinted plain coverage; ExtraLight CFF stems survive mono; v1 mono alpha equals plain mono

**Regression coverage**: `hint-12/14/16/18.bmfc` and `nohint-12/18.bmfc` (Arial, `antiAlias=light`, `useHinting=1` vs `0`). Other backends' output must not change; Native's `nohint` output must match pre-phase output.

## Success Criteria

- [x] Blue zones and standard horizontal stems detected and cached per font
- [x] Horizontal stems snap to consistent integer widths; zones align
- [x] Default-request mapping decided (`Light` only)
- [x] `Light` advertised once y-only fitting works
- [x] X fitting decided against (y-only by design)
- [x] All tests pass; `regression_check.py` run with the hinting pairs
