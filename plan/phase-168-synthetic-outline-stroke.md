# Phase 168 — Native Rasterizer: Outline Stroke, Small Caps & Condensed

> **Status**: Future — Wave 1, after Phase 167
> **Created**: 2026-04-01
> **Depends on**: Phase 167 (outline offset code), Phase 166 Part 1 (transform list)

## Goal

Outline-level stroke generation plus small caps and condensed/extended transforms in the Native rasterizer, and the core plumbing that lets the pipeline use a backend's stroke instead of its bitmap `OutlineEffect`.

## Starting point

- **Core never consults `SupportsOutlineStroke`.** Outlines are always drawn by core's EDT-based `OutlineEffect` from `FontGeneratorOptions.Outline`, for every backend (FreeType reports `true` and is ignored too).
- **`RasterOptions` has no outline-width field**, so a backend can't see the requested stroke width.
- `WidthScale`, `SmallCaps` and `SyntheticItalicAngle` do not exist on `RasterOptions` or `FontGeneratorOptions`.
- Native has no `post` table parser; `FontValidator` only checks `post` is present.

## Work items

### Core plumbing (do first — nothing else in this phase is reachable without it)

1. Add the stroke request to `RasterOptions` (width in pixels, plus whatever shape/join the existing `OutlineShape` maps to), copied in `RasterOptions.FromGeneratorOptions`.
2. Decide how a backend-drawn stroke is returned. `OutlineEffect` produces a separate outline layer that `GlyphCompositor` colors and composites under the body, and channel config can route outline and glyph to different channels — a merged single bitmap would break both. The likely shape is an extra layer on `RasterizedGlyph` (or a second rasterize call); settle it with the compositor and `ChannelCompositor` code in view.
3. In `PreparedRasterization`, when `SupportsOutlineStroke` is true and an outline is requested, use the backend's stroke layer and skip building `OutlineEffect`; otherwise unchanged. Other backends' output must stay identical (`regression_check.py`).
4. Add `WidthScale` (default 1.0), `SmallCaps` (default false) and `SyntheticItalicAngle` (default 12°) as optional `RasterOptions` properties beside `Bold`/`Italic`; expose matching `FontGeneratorOptions` / `.bmfc` extension keys only if Jeremy wants them user-facing.

### `post` table parser (Native)

- Parse `post` header fields (`italicAngle`, `underlinePosition`/`Thickness`, `isFixedPitch`). `italicAngle` is the optional source for a font-derived `SyntheticItalicAngle`; the 12° default stays the parity value used by Phase 167.

### Outline stroke

- Offset each contour outward and inward by `strokeWidth / 2` (reusing Phase 167's offset code), reverse the inner offset, and emit both as a stroke outline whose non-zero fill is the ring.
- Joins: miter (default, limit 2× stroke width), round (arc approximated by segments), bevel. Map `OutlineShape` onto these where it has a meaning.
- Stroke width converts to font units like bold strength: `width_fu = width_px * unitsPerEm / ppem`.
- The stroke is a separate layer from the fill, never merged into it.

### Small caps

- Scale uppercase outlines by `OS/2 sxHeight / sCapHeight` (Native already parses OS/2); optional light emboldening to compensate thinner stems. Which codepoints get small-capped (lowercase input mapped to scaled capitals?) must be specified before implementing — it changes which glyph is drawn for a codepoint.

### Condensed / extended

- `x *= widthScale` on all points, `advance *= widthScale`.

### Transform order

Condensed/extended → small caps → bold → italic → stroke (stroke generated from the fully styled outline), then scale to pixels.

## Testing

- Stroke `'O'`: ring of the requested width; sharp-corner glyphs (`'A'`, `'M'`): miter limit holds
- Native stroke vs core `OutlineEffect` at the same width: similar extent (SSIM), and colors/channel routing unchanged
- Small caps ratio matches OS/2; condensed/extended scales X only; combined transforms
- `post` parser against Roboto and a CFF fixture

**Regression coverage**: the existing `outline-nosmoothing.bmfc`, `outline-plus-nosmoothing.bmfc`, `outline-square-nosmoothing.bmfc` (hard-edged) and `fire.bmfc` (smoothed, 4px) configs switch Native onto its own stroke once the capability flips — review those Native diffs deliberately; every other backend must stay identical. Small caps / condensed need a config only if they get `.bmfc` keys.

## Success Criteria

- [ ] Core honors `SupportsOutlineStroke` and passes stroke width; other backends byte-identical
- [ ] Native stroke renders a correct ring with miter/round/bevel joins, as its own layer
- [ ] `post` table parsed
- [ ] Small caps and condensed/extended transforms work
- [ ] `SupportsOutlineStroke = true` on Native
- [ ] All tests pass; `regression_check.py` run
