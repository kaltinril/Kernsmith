# Phase 78CC -- Font Sizing & DPI Gaps

> **Status**: Planning
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

### Verify MatchCharHeight Implementation

`MatchCharHeight` exists as a property on `FontGeneratorOptions` but needs verification that it actually affects the sizing logic in `BmFont.cs` and the rasterizer backends, not just gets stored as metadata.

- [ ] Trace `MatchCharHeight` through from `FontGeneratorOptions` to actual font size calculation
- [ ] Verify negative `fontSize` round-trips correctly through `BmfcConfigReader` / `BmfcConfigWriter`

### Consider BMFont-style autoFitNumPages

BMFont's "scale font size up to fill texture" feature is not currently supported in KernSmith. Decide whether to implement it:

- **Pro**: Exact `.bmfc` config parity for users migrating from BMFont
- **Con**: Niche feature, inverse of what most users want (they pick a size and want the right texture)
- **Decision**: TBD -- may belong in a future phase if there is user demand

### Investigate GDI/DirectWrite lineHeight=65 vs FreeType/BMFont lineHeight=56

For Georgia size 56 at 72 DPI, GDI and DirectWrite both produce `lineHeight=65` while FreeType and BMFont produce `lineHeight=56`. This is related to the Phase 78BB findings about GDI/DirectWrite sizing differences and is tracked there.

### Consider Space Outline Rendering

BMFont renders outline pixels for space (char 32). Decide whether KernSmith should match this behavior:

- **Pro**: Exact pixel-level parity with BMFont
- **Con**: Wastes texture space, no visual impact on rendered text
- **Decision**: TBD -- low priority

## Files Reference

| File | Relevance |
|------|-----------|
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Has `Dpi` (line 75) and `MatchCharHeight` (line 156) |
| `src/KernSmith/Rasterizer/RasterOptions.cs` | Internal `Dpi` property piped from generator options |
| `src/KernSmith/Config/BmfcConfigReader.cs` | Parses `fontSize` sign convention and `dpi` setting |
| `src/KernSmith/Config/BmfcConfigWriter.cs` | Emits `fontSize` and `dpi` settings |
| `src/KernSmith/BmFont.cs` | Core sizing logic -- verify `MatchCharHeight` is used |
