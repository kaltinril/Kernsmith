# Phase 78BB -- GDI BMFont Parity Fixes

> **Status**: Partial
> **Size**: Medium
> **Created**: 2026-03-25
> **Updated**: 2026-03-26
> **Dependencies**: Phase 78B (GDI backend must exist)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Close the metrics gap between KernSmith's GDI backend and BMFont's GDI output, achieving exact (or near-exact) parity on lineHeight, base, xadvance, xoffset, yoffset, and kerning pairs.

---

## Current Parity Results (same-machine validation, 15 Gum UI fonts)

| Metric | 12 standard fonts | Bell MT (2 configs) | Summary |
|--------|-------------------|---------------------|---------|
| **lineHeight** | ✅ exact | -1 at 16pt, exact at 48pt | 14/15 exact |
| **base** | ✅ exact | ✅ exact | 15/15 exact |
| **xadvance** | ✅ exact | avg +1.31, max 6 | 14/15 exact |
| **xoffset** | ±1 avg, max ±2 | avg -0.83, max 12 | Systematic ±1 |
| **yoffset** | ±1-3 some chars | avg -0.58, max 12 | Rendering path diff |
| **kerning** | ✅ exact on shared | 218 amount diffs | See kerning section |

## What Was Implemented

### IRasterizer Interface Extensions (all backends)

- `HandlesOwnSizing` capability flag (`=> false` default) — backends that handle font sizing internally set this to true so BmFont.cs skips the cell-height-to-ppem conversion
- `SupportsSystemFonts` capability flag (`=> false` default) — backends that can load system fonts by name set this to true
- `LoadSystemFont(string familyName)` — loads a system font by name instead of from bytes; default throws NotSupportedException
- `GetFontMetrics(RasterOptions)` — returns rasterizer-provided lineHeight/base/ascent; default returns null (falls back to OS/2 table calculation)
- `GetKerningPairs(RasterOptions)` — returns rasterizer-provided pre-scaled kerning pairs; default returns null (falls back to GPOS/kern parser)
- `RasterizerFontMetrics` record type — Ascent, Descent, LineHeight
- `ScaledKerningPair` record struct — distinct from `KerningPair` (design units) to prevent double-scaling
- `SuperSample` property on `RasterOptions` — supersampling factor, any backend can use it

### GDI Backend Implementation

- `LoadSystemFont` — stores family name directly, lets GDI font mapper resolve (matching BMFont's `CreateFont` by-name approach)
- `GetFontMetrics` — calls `GetTextMetricsW`, returns tmHeight/tmAscent/tmDescent, with `ceil(value/aa)` when supersampling
- `GetKerningPairs` — calls `GetKerningPairsW`, returns null when 0 pairs (falls back to GPOS parser), divides amounts by aa when supersampling
- Supersampling — renders at `size * aa` via supersized HFONT, downscales bitmap by averaging aa×aa blocks, divides metrics by aa
- TTC font collection support in `ParseFamilyName`

### Pipeline Changes

- `BmFont.cs` — calls `LoadSystemFont` when source is a system font name and backend supports it; skips cell-height-to-ppem when `HandlesOwnSizing` is true; captures rasterizer metrics/kerning before disposal
- `BmFontModelBuilder` — uses rasterizer-provided lineHeight/base when available; uses rasterizer-provided kerning pairs (already scaled) when available; applies padding to xoffset/yoffset/width/height per BMFont spec
- `BmfcConfigReader` — maps `aa` key to `SuperSampleLevel` (was incorrectly mapped to AntiAliasMode)
- Kerning rounding changed to `MidpointRounding.AwayFromZero` to match BMFont

### FreeType Impact: NONE

All changes use default interface methods or capability checks. FreeType code paths are completely unchanged. Verified: 330/330 FreeType tests pass.

## Remaining Gaps and Root Causes

### 1. xoffset ±1 systematic — BMFont's fixed 8x internal supersample

**Root cause**: BMFont's outline rendering path (`DrawGlyphFromOutline`) has a FIXED 8x internal supersample that is always active, independent of the user-facing `aa` setting. BMFont renders glyph outlines at 8x resolution, computes bounding boxes from the supersampled data, then downscales. KernSmith's GDI backend uses `GetGlyphOutlineW(GGO_GRAY8_BITMAP)` which rasterizes at native resolution.

At `aa=1`: BMFont renders at 8x internally, KernSmith renders at 1x. The glyph origins (`gmptGlyphOrigin`) differ by ±1 pixel because the 8x supersampled bounding box rounds differently than the native-resolution bounding box.

**Evidence**: Testing with `aa=2` showed xoffset improving significantly (121/191 diffs → 36/191 diffs for Arial 18pt), confirming that higher resolution rendering aligns the glyph origins more closely.

**Fix**: Add a fixed 8x internal supersample factor to the GDI backend. In `RasterizeGlyphCore`, use `aa * 8` as the internal rendering factor instead of just `aa`. This means at `aa=1`, the GDI backend renders at 8x and downscales, matching BMFont's outline path. This is GDI-only — FreeType untouched.

**Trade-off**: 8x supersampling means each glyph is rendered 64x larger (8x width × 8x height) before downscaling. This increases memory and CPU usage during generation but produces higher quality output and BMFont-matching metrics.

### 2. yoffset ±1-3 — downstream of xoffset + bearingY rounding

Partially caused by the same 8x supersample gap. BMFont computes `yoffset = fontAscent - (maxY / 65536)` from its supersampled outline data, while KernSmith uses `baseLine - gmptGlyphOrigin.Y`. The 8x fix should largely resolve this.

### 3. Bell MT lineHeight -1 at 16pt

Likely a font-specific LOGFONT interaction. BMFont uses `::CreateFont` (positional parameters) while KernSmith uses `CreateFontIndirectW` (LOGFONTW struct). For most fonts these are equivalent, but edge cases may exist. Low priority — only affects one font at one size.

### 4. Bell MT xadvance and kerning differences

**xadvance**: avg +1.31 at 48pt. May be related to the 8x supersample gap — at 8x, the advance width rounding would align with BMFont.

**Kerning amounts**: 218 shared pairs have different amounts. Root causes:
- BMFont uses `otmrcFontBox`-based scaling for GPOS kerning; KernSmith uses `ppem/unitsPerEm`. The formulas produce slightly different scale factors for fonts where `head.yMax - head.yMin != unitsPerEm`.
- BMFont tries `GetKerningPairsW` first (pre-scaled pixel values from OS), then falls back to GPOS. Bell MT may have kern table pairs that GDI returns with different values than GPOS parsing produces.
- The rounding fix (AwayFromZero) was applied but didn't change Bell MT results, suggesting the scaling factor difference is the primary cause.

### 5. Missing kerning pairs (Bahnschrift)

BMFont has 47-63 extra pairs beyond what KernSmith's GPOS parser finds. BMFont's GPOS parser may handle class-based pairs (Format 2) more expansively, or may expand classes into individual pairs that KernSmith's parser represents differently.

### 6. Atlas PNG channel configuration (separate bug)

KernSmith ignores `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` from .bmfc configs. Produces white-on-black instead of BMFont's white-on-alpha. Not a metrics issue — tracked separately.

## Next Steps (Priority Order)

1. **Fixed 8x internal supersample** in GDI backend — addresses xoffset ±1, yoffset, and likely Bell MT xadvance. GDI-only change, FreeType untouched.
2. **Bell MT kerning scaling** — switch from `ppem/unitsPerEm` to `otmrcFontBox`-based scaling to match BMFont's GPOS path. Shared change but only affects kerning math.
3. **Atlas channel configuration** — separate phase, affects all backends.

## Files Created/Changed

| File | Change |
|------|--------|
| `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs` | Add `HandlesOwnSizing`, `SupportsSystemFonts` |
| `src/KernSmith/Rasterizer/IRasterizer.cs` | Add `GetFontMetrics`, `GetKerningPairs`, `LoadSystemFont` |
| `src/KernSmith/Rasterizer/RasterOptions.cs` | Add `SuperSample` property |
| `src/KernSmith/Rasterizer/RasterizerFontMetrics.cs` | New — font metrics record |
| `src/KernSmith/Font/Models/ScaledKerningPair.cs` | New — pre-scaled kerning pair type |
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Add `SuperSampleLevel` property |
| `src/KernSmith/Config/BmfcConfigReader.cs` | Map `aa` key to `SuperSampleLevel` |
| `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` | Add `HandlesOwnSizing = false` to capabilities |
| `src/KernSmith/RasterizationResult.cs` | Add captured rasterizer metrics/kerning properties |
| `src/KernSmith/BmFont.cs` | LoadSystemFont path, HandlesOwnSizing bypass, capture before disposal |
| `src/KernSmith/Output/BmFontModelBuilder.cs` | Rasterizer metrics/kerning, padding, AwayFromZero rounding |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` | LoadSystemFont, GetFontMetrics, GetKerningPairs, supersampling, TTC support |
| `src/KernSmith.Rasterizers.Gdi/NativeMethods.cs` | GetKerningPairsW, KERNINGPAIR, precision constants |
| `src/KernSmith.Rasterizers.Gdi/GdiRegistration.cs` | Module initializer (from 78B) |
| `src/KernSmith/Rasterizer/RasterizerFactory.cs` | ResetForTesting (from 78B) |

## Testing

- ✅ 330/330 FreeType tests pass (zero behavioral change)
- ✅ 344/344 GDI tests pass on Windows TFMs
- ✅ 14/15 lineHeight exact, 15/15 base exact, 14/15 xadvance exact (same-machine validation)
- ⚠️ xoffset ±1 systematic — fixable with 8x internal supersample
- ⚠️ Bell MT minor divergence — kerning scaling difference
