# Phase 174 — Native Rasterizer: Auto-Hinting

> **Status**: Future — Wave 2
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (scale/hint hook); Phases 167/168 (hinting must run on the transformed outline)

## Goal

Heuristic auto-hinting (no TrueType bytecode — Phase 160 Q2/D5) that snaps blue zones and stems to the pixel grid, sharpening small sizes.

## Starting point

- Native ignores `RasterOptions.EnableHinting` (default true; `.bmfc` `useHinting`) and renders unhinted, so small sizes are softer than FreeType or GDI.
- Pipeline position is fixed by Phase 160 D14: after scaling to pixels, before flattening — i.e. the Phase 166 Part 1 scale/hint hook. Synthetic transforms happen earlier, in font units.

## Work items

### Per-font analysis (once per font load, cached; scaled per size)

- **Blue zones**: baseline, x-height, cap height, ascender, descender — from OS/2 `sxHeight`/`sCapHeight` and `hhea`, refined by measuring reference glyphs (`x`, `H`, `d`, `p`, `o` for overshoot).
- **Standard stem widths**: measured from reference glyphs (`l`, `o` vertical; `H`/`e` horizontal).

### Per-glyph grid fitting

1. Detect near-horizontal and near-vertical segments; pair them into stems.
2. Snap stem edges in a blue zone to the zone's pixel edge (with overshoot suppression).
3. Round stem widths to whole pixels, preferring the standard width so `l`/`i`/`t` match.
4. Interpolate untouched points between hinted neighbors (IUP-like), off-curve points with their on-curve neighbors.
5. Horizontal-only (y-axis) fitting is the `Light` mode; full mode also fits x.

### Integration

- Runs only when `EnableHinting` is true; `false` keeps today's output byte-identical.
- CFF fonts use the same heuristics (charstring hints are ignored).
- Advertise `AntiAliasMode.Light` in `SupportedAntiAliasModes` (y-only fitting), relying on Phase 173's core negotiation.
- Decide whether hinting also applies under SDF/MSDF (FreeType does not hint SDF) — default: skip for SDF.
- Advance widths: decide whether to round hinted advances (FreeType does with hinting on) and keep `GetGlyphMetrics` consistent with the bitmap.

## Testing

- `'H'` cap height and `'x'` x-height land on pixel boundaries; `'p'` descender snaps
- `l`/`i`/`t` share stem width at each of 12, 14, 16, 18, 24px
- Hinted vs unhinted: higher edge contrast on horizontals (measurable, not just visual)
- `EnableHinting = false` output identical to pre-phase output
- Metrics vs FreeType (hinted) within ±1px

**Regression coverage**: add hinting on/off pairs at small sizes — e.g. `hint-12.bmfc` / `nohint-12.bmfc` and `hint-18.bmfc` / `nohint-18.bmfc` (Georgia or Arial, `useHinting=1` vs `0`) — and force-add them. Other backends' output must not change; Native's `nohint` output must match pre-phase output.

## Success Criteria

- [ ] Blue zones and standard stems detected and cached per font
- [ ] Stems snap to consistent integer widths; zones align
- [ ] `EnableHinting` respected; off = unchanged output
- [ ] `Light` advertised once y-only fitting works
- [ ] All tests pass; `regression_check.py` run with the hinting pairs
