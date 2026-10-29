# Phase 170 — Native Rasterizer: Multi-Channel SDF (MSDF / MTSDF)

> **Status**: Complete
> **Created**: 2026-04-01
> **Depends on**: [Phase 169](phase-169-sdf-generation.md) (single-channel SDF and its distance code)

## Goal

Multi-channel signed distance fields (msdfgen-style), which keep sharp corners that single-channel SDF rounds off, plus the MTSDF variant with a true SDF in alpha.

## Core API (public, additive)

- `SdfMode { None, Sdf, Msdf, Mtsdf }` (root namespace). `FontGeneratorOptions.SdfMode` / `RasterOptions.SdfMode`, plus computed `EffectiveSdfMode` (`SdfMode` when set, else `Sdf` → `SdfMode.Sdf`). `bool Sdf` stays and keeps its meaning.
- `RasterOptions.FromGeneratorOptions` sets `RasterOptions.Sdf` only when the effective mode is single-channel, so SDF-only backends never see MSDF as SDF.
- `IRasterizerCapabilities.SupportsMsdf => false` (default member); `PreparedRasterization` throws `NotSupportedException` for `Msdf`/`Mtsdf` without it, and `InvalidOperationException` for MSDF combined with effects, a fill color, channel packing, a custom channel layout, atlas variants or `ColorFont`. Native also rejects color + MSDF on a face with color glyphs; single-channel SDF + color keeps COLR glyphs in color (FreeType's behaviour), pinned by `NativeColorDistanceFieldTests`. The SDF + `SuperSampleLevel` guard and `SdfScale` apply to every distance-field mode; `SdfScale` downsampling averages MSDF/MTSDF channels independently (MTSDF alpha is a distance).
- Output: `Rgba32` glyphs → RGBA atlas. `.fnt`: MSDF writes `alphaChnl=4` (alpha = one); MTSDF keeps 0. The `kernsmith` line carries `sdfSpread` and `sdfMode=msdf|mtsdf` (`ExtendedMetadata.SdfMode`, all three formats read and write it); single-channel SDF output is unchanged.
- `.bmfc` extension key `sdfMode=none|sdf|msdf|mtsdf` (written only when not `None`); `BmFontBuilder.WithSdfMode`; `FromConfig` carries it; CLI `--sdf-mode`. `GenerateAll` skips `sdfMode=msdf|mtsdf` configs on backends without `SupportsMsdf`. Hiero output writes its distance-field effect only for single-channel SDF.

## Native

- `MsdfRenderer.Msdf` / `.Mtsdf` from `GlyphRenderers.Select` (switch on `EffectiveSdfMode`); `SupportsMsdf = true`. Same spread, encoding, padding and metrics as `SdfRenderer`.
- `EdgeColoring` (`Internal/Distance/`): corners where tangents reverse or turn past sin = 0.141 (~8°); smooth contours white; splines between corners rotate cyan/magenta/yellow, the last spline taking the third color when rotation would repeat the first; single-corner (teardrop) contours are split to ≥ 3 edges and colored in thirds. Tangents come from control points, not the polyline.
- `MultiChannelDistanceField`: per channel, nearest edge by true distance (ties → the more perpendicular edge), stored as a signed pseudo-distance (beyond an edge's own endpoints, distance to its tangent line). Signs from orientation, then all channels negated where the median disagrees with the non-zero inside test. Measures to the same clipped boundary as Phase 169, so overlaps leave no seam. Only edges within the spread are visited; a channel with none takes ±spread by the inside test.
- Clash correction: a texel whose most-changed channel jumps against a neighbour by more than 1.001 px of distance (√2× diagonally), and which is the farther from the edge in the least-changed channel, is flattened to its median (median and texel-centre edge unchanged).
- MTSDF alpha is `SignedDistanceField` on the same shape: byte-identical to Phase 169 output.
- Cost (Roboto ASCII, throwaway Stopwatch): MSDF 3-6x Native SDF — at 64px/spread 8, 151 ms MSDF vs 42 ms SDF vs 320 ms FreeType SDF.

## Testing

`MsdfRendererTests`: dispatch; capability; corners join different two-channel colors (square, every Roboto ASCII glyph, teardrop split); smooth contours white; square with off-grid corners reconstructed at 8x by bilinear sampling — MSDF 0 misclassified samples vs single-channel SDF 48; circle → all channels equal the SDF bytes; overlapping bars → median sign equals SDF sign; no interpolation artifacts on Roboto ASCII at 16/32/48px after clash correction (artifacts appear without it); RGBA format/pitch, metrics equal SDF metrics and `Measure`; MSDF alpha 255, MTSDF alpha equals SDF; median threshold reproduces coverage; end-to-end generation with RGBA atlas and `.fnt` flags. Core `SdfModeTests`: mode resolution, legacy flag, capability defaults and rejection, guards, RGBA/`alphaChnl`/`sdfMode` output, MTSDF per-channel downsample, metadata round trip in text/XML/binary, `.bmfc` key, builder. CLI tests for `--sdf-mode`.

**Regression coverage**: `tests/bmfont-compare/gum-bmfont/msdf.bmfc` (Arial 48, spread 6, `sdfScale=2`, `sdfMode=msdf`, plain channel layout) — only Native renders it.

## Success Criteria

- [x] Edge coloring correct at corners; error correction applied
- [x] MSDF median matches SDF on smooth regions; corners preserved
- [x] MTSDF alpha matches single-channel SDF
- [x] `SdfMode` + `SupportsMsdf` added without breaking `bool Sdf` or other backends
- [x] `.bmfc`/CLI surface and `msdf.bmfc` config added
- [x] All tests pass; `regression_check.py` run
