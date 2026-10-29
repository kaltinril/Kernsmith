# Phase 168 — Native Rasterizer: Outline Stroke, Small Caps & Condensed

> **Status**: In progress — Native geometry done (internal, unwired); core plumbing awaits Jeremy's decision below
> **Created**: 2026-04-01
> **Depends on**: Phase 167 (synthetic styles), Phase 166 Part 1 (transform list)

## Goal

Outline-level stroke generation plus small caps and condensed/extended transforms in the Native rasterizer, and the core plumbing that lets the pipeline use a backend's stroke instead of its bitmap `OutlineEffect`.

## Done — Native internals (unit-tested, reachable from no option yet)

- **`post` table** — `Internal/Tables/PostTable.cs` parses the shared header (`italicAngle`, `underlinePosition`/`Thickness`, `isFixedPitch`). Not loaded by `NativeFontFace`: nothing consumes it until a font-derived angle is wired.
- **Italic angle** — `ItalicTransform.FromItalicAngle(degrees)` (post convention: negative leans right; ±60° max). `ItalicTransform.Instance` stays FreeType's 12°.
- **Stroke** — `Internal/Outlines/OutlineStroker.cs` + `Pipeline/StrokeTransform.cs`. Contours are flattened in font units (0.05px tolerance via `StrokeTransform.FromPixels`), then every edge emits a rectangle and every corner an outer-side join wedge (miter with limit, default 2 half-widths, else bevel; round; bevel), all wound clockwise so non-zero fill draws their union. Chosen over "offset both sides, reverse the inner one" because the union cannot invert or punch holes where an offset self-intersects (curves tighter than the half-width, cusps); covered by tests for both windings. Cost: overlapping pieces can darken an AA edge pixel slightly. `StrokeTransform.JoinFor(OutlineShape)` maps Round→Round, Square→Miter, Plus→Bevel. The stroke is its own outline; it never replaces the fill in the glyph's transform list.
- **Condensed / extended** — `WidthScaleTransform` (`x *= s`, `advance *= s`).
- **Small caps geometry** — `SmallCapsTransform` (uniform scale about the pen origin, advance included) and `SmallCapsTransform.ScaleFor(OS/2)` = `sxHeight / sCapHeight`, null when either is missing.
- **Order** — condensed → small caps → bold → italic → stroke composes correctly (`ShapeTransformTests`). `GlyphTransforms.For` registers only bold/italic until options exist for the rest.

Tests: `OutlineStrokerTests`, `ShapeTransformTests`, `PostTableTests`.

## Remaining (blocked on decisions)

1. Core stroke plumbing — design below.
2. `WidthScale`, `SmallCaps`, `SyntheticItalicAngle` options: whether they become user-facing (`RasterOptions` + `FontGeneratorOptions` + `.bmfc`/CLI keys), and they would be honored by Native only — other backends would ignore them unless implemented there too.
3. Small caps: which codepoints get small-capped. Proposed: with `SmallCaps` on, a lowercase codepoint renders its uppercase glyph through `SmallCapsTransform` (keeping the lowercase id in the `.fnt`); uppercase and everything else unchanged. Optional compensation: embolden by roughly `(1 − scale) × stem` — stem width is not known per font, so this needs a heuristic or is dropped.
4. `SupportsOutlineStroke = true` on Native — only once core actually uses it.

## Design proposal — backend stroke into core's outline layer

Facts that constrain it:

- Core's `OutlineEffect` is not a centred stroke: it is an outward ring of N px around the ink (exterior and counter walls) plus a solid backing under the body — i.e. the ink **dilated** by N, coloured, at Z-order 1. The Native equivalent is `fill ∪ stroke(width 2N)` with `JoinFor(OutlineShape)` (exact for Round; Square/Plus match their Chebyshev/Manhattan corners only at right angles).
- `ChannelCompositor` assumes the outline glyph expands the body **symmetrically** (`(outlineW − W) / 2`), so a backend layer must be padded to the body box ± N, or the compositor must take its offset from metrics as it already does for shadow.
- In `PreparedRasterization.RasterizeAndProcess` effects run after height-stretch and custom-glyph substitution, at the supersampled size, inside a `Parallel.For` — and rasterizers are not thread-safe. A backend layer must be produced in the serial rasterize step, stretched like the body, and skipped (falling back to `OutlineEffect`) for custom glyphs. With `AntiAlias = None`, `OutlineEffect` thresholds hard; the backend layer must match.
- FreeType reports `SupportsOutlineStroke = true` but core never reads it; honoring the flag as-is would switch FreeType onto its (unused, internal) `RasterizeOutline` and change its output.

Options:

- **A. Layer on `RasterizedGlyph`.** Add outline width/shape to `RasterOptions`; a capable backend returns an extra alpha layer with the glyph. One decode per glyph, but it changes the public `RasterizedGlyph` and `RasterOptions`, and the FreeType flag must be split or renamed to keep FreeType unchanged.
- **B. Separate opt-in call (recommended).** A default-interface method, e.g. `RasterizedGlyph? IRasterizer.RasterizeOutlineLayer(int codepoint, RasterOptions options, int width, OutlineShape shape) => null`. Core calls it in the serial rasterize step when an outline is requested; a non-null result becomes a `BackendOutlineEffect` (colour + symmetric padding) in place of `OutlineEffect` for that glyph, null keeps today's path. Every other backend inherits `null`, so their output is byte-identical by construction, independent of `SupportsOutlineStroke` (whose doc would then say it means "implements the layer method"). Costs a second decode/transform per glyph on Native — cheap next to rasterization.
- **C. Don't wire it.** Keep core's EDT outline for every backend; use the stroker only inside Native later (e.g. SDF/MSDF outline channels, Phases 169/170).

Recommendation: **B**, with Native's layer built by the existing pipeline (`[...styles, StrokeTransform]` filled together with the body), the harness configs `outline-*-nosmoothing.bmfc` and `fire.bmfc` reviewed for Native, and every other backend proven identical by `regression_check.py`.

## Testing still owed (once wired)

- Native stroke layer vs core `OutlineEffect` at the same width: similar extent (SSIM); colours and channel routing unchanged.
- **Regression coverage**: `outline-nosmoothing.bmfc`, `outline-plus-nosmoothing.bmfc`, `outline-square-nosmoothing.bmfc` (hard-edged) and `fire.bmfc` (smoothed, 4px) change for Native only. Small caps / condensed need a config only if they get `.bmfc` keys.

## Success Criteria

- [ ] Core honors a backend stroke and passes stroke width; other backends byte-identical
- [x] Native stroke renders a correct ring with miter/round/bevel joins, as its own outline (internal)
- [x] `post` table parsed
- [x] Small caps and condensed/extended transforms work (geometry; not yet reachable)
- [ ] `SupportsOutlineStroke = true` on Native
- [ ] All tests pass; `regression_check.py` run with the stroke wired
