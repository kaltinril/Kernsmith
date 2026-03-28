# Phase 99 -- BMFont Parity Remaining Gaps

> **Status**: Planning
> **Size**: Medium
> **Created**: 2026-03-27
> **Origin**: Remaining gaps from Phase 78BB (GDI parity)
> **Goal**: Investigate and close remaining metrics differences between KernSmith's GDI backend and BMFont's output.

## Background

Phase 78BB achieved near-exact BMFont parity: 14/15 lineHeight exact, 15/15 base exact, 14/15 xadvance exact, kerning amounts exact on all shared pairs. The remaining gaps documented below are systematic differences rooted in architectural choices (GDI rendering path vs BMFont's proprietary outline renderer) and edge cases.

## Remaining Gaps

### 1. xoffset +/-1 systematic -- BMFont's fixed 8x internal supersample

**Root cause**: BMFont renders its own outlines at 8x via `DrawGlyphFromOutline` using `GGO_NATIVE` + polygon fill, producing different sub-pixel positions than `GetGlyphOutlineW(GGO_GRAY8_BITMAP)`. These are fundamentally different rendering paths in the Windows GDI stack. The 8x supersample approach was tested and reverted because it makes other metrics worse when applied to `GGO_GRAY8_BITMAP` output.

**Status**: Not fixable without implementing BMFont's own outline renderer (GGO_NATIVE polygon fill + 8x rasterization). This would be a major undertaking for diminishing returns (+/-1 pixel).

### 2. yoffset +/-1-3 -- downstream of xoffset + bearingY rounding

Same root cause as xoffset. BMFont computes yoffset from its supersampled outline data, while KernSmith uses `gmptGlyphOrigin.Y` from the GDI rasterizer. The difference is inherent to the different rendering paths.

### 3. Bell MT lineHeight -1 at 16pt

`CreateFont` (positional parameters, used by BMFont) vs `CreateFontIndirectW` (LOGFONTW struct, used by KernSmith) edge case. Only affects one font at one size. Low priority.

### 4. Bell MT xadvance and kerning differences

Same root cause as xoffset -- BMFont's outline renderer produces different advance widths than `GetGlyphOutlineW`. The avg +1.31 at 48pt is an artifact of the different rendering paths.

### 5. Missing kerning pairs (Bahnschrift)

GPOS class 0 fix reduced but didn't fully resolve this. The remaining missing pairs come from an unknown source in BMFont's kerning pipeline. Possible causes:
- BMFont may expand class pairs differently or have additional fallback logic
- BMFont's 32-bit GPOS parser may handle edge cases differently than our implementation
- There may be additional kerning sources (kern table fallback after GPOS) that BMFont merges

### 6. Atlas PNG channel configuration

KernSmith ignores `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` from .bmfc configs. Produces white-on-black instead of BMFont's white-on-alpha. Not a metrics issue -- tracked separately.

### 7. GDI lfHeight sign -- cell height vs em height (FIXED in Phase 78C)

**Root cause**: Our GDI backend was passing negative `lfHeight` to `CreateFontIndirectW` (em height mode), while BMFont passes positive `lfHeight` (cell height mode). For Georgia at size 56, this produced `tmHeight=65` (negative/em) vs `tmHeight=56` (positive/cell) because negative lfHeight excludes internal leading.

**Fix**: Changed `LfHeight = -(size * dpi / 72)` to `LfHeight = (size * dpi / 72)` in `GdiRasterizer.CreateHFont()`. This makes `lineHeight` and `base` match BMFont exactly for all tested fonts.

**Status**: Fixed in Phase 78C branch.

### 8. Anti-aliasing gradient -- GGO_GRAY8_BITMAP vs GGO_NATIVE polygon fill

**Root cause**: Our GDI backend uses `GGO_GRAY8_BITMAP` which produces smooth anti-aliasing with many intermediate gray levels (GDI's 65-level grayscale remapped to 0-255). BMFont uses `GGO_NATIVE` to extract vector outlines and rasterizes polygons itself with an 8x internal supersample, producing sharper edges with fewer intermediate gray values. Side-by-side character comparison (Phase 78C testing) shows visibly more anti-aliasing tones in our output vs BMFont's crisper edges.

**Status**: Not fixable without implementing BMFont's `GGO_NATIVE` polygon extraction and manual scanline rasterization (Path A: `DrawGlyphFromOutline`). Same root cause as gaps 1-2 (xoffset/yoffset). This is the fundamental architectural difference between our approach and BMFont's.

## Comparison Tooling

Reference the existing comparison tools:
- `tests/bmfont-compare/diff_all_fonts.py` -- multi-font BMFont vs GDI diff
- `tests/bmfont-compare/diff_fnt.py` -- single-font comparison
- `tests/bmfont-compare/diff_images.py` -- visual atlas comparison
- `tests/bmfont-compare/GenerateGdi/` -- regenerate KernSmith GDI output
- `tests/bmfont-compare/GenerateAll/` -- generates atlas PNGs from all 3 backends (FreeType, GDI, DirectWrite) with both fire-effect and plain configs. Usage: `dotnet run --framework net10.0-windows -- <output-dir>`
- `tests/bmfont-compare/CompareGlyphs/` -- character-by-character visual comparison across all backends + BMFont64, outputs `comparison.png` (fire effects) and `comparison2.png` (plain white). Usage: `dotnet run --framework net10.0-windows -- <data-dir>`. Gracefully skips missing backends.

## Potential Approaches

1. **Fixed 8x internal supersample** in GDI backend -- addresses xoffset, yoffset, likely Bell MT xadvance. Attempted and reverted in 78BB because it worsened metrics when applied to GGO_GRAY8_BITMAP. May need GGO_NATIVE + polygon fill approach instead.
2. **otmrcFontBox-based kerning scaling** -- switch from ppem/unitsPerEm to match BMFont's GPOS scaling formula. Shared change but only affects kerning math.
3. **Accept as known limitations** -- document that exact parity requires BMFont's proprietary outline renderer, which is outside scope.
