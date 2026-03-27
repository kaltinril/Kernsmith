# Phase 78BB -- GDI BMFont Parity Fixes

> **Status**: Partial
> **Size**: Medium
> **Created**: 2026-03-25
> **Updated**: 2026-03-26 (session 2)
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
| **kerning** | ✅ exact on shared | ✅ exact on shared (was 218 diffs) | See kerning section |

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
- `GetKerningPairs` — returns `null` (delegates to shared GPOS parser). Originally called `GetKerningPairsW`, but the 64-bit API returns legacy kern table data that differs from BMFont's 32-bit behavior.
- Supersampling — renders at `size * aa` via supersized HFONT, downscales bitmap by averaging aa×aa blocks, divides metrics by aa
- TTC font collection support in `ParseFamilyName`

### Pipeline Changes

- `BmFont.cs` — calls `LoadSystemFont` when source is a system font name and backend supports it; skips cell-height-to-ppem when `HandlesOwnSizing` is true; captures rasterizer metrics/kerning before disposal
- `BmFontModelBuilder` — uses rasterizer-provided lineHeight/base when available; uses rasterizer-provided kerning pairs (already scaled) when available; applies padding to xoffset/yoffset/width/height per BMFont spec
- `BmfcConfigReader` — maps `aa` key to `SuperSampleLevel` (was incorrectly mapped to AntiAliasMode)
- Kerning rounding changed to `MidpointRounding.AwayFromZero` to match BMFont

### FreeType Impact: NONE

All changes use default interface methods or capability checks. FreeType code paths are completely unchanged. Verified: 330/330 FreeType tests pass.

## Fixes Applied This Session

### Bell MT kerning scaling -- FIXED

`GetKerningPairs` now returns `null`, delegating all kerning to the shared GPOS parser. The 64-bit `GetKerningPairsW` returns legacy kern table data that differs from BMFont's 32-bit behavior (BMFont is a 32-bit application). By falling back to GPOS parsing, Bell MT kerning amount diffs went from 218 to 0 (48pt) and 49 to 0 (16pt). All shared pairs now match exactly.

### GPOS Format 2 class 0 -- FIXED (partial)

`TtfParser.ParsePairPosFormat2` now populates class 0 with glyphs not explicitly in `classDef2`, per OpenType spec. This added missing kerning pairs for class-based GPOS lookups. However, Bahnschrift still shows 47-63 missing pairs from an unknown source in BMFont's pipeline.

### Attempted and Reverted

1. **8x internal supersample -- REVERTED**: Made xadvance, yoffset, and lineHeight significantly worse. BMFont's 8x is in its own outline renderer (`GGO_NATIVE` + polygon fill via `DrawGlyphFromOutline`), not reproducible via GDI's `GetGlyphOutlineW(GGO_GRAY8_BITMAP)`. The two rendering paths produce fundamentally different results.

2. **ABC widths for xoffset/xadvance -- REVERTED**: Made xoffset worse because BMFont applies bitmap trimming (`TrimLeftAndRight`) after `abcA`. `GmptGlyphOrigin.X` already approximates the post-trim value.

## Remaining Gaps and Root Causes

### 1. xoffset ±1 systematic -- architectural rendering path difference

**Root cause**: BMFont renders its own outlines at 8x via `DrawGlyphFromOutline` using `GGO_NATIVE` + polygon fill, producing different sub-pixel positions than `GetGlyphOutlineW(GGO_GRAY8_BITMAP)`. These are fundamentally different rendering paths in the Windows GDI stack. The 8x supersample approach was tested and reverted because it makes other metrics worse when applied to `GGO_GRAY8_BITMAP` output.

**Status**: Not fixable without implementing BMFont's own outline renderer (GGO_NATIVE polygon fill + 8x rasterization). This would be a major undertaking for diminishing returns (±1 pixel).

### 2. yoffset ±1-3 -- downstream of rendering path difference

Same root cause as xoffset. BMFont computes yoffset from its supersampled outline data, while KernSmith uses `gmptGlyphOrigin.Y` from the GDI rasterizer. The difference is inherent to the different rendering paths.

### 3. Bell MT lineHeight -1 at 16pt

`CreateFont` (positional parameters, used by BMFont) vs `CreateFontIndirectW` (LOGFONTW struct, used by KernSmith) edge case. Only affects one font at one size. Low priority.

### 4. Bell MT xadvance diffs

Same root cause as xoffset -- BMFont's outline renderer produces different advance widths than `GetGlyphOutlineW`. The avg +1.31 at 48pt is an artifact of the different rendering paths.

### 5. Bahnschrift 47-63 missing kerning pairs

GPOS class 0 fix reduced but didn't fully resolve this. The remaining missing pairs come from an unknown source in BMFont's kerning pipeline. Possible causes:
- BMFont may expand class pairs differently or have additional fallback logic
- BMFont's 32-bit GPOS parser may handle edge cases differently than our implementation
- There may be additional kerning sources (kern table fallback after GPOS) that BMFont merges

### 6. Atlas PNG channel configuration (separate bug)

KernSmith ignores `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` from .bmfc configs. Produces white-on-black instead of BMFont's white-on-alpha. Not a metrics issue -- tracked separately.

## Testing & Comparison

### Regenerating Output

Both BMFont and KernSmith GDI output live under `tests/bmfont-compare/`:

| Directory | Source | Contents |
|-----------|--------|----------|
| `gum-bmfont/` | BMFont64.exe | `.bmfc` configs + `.fnt` + `.png` (ground truth) |
| `gum-gdi/` | KernSmith GDI backend | `.fnt` + `.png` + copied `.bmfc` |

**Regenerate BMFont output** (requires `bmfont64.exe` on PATH or at `c:/tools/`):
```bash
cd tests/bmfont-compare
for f in gum-bmfont/*.bmfc; do
  name=$(basename "$f" .bmfc)
  bmfont64.exe -c "$f" -o "gum-bmfont/${name}.fnt"
done
```

**Regenerate KernSmith GDI output**:
```bash
dotnet run --project tests/bmfont-compare/GenerateGdi/
```

### Running the Comparison

**Compare all fonts** (BMFont vs KernSmith GDI):
```bash
python tests/bmfont-compare/diff_all_fonts.py
```

**Compare a single font pair**:
```bash
python tests/bmfont-compare/diff_fnt.py tests/bmfont-compare/gum-bmfont/Font18Arial.fnt tests/bmfont-compare/gum-gdi/Font18Arial.fnt
```

### Visual Comparison

PNG atlas textures are generated alongside `.fnt` files in both directories. Open the matching `*_0.png` files side-by-side to visually compare glyph rendering:
- `tests/bmfont-compare/gum-bmfont/Font18Arial_0.png` (BMFont)
- `tests/bmfont-compare/gum-gdi/Font18Arial_0.png` (KernSmith GDI)

## Next Steps (Priority Order)

1. ~~**Fixed 8x internal supersample**~~ -- ATTEMPTED AND REVERTED. Not viable with `GGO_GRAY8_BITMAP`.
2. ~~**Bell MT kerning scaling**~~ -- FIXED by delegating to GPOS parser instead of `GetKerningPairsW`.
3. **Bahnschrift missing pairs investigation** -- identify the source of 47-63 extra pairs in BMFont's output. May require deeper analysis of BMFont's 32-bit GPOS parser behavior.
4. **Atlas channel configuration** -- separate phase, affects all backends.
5. **Accept xoffset/yoffset ±1 as known limitation** -- document that exact parity requires BMFont's proprietary outline renderer (`GGO_NATIVE` + polygon fill), which is outside scope.

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
- ✅ Kerning: all shared pairs now match exactly (Bell MT 218 diffs resolved)
- ⚠️ xoffset ±1 systematic -- architectural limitation (different GDI rendering paths)
- ⚠️ Bahnschrift 47-63 missing kerning pairs -- unknown source in BMFont pipeline
