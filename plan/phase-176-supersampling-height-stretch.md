# Phase 176 — Native Rasterizer: Supersampling & Height Stretch

> **Status**: Deferred — awaiting Jeremy's later decision; core already provides both for Native (keep, narrow or drop: see Open question)
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (scale stage)

## Goal (as originally scoped)

Do supersampling and BMFont `heightPercent` stretch inside the Native rasterizer instead of in the pipeline.

## What already exists

- **Supersampling**: `PreparedRasterization.Prepare` multiplies `Size` by `SuperSampleLevel` for any backend with `HandlesOwnSizing = false` (Native included) and box-filters the result back down in the effects/downscale pass. `RasterOptions.SuperSample` is therefore ignored by Native, correctly. `Lcd` is the exception: core renders it at the final size (Phase 173).
- **Height stretch**: `RasterizeAndProcess` runs `HeightStretchPostProcessor` on every backend's output when `HeightPercent != 100`.
- The `supersample.bmfc` harness config (`aa=2`) already exercises Native's supersampled path.

## Open question (decide before any work)

What would an in-rasterizer version buy?

- **Height stretch as an outline transform** (scale Y in font units before rasterizing) is genuinely better than bitmap resampling — it stays sharp and composes with SDF/stroke. It fits naturally as one more Phase 166 transform, next to Phase 168's `WidthScaleTransform`.
- **Native supersampling** would be output-identical to the pipeline's box filter at best; the only gain is skipping a buffer copy, which Phase 177 can measure. Alternatives with a quality difference (analytic coverage is already exact; RGSS; stb-style 1D prefilter) are research, not parity.

Either path needs a new capability flag so core skips its own pass (`HandlesOwnSizing` is unrelated and must stay `false`): name, one flag or two, and default-interface placement on `IRasterizerCapabilities` are part of the decision.

Options, for Jeremy to choose:

1. Drop the phase; core already covers it.
2. Narrow it to outline-level height stretch (plus a `HandlesOwnHeightStretch`-style flag), built like Phase 168's `WidthPercent`.
3. Keep the full original scope.

## Testing (if pursued)

- Outline height stretch vs `HeightStretchPostProcessor`: same metrics, sharper edges
- Native supersample vs pipeline supersample: identical output (if option 3)

**Regression coverage**: `supersample.bmfc` already covers supersampling; height stretch needs a config with `scaleH` other than 100 (BMFont's height-percent key) only if option 2 or 3 is chosen — no tracked config sets one.
