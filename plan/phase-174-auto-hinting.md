# Phase 174 — Native Rasterizer: Auto-Hinting

> **Status**: In progress — y-only light hinting works for `AntiAlias = Light`; the default-request mapping (decision below) and x fitting remain
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (scale/hint hook); Phases 167/168 (hinting must run on the transformed outline)

## Goal

Heuristic auto-hinting (no TrueType bytecode — Phase 160 Q2/D5) that snaps blue zones and stems to the pixel grid, sharpening small sizes.

## Current state

- `GlyphHinter` (stage 4) runs `FontHintMetrics` (blue zones + standard horizontal stroke from reference glyphs, cached per face/size/style/instance) and `GridFitter` (y-only fitting, monotone interpolation) for `AntiAlias = Light` with `EnableHinting` on; the Native README "Hinting" section describes the behaviour. Every other request renders unhinted.
- Pipeline position is fixed by Phase 160 D14: after scaling to pixels, before flattening. Synthetic transforms and variation deltas happen earlier, in font units, and the zones are measured from the transformed reference glyphs.

## Open decision — hinting for default requests

`RasterOptions.EnableHinting` defaults to **true**, so hinting every `EnableHinting` request would change Native's default grayscale (and `None`) output and its `NativeOutputCharacterizationTests` hashes. It is therefore gated on `Light` only. Options: (a) keep `Light`-only (opt-in, default output stable); (b) hint grayscale too whenever `EnableHinting` is true (matches FreeType/GDI semantics, changes default Native output and every harness config with `useHinting=1`). The gate is `GlyphHinter.Applies`.

## Work items

### Per-font analysis (once per font load, cached; scaled per size)

- **Blue zones**: baseline, x-height, cap height, ascender, descender — from OS/2 `sxHeight`/`sCapHeight` and `hhea`, refined by measuring reference glyphs (`x`, `H`, `d`, `p`, `o` for overshoot).
- **Standard stem widths**: horizontal measured from `H`/`e`/`o` (done); vertical (`l`, `o`) only matters for x fitting (open).

### Per-glyph grid fitting

1. Detect near-horizontal and near-vertical segments; pair them into stems.
2. Snap stem edges in a blue zone to the zone's pixel edge (with overshoot suppression).
3. Round stem widths to whole pixels, preferring the standard width so `l`/`i`/`t` match.
4. Interpolate untouched points between hinted neighbors (IUP-like), off-curve points with their on-curve neighbors.
5. Horizontal-only (y-axis) fitting is the `Light` mode (done); full mode also fits x (open — mono/`None` hinting would change default `None` output, see the decision above).

### Integration

- `EnableHinting = false` (and every non-`Light` request) keeps output byte-identical.
- CFF fonts use the same heuristics (charstring hints are ignored); the fitter reads ink side from winding, so CFF's counter-clockwise outlines work (unit-tested; Native cannot load CFF until Phase 166).
- `AntiAliasMode.Light` is advertised in `SupportedAntiAliasModes`.
- SDF is never hinted.
- Advances are not touched (x is never fitted); `GetGlyphMetrics` runs the same stages as the bitmap.

## Testing

- `'H'`/`'z'`/`'p'` outermost rows solid at 12–18px; fewer partial pixels than unhinted; mean SSIM against hinted FreeType improves at 12/14/16/18px (`NativeHintingTests`)
- Blue zones on Roboto match `OS/2` heights; synthetic stems round to whole pixels on the grid (`AutoHinterTests`)
- `l`/`i`/`t` share stem width — needs x fitting (open)
- `Light` without hinting identical to grayscale; advances unchanged; `GetGlyphMetrics` equals the bitmap

**Regression coverage**: `hint-12/14/16/18.bmfc` and `nohint-12/18.bmfc` (Arial, `antiAlias=light`, `useHinting=1` vs `0`). Other backends' output must not change; Native's `nohint` output must match pre-phase output.

## Success Criteria

- [x] Blue zones and standard horizontal stems detected and cached per font
- [x] Horizontal stems snap to consistent integer widths; zones align
- [ ] Default-request mapping decided (see Open decision)
- [x] `Light` advertised once y-only fitting works
- [ ] X fitting (vertical stems, `l`/`i`/`t` widths)
- [x] All tests pass; `regression_check.py` run with the hinting pairs
