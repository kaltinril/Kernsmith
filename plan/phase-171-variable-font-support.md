# Phase 171 — Native Rasterizer: Variable Font Support

> **Status**: In progress — Wave 1 (TrueType: `avar` v1, `gvar`, `HVAR`, `MVAR`) complete; Wave 2 (CFF2 `blend`) pending Phase 166 Part 2 and a CFF2 fixture
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (decoder stage) for TrueType; Phase 166 Part 2 (CFF2 structure) for CFF2

## Goal

Render variable fonts at user-chosen axis values in the Native rasterizer: glyph outlines, advances and font metrics all follow the instance.

## Starting point

- Core already parses `fvar`: `PreparedRasterization.Prepare` (and the metrics-only path in `BmFont.cs`) call `IRasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes)` when the capability is true. Native does **not** need its own `fvar` axis parser — it receives the axis list. It may still need `fvar` for named instances only if they become user-selectable.
- FreeType (`FreeTypeRasterizer.SetVariationAxes`) is the quality baseline.
- Fixture: `tests/KernSmith.Tests/Fixtures/RobotoFlex-Variable.ttf` has `fvar`, `avar`, `gvar`, `HVAR` and `MVAR`. No CFF2 fixture is committed.

## Work items — TrueType variations (Wave 1)

### Coordinates

- `SetVariationAxes`: clamp each user value to the axis range, normalize to [-1, 1] (`(v - default) / (default - min)` below default, `(v - default) / (max - default)` above), then apply `avar` (v1 segment maps; piecewise linear). Unknown axis tags are ignored; unspecified axes stay at default.

### ItemVariationStore

- Shared parser for `HVAR`/`MVAR` (and CFF2 later): region list, item variation data, delta-set index maps; region scalar = product of per-axis tent factors.

### `gvar`

- Shared tuples, per-glyph tuple variation headers, private/shared point numbers, packed deltas.
- Tuple scalar: product over axes of the tent (start/peak/end, with implicit intermediate regions).
- IUP: interpolate deltas for untouched points per contour (between touched neighbors by coordinate; outside the range takes the nearest delta).
- Phantom points: the four trailing deltas adjust advance and side bearings when `HVAR` is absent.
- Composite glyphs: component offsets vary; apply deltas to the composite's components per spec.
- Applied in the `TrueTypeOutlineDecoder` before any transform, so bold/SDF/stroke see the varied outline. The bbox comes from the varied outline (Phase 166 Part 1), not the static `glyf` header.

### Metrics

- `HVAR` advance deltas (fall back to phantom points); `MVAR` deltas for the OS/2 / `hhea` values `GetFontMetrics` reads (`winAscent`/`winDescent` tags, ascender, descender, line gap).

### Integration

- `SupportsVariableFonts = true`; update README backend table, Native README, `docs/rasterizers/native.md`, REF-12.
- Cache per-glyph varied outlines per instance if Phase 177 shows `gvar` decode dominating.

### As built (Wave 1)

- Code lives in `Internal/Variations/`; `FontVariations` holds the instance and is read by `TrueTypeOutlineDecoder` and `GetFontMetrics`. A CFF2 decoder should read the same `FontVariations` coordinates and reuse `ItemVariationStore`.
- `avar` v2 axis mixing is not implemented; a v2 table gets its v1 segment maps only.
- `USE_MY_METRICS` is not followed for phantom-point advances: unhinted, the composite's own `hmtx` and phantom deltas apply (they match `HVAR`; RobotoFlex's `ç` shows the component's would not). The origin (pp1) does follow it, as FreeType does.
- Packed deltas follow OpenType 1.9.1: `0x80` is a zero run whatever `0x40` says (no 32-bit delta runs).
- `MVAR` feeds `GetFontMetrics` through `hcla`/`hcld` only (the values it reads); the `hhea` fallback is not varied.

## Work items — CFF2 `blend` (Wave 2)

- `vsindex` selects the ItemVariationData; `blend` pops `n × k` deltas + `n` defaults and pushes `n` blended values using the region scalars at the current instance.
- Private DICT `blend` for hint values can be ignored (hints are not applied).
- Needs a committed OFL CFF2 variable fixture (and a `FONT-LICENSES.md` entry).

## Testing

- `wght` 100/400/700/900 on RobotoFlex: stems thicken; outline matches FreeType control points within tolerance
- `wdth`: advances change (`HVAR`)
- `avar` remapping non-linear; intermediate regions; axis extremes and out-of-range clamping
- IUP correctness on a glyph with sparse deltas; composite glyph variation
- `MVAR`: `.fnt` `base`/`lineHeight` follow the instance, matching FreeType ±1px
- Default instance (no axes) stays byte-identical to today

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/variable-wght700.bmfc` with `fontFile` pointing at a committed variable font (RobotoFlex) and `variationAxes=wght:700`; force-add it. Assert in a test that Native's output differs from the default instance (the harness can't distinguish "varied" from "ignored").

## Success Criteria

- [x] Normalization + `avar` correct
- [x] `gvar` deltas (including IUP, phantom points, composites) applied
- [x] `HVAR`/`MVAR` applied to advances and font metrics
- [x] `SupportsVariableFonts = true`; default instance unchanged
- [ ] CFF2 `blend`/`vsindex` (Wave 2, with a CFF2 fixture)
- [x] All tests pass; `regression_check.py` run with the variable config
