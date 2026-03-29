# Phase 78CC -- Font Sizing & DPI Gaps

> **Status**: Complete
> **Size**: Small
> **Created**: 2026-03-27
> **Updated**: 2026-03-27
> **Dependencies**: None (pre-existing gaps, not caused by 78C)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Verify sizing/DPI parity between KernSmith and BMFont64, and decide on remaining minor behavioral differences.

---

## Overview

During Phase 78C testing, character-by-character comparison of KernSmith (FreeType/GDI/DirectWrite) vs BMFont64 revealed what initially appeared to be a 3x size difference. Investigation resolved most of the discrepancy:

- The 3x size difference was caused by `autoFitNumPages=1` in the BMFont64 `.bmfc` config. BMFont's autofit feature scales the font **size up** to fill the texture, not down. With `autoFitNumPages=0`, BMFont64 produces `lineHeight=56` matching our FreeType backend exactly.
- `FontGeneratorOptions` already has both `Dpi` (default 72, line 75) and `MatchCharHeight` (line 156) properties -- they exist and are wired through.
- At 72 DPI, FreeType and BMFont produce identical `lineHeight=56` for Georgia size 56. The DPI difference (72 vs 96) is real but may be intentional -- at matching DPI the outputs match.
- GDI/DirectWrite produce `lineHeight=65` for the same font/size, which is the known GDI sizing difference from Phase 78BB (not a new bug).

## Key Finding: BMFont autoFitNumPages vs KernSmith AutofitTexture

BMFont's `autoFitNumPages` and KernSmith's `AutofitTexture` are essentially **opposite operations**:

| Feature | What it does |
|---------|-------------|
| BMFont `autoFitNumPages=N` | Scales the font **size up** to find the maximum size that fits all characters into N texture pages |
| KernSmith `AutofitTexture` | Shrinks the **texture** down to fit the glyphs at the given font size |

This means a `.bmfc` config with `autoFitNumPages=1` and `fontSize=56` does NOT produce a size-56 font -- BMFont will search for the largest size that fits in the configured texture, potentially much larger than 56.

## Minor Finding: Space Character Outline

BMFont renders outline pixels even for space (char 32), giving it `width=11 height=9`, while our backends give `width=0 height=0` for space. This is a minor cosmetic difference that does not affect text layout (space advances via `xadvance`, not glyph dimensions).

## Remaining Tasks

### ~~Verify MatchCharHeight Implementation~~ VERIFIED

`MatchCharHeight` (negative fontSize in .bmfc) works correctly for FreeType and DirectWrite -- both produce correct metrics when MatchCharHeight is enabled. However, GDI has a sizing bug: with `HandlesOwnSizing=true` and MatchCharHeight=true (negative fontSize), GDI produces wrong metrics (e.g., lineHeight=12 instead of 14 for Bahnschrift size -12). This is tracked in Phase 78G.

- [x] Trace `MatchCharHeight` through from `FontGeneratorOptions` to actual font size calculation
- [x] Verify negative `fontSize` round-trips correctly through `BmfcConfigReader` / `BmfcConfigWriter`

### Consider BMFont-style autoFitNumPages

BMFont's "scale font size up to fill texture" feature is not currently supported in KernSmith. Decide whether to implement it:

- **Pro**: Exact `.bmfc` config parity for users migrating from BMFont
- **Con**: Niche feature, inverse of what most users want (they pick a size and want the right texture)
- **Decision**: TBD -- may belong in a future phase if there is user demand

### ~~Investigate GDI/DirectWrite lineHeight=65 vs FreeType/BMFont lineHeight=56~~ RESOLVED

**Root cause found and fixed (2026-03-27):** DirectWrite had `HandlesOwnSizing=true` but did not perform cell-height-to-ppem conversion internally. This caused it to render at the full em-square size (~14% too large). Fix: set `HandlesOwnSizing=false` so the shared pipeline handles the conversion, and `GetFontMetrics()` returns null. After the fix, DirectWrite produces `lineHeight=56` matching FreeType and BMFont. GDI was already correct (it handles sizing internally via Windows TEXTMETRIC APIs).

**Remaining ±1 rounding differences:** DirectWrite still shows ±1 lineHeight/base on ~7/15 test fonts vs BMFont64. This is caused by the shared OS/2 metrics path using `Math.Ceiling` while BMFont64/GDI use Windows `MulDiv` (round-to-nearest) internally. Fixing this would require either changing shared pipeline code (affects FreeType) or making DirectWrite fully own its sizing pipeline (architectural change). Accepted as a known limitation.

### Consider Space Outline Rendering

BMFont renders outline pixels for space (char 32). Decide whether KernSmith should match this behavior:

- **Pro**: Exact pixel-level parity with BMFont
- **Con**: Wastes texture space, no visual impact on rendered text
- **Decision**: TBD -- low priority

### BMFont64 Channel-Based Outline Rendering

BMFont64 uses `alphaChnl`, `redChnl`, `greenChnl`, `blueChnl` settings (values 0-4) to control which channels contain glyph data vs outline data. For example, `alphaChnl=1` (outline in alpha), `redChnl=0, greenChnl=0, blueChnl=0` (glyph in RGB) is the typical outlined font setup. This is not an explicit "outline color" setting -- it is a channel encoding that the pixel shader decodes at render time. KernSmith uses an `outlineColor` extension instead. Tracked in Phase 78G for supporting standard BMFont channel outline behavior as a baseline.

## Files Reference

| File | Relevance |
|------|-----------|
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Has `Dpi` (line 75) and `MatchCharHeight` (line 156) |
| `src/KernSmith/Rasterizer/RasterOptions.cs` | Internal `Dpi` property piped from generator options |
| `src/KernSmith/Config/BmfcConfigReader.cs` | Parses `fontSize` sign convention and `dpi` setting |
| `src/KernSmith/Config/BmfcConfigWriter.cs` | Emits `fontSize` and `dpi` settings |
| `src/KernSmith/BmFont.cs` | Core sizing logic -- verify `MatchCharHeight` is used |
