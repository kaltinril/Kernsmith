# Phase 170 — Native Rasterizer: Multi-Channel SDF (MSDF / MTSDF)

> **Status**: Future — Wave 1, after Phase 169
> **Created**: 2026-04-01
> **Depends on**: Phase 169 (single-channel SDF and its distance code)

## Goal

Multi-channel signed distance fields (msdfgen-style), which keep sharp corners that single-channel SDF rounds off, plus the MTSDF variant with a true SDF in alpha.

## Background

Edge segments are assigned to R, G or B. Each channel stores the signed distance to the nearest edge of its color; at render time `median(R, G, B)` reconstructs the distance while corners stay sharp.

## Work items

### Edge coloring

- msdfgen's `edgeColoringSimple`: split each contour into edges at corners (angle above a threshold, e.g. 3°, or a reversal); adjacent edges at a corner get different colors; smooth runs share a color. Single-edge contours get split so they carry at least two colors.
- Corner test: `|cross(t1, t2)| > threshold || dot(t1, t2) <= 0`.

### Per-channel distance

- Per texel, signed distance to the nearest edge of each color → R, G, B, with the same encoding, `SdfSpread` and padding as Phase 169 (inside bright).
- Error correction pass (msdfgen's clash detection) to remove artifacts where channels disagree.

### Output and configuration (core API — public surface, additive only)

- New `SdfMode { None, Sdf, Msdf, Mtsdf }` in the `KernSmith` root namespace.
- `RasterOptions` / `FontGeneratorOptions` gain `SdfMode`; the shipped `bool Sdf` stays and keeps meaning `SdfMode.Sdf` (resolve the effective mode as `Sdf ? SdfMode.Sdf : SdfMode` when `SdfMode` is `None`). Soft-deprecate `Sdf` in docs only.
- `IRasterizerCapabilities` gains `bool SupportsMsdf => false` (default interface member, like `SupportsSyntheticBold`), so existing backends compile unchanged.
- `PreparedRasterization` rejects `Msdf`/`Mtsdf` on backends without `SupportsMsdf`, mirroring the SDF gate.
- MSDF: `PixelFormat.Rgba32`, A = 255. MTSDF: `Rgba32`, A = the Phase 169 single-channel SDF.
- MSDF output is RGB data, not a color glyph: channel packing, effects and the extended `.fnt` metadata must treat it accordingly (check `ChannelPacking` guards and SDF metadata writers).
- `.bmfc`/CLI: add an extension key (e.g. `sdfMode=msdf|mtsdf`) and CLI flag, written only when non-default; update `GenerateAll`'s `ConfigRequestsSdf` skip logic so backends without MSDF are skipped, not failed.

### Native

- MSDF/MTSDF branches in the render-mode dispatch; `SupportsMsdf = true`.

## Testing

- `'A'`: apex and baseline corners sharp after median + threshold
- Median of MSDF equals single-channel SDF on smooth regions; differs only near corners
- Edge coloring: no two edges meeting at a corner share a color
- MTSDF alpha equals Phase 169 output
- Round trip: median → threshold vs coverage bitmap
- `bool Sdf` alone still produces single-channel SDF (backward compatibility)

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/msdf.bmfc` (same font/size as `sdf.bmfc`, `sdfMode=msdf`) and force-add it. Only Native will produce output; add a test that asserts it did.

## Success Criteria

- [ ] Edge coloring correct at corners; error correction applied
- [ ] MSDF median matches SDF on smooth regions; corners preserved
- [ ] MTSDF alpha matches single-channel SDF
- [ ] `SdfMode` + `SupportsMsdf` added without breaking `bool Sdf` or other backends
- [ ] `.bmfc`/CLI surface and `msdf.bmfc` config added
- [ ] All tests pass; `regression_check.py` run
