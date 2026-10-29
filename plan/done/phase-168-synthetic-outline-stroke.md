# Phase 168 — Native Rasterizer: Vector Outlines, Synthetic Width & Italic Angle

> **Status**: Complete — vector outline layer, `WidthPercent` and `ItalicAngle` shipped opt-in (Native only); small caps deferred
> **Created**: 2026-04-01
> **Depends on**: Phase 167 (synthetic styles), Phase 166 Part 1 (transform list)

## Goal

Let a backend draw the glyph outline (border) from the vector outline instead of core's bitmap `OutlineEffect`, and expose synthetic condensed/expanded width and italic angle — all opt-in, so default output of every backend, Native included, is unchanged.

## As built

### Public surface

| Option | `FontGeneratorOptions` / `RasterOptions` | `.bmfc` | CLI | Builder | Capability |
|---|---|---|---|---|---|
| Vector outline | `OutlineMode { Effect (default), Vector }` | `outlineMode=vector` | `--outline-mode vector` | `WithOutlineMode` | `SupportsOutlineStroke` |
| Width | `WidthPercent` (int, 100, 10–1000) | `widthPercent` | `--width-percent` | `WithWidthPercent` | `SupportsWidthScale` (default false) |
| Slant | `ItalicAngle` (float °, 12, ±60, positive leans right) | `italicAngle` | `--italic-angle` | `WithItalicAngle` | `SupportsItalicAngle` (default false) |

`IRasterizer.RasterizeOutlineLayer(codepoint, options, width, shape, heightScale = 1) => null` (default-interface method, design option B). `SupportsOutlineStroke` now means "implements it": Native true; FreeType false (it never implemented it — output unchanged, core never read the flag). Writers emit the keys only when non-default.

### Core (`PreparedRasterization`)

- Layers are requested in the serial step right after `RasterizeAll` (rasterizers are not thread-safe; the effects pass is `Parallel.For`), at the render (supersampled) options, for every non-blank glyph. A null layer, a custom glyph, a backend without the capability, or `Outline == 0` keeps `OutlineEffect` for that glyph.
- `OutlineMask` holds the layer relative to its body's top-left and follows the body through height stretch (resampled with the body's own row mapping; aliased layers re-thresholded), compositing shifts and, for routed outlines, the box-filter downscale.
- `VectorOutlineEffect` replaces `OutlineEffect` (Z 1, outline color, solid backing under every body pixel), so shadow (cast from outline + body), gradient, fill color and supersampling compose exactly as before. Routed outlines (`Channels` with Outline content — every BMFont-style `alphaChnl=1` config) use `VectorOutlineEffect.ChannelGlyph` in place of `OutlinePostProcessor`.
- Width semantics match each path's effect: `Outline` px at the render size for the composited effect, `Outline` × supersample at render size (= `Outline` px at output) for routed outlines. `heightScale = HeightPercent / 100` makes the pen `width / heightScale` tall so the ring is even after the stretch.
- INVARIANT: the canvas is always the effect's (body ± `Outline`); the layer is clipped to it, so switching modes never changes a cell, bearing or `.fnt` offset (measured: 94/94 identical cells in every configuration below).
- Rejected: `Vector` + SDF (`InvalidOperationException`; MSDF already rejects effects); `WidthPercent`/`ItalicAngle` out of range (`ArgumentOutOfRangeException`); a non-default width, or a non-default angle with synthetic italic in effect, on a backend without the capability (`NotSupportedException` — no silent wrong output). Kerning from the GPOS/kern parser scales by `WidthPercent` (exactly ×1 at 100).

### Native

- `OutlineLayer` dilates the prepared glyph (after transforms and the hint stage) by the outline width: distance from each pixel to the filled region (`DistanceShape.Boundary`, overlapping contours counted once) in the pen's metric — round, square (Chebyshev) or diamond (Manhattan, `Plus`), the effect's own metrics. Pixels within half a pixel-diagonal of the pen's edge average 4×4 sub-samples; aliased requests threshold pixel centres. LCD requests get grayscale coverage.
- Not the stroker union: filling `OutlineStroker` pieces over-darkens anti-aliased edge pixels where pieces overlap (inner side of every curve) by up to 166/255 against an 8× render; one tangent-line coverage per pixel was up to 100/255 light at concave corners; miter joins poked tips past the width at acute corners (an isolated pixel on `W` in aliased output). The shipped layer is within 7/255 (round) and 13/255 (square/plus) of an 8× render (`Layer_Coverage_MatchesAnEightTimesRender`). `OutlineStroker`/`StrokeTransform` remain internal and unregistered.
- Declines (returns null → effect) for distance fields, blank glyphs and any request with `ColorFont` on a face with color glyphs (a color glyph's silhouette is its layers'; declining the whole face keeps one look per atlas).
- `GlyphTransforms.For`: `WidthScaleTransform` → bold → `ItalicTransform.ForAngle(ItalicAngle)`; exactly 12 is FreeType's fixed-point slant, so default italic output is unchanged. Applies to color layers and clips too.

### Vector vs Effect (Native, Roboto 32px, printable ASCII, composited alpha)

| Config | Mean SSIM | Max ink-bounds Δ | Cells identical |
|---|---|---|---|
| Grayscale 1 / 2 / 3 / 4 px | 0.899 / 0.897 / 0.860 / 0.888 | 1 px | 94/94 |
| None 1 / 2 / 3 / 4 px | 0.780 / 0.726 / 0.711 / 0.673 | 1 px | 94/94 |
| Grayscale 2px square / plus | 0.814 / 0.903 | 1 px | 94/94 |
| None 2px square | 0.952 | 1 px | 94/94 |
| Grayscale 2px + shadow / ss2 / height 150% | 0.889 / 0.959 / 0.896 | 1 px | 94/94 |

SSIM measures difference, not quality. Where they differ, by eye: the vector ring's edge has the body's exact-area anti-aliasing where the effect fades over 1.5 px; narrow counters (`@`, `s`, `a`) fill solidly where the effect leaves isolated holes; aliased rings step evenly on curves. The vector ring reaches exactly N px from the true outline, while the effect's 50% point sits N − 0.125 px out on average and N − 0.5 px on pixel-aligned edges, so vector rings are slightly heavier — most visible at 1 px, where the effect's ring on aligned stems (`|`, `_`) is half-transparent and the vector's is solid, and aliased 1 px rings around tiny glyphs (`.`, `'`) get square corners where the effect drops the corner pixel.

Cost (Native, layers drawn serially): ~0.2 ms/glyph at 32px w2, ~0.37 ms at 96px w4; an ASCII font takes ~43 ms vs ~16 ms for Effect at 32px, ~81 ms vs ~12 ms at 96px w4.

## Deferred

- **Small caps** — `SmallCapsTransform` and `ScaleFor(OS/2)` exist internally; no option. Open: which codepoints map (proposed: lowercase renders its uppercase glyph scaled, keeping the lowercase id) and whether to compensate stem weight.
- **Font-derived italic angle** — `PostTable` parses `post.italicAngle`; nothing loads it (the option is explicit degrees).
- **Vector outlines for color glyphs** and other backends (FreeType's internal `RasterizeOutline` could implement the layer later).

## Tests and harness

`VectorOutlineTests`, `SyntheticWidthAndAngleTests`, `OutlineModeAndShapeConfigTests`, CLI tests (core); `NativeVectorOutlineTests`, `NativeWidthAndAngleTests`, goldens `generate/*outline-vector*` and `generate/roboto/width75-italic20-28` (Native); `OutlineStrokerTests`, `ShapeTransformTests`, `PostTableTests` (internals). Harness configs: `outline-vector.bmfc` (routed), `outline-vector-shadow.bmfc` (composited, shadow + gradient), `width-condensed.bmfc`, `italic-angle.bmfc`; `GenerateAll` skips backends without width/angle support.
