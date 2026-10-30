# Phase 171 — Native Rasterizer: Variable Font Support

> **Status**: Complete — Wave 1 (TrueType: `avar` v1, `gvar`, `HVAR`, `MVAR`) and Wave 2 (CFF2 `blend`/`vsindex`)
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (decoder stage) for TrueType; Phase 166 Part 2 (CFF2 structure) for CFF2

## Goal

Render variable fonts at user-chosen axis values in the Native rasterizer: glyph outlines, advances and font metrics all follow the instance.

## Starting point

- Core already parses `fvar`: `PreparedRasterization.Prepare` (and the metrics-only path in `BmFont.cs`) call `IRasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes)` when the capability is true. Native does **not** need its own `fvar` axis parser — it receives the axis list. Named instances need no `fvar` parser here either: the CLI's `--instance` resolves one to axis values before generation.
- FreeType (`FreeTypeRasterizer.SetVariationAxes`) is the quality baseline.
- Fixtures: `tests/KernSmith.Tests/Fixtures/RobotoFlex-Variable.ttf` has `fvar`, `avar`, `gvar`, `HVAR` and `MVAR`; `SourceCodeVF-Upright.otf` (Phase 166) is the CFF2 one (`wght` 200..900, default 200).

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
- `USE_MY_METRICS` hands a composite the component's phantom points, origin and advance alike, as FreeType does: the advance is the component's `hmtx` plus its `HVAR` (or phantom-point) delta. Roboto Flex's composites vary their own advances differently (`ç` at wdth 25 wght 700: 1003 vs the component's 979); FreeType reports the component's.
- Packed deltas follow OpenType 1.9.1: `0x80` is a zero run whatever `0x40` says (no 32-bit delta runs).
- `MVAR` feeds `GetFontMetrics` through `hcla`/`hcld` only (the values it reads); the `hhea` fallback is not varied.

## Work items — CFF2 `blend` (Wave 2)

- `vsindex` selects the ItemVariationData; `blend` pops `n × k` deltas + `n` defaults and pushes `n` blended values using the region scalars at the current instance.
- Private DICT `blend` for hint values can be ignored (hints are not applied).

### As built (Wave 2)

- `CffOutlineDecoder` parses the CFF2 VariationStore with the shared `ItemVariationStore` on the first non-default decode (a damaged store cannot stop the default instance loading) and recomputes per-`vsindex` scalars — each ItemVariationData's `regionIndexes` order — whenever the coordinates change; `CharstringContext.BlendScalars` hands them to the interpreter. At the default instance it returns null and `blend` keeps its defaults, so default output is byte-identical.
- Advances add the `HVAR` delta (CFF2 has no phantom points; without `HVAR` the `hmtx` advance stands). `MVAR` already applies through `GetFontMetrics`.
- Private DICT `blend` keeps the default values at every instance (only hint values are blendable).
- Regression config: `otf-cff2-wght900.bmfc` (Source Code VF at `wght:900`).

## Testing

- `wght` 100/400/700/900 on RobotoFlex: stems thicken; outline matches FreeType control points within tolerance
- `wdth`: advances change (`HVAR`)
- `avar` remapping non-linear; intermediate regions; axis extremes and out-of-range clamping
- IUP correctness on a glyph with sparse deltas; composite glyph variation
- `MVAR`: `.fnt` `base`/`lineHeight` follow the instance, matching FreeType ±1px
- Default instance (no axes) stays byte-identical to today
- CFF2: `blend` with hand-built scalars (n = 1/2, several regions, `vsindex` from the Private DICT and the charstring); the spec's example table and a two-ItemVariationData table at several instances; Source Code VF at `wght` 200/400/650/900 against FreeType (advance and ink ±1px, SSIM ≥ 0.95); a synthetic `HVAR`; bold/italic/SDF/LCD on a varied glyph

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/variable-wght700.bmfc` with `fontFile` pointing at a committed variable font (RobotoFlex) and `variationAxes=wght:700`; force-add it. Assert in a test that Native's output differs from the default instance (the harness can't distinguish "varied" from "ignored").

## Success Criteria

- [x] Normalization + `avar` correct
- [x] `gvar` deltas (including IUP, phantom points, composites) applied
- [x] `HVAR`/`MVAR` applied to advances and font metrics
- [x] `SupportsVariableFonts = true`; default instance unchanged
- [x] CFF2 `blend`/`vsindex` (Wave 2, with a CFF2 fixture)
- [x] All tests pass; `regression_check.py` run with the variable config
