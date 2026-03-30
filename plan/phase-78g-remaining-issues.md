# Phase 78G -- Remaining Rasterizer Issues

> **Status**: Planning
> **Size**: Small-Medium
> **Created**: 2026-03-27
> **Updated**: 2026-03-27
> **Dependencies**: Phase 78C (DirectWrite backend)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Track and resolve remaining open issues discovered during Phase 78C DirectWrite work.

---

## Overview

During Phase 78C implementation and multi-backend comparison testing, several issues were identified that are outside the scope of 78C's core rasterization goal. This phase collects them for future resolution.

## Issues

### 1. Color Font Rendering (DirectWrite)

`SupportsColorFonts` is set to `false`. Implementing color font support requires `IDWriteFactory4.TranslateColorGlyphRun` to decompose COLR/CPAL color glyphs into layered runs, plus a D2D dependency to render each color layer with the appropriate brush color. The current `IDWriteGlyphRunAnalysis` approach cannot render color glyphs.

### 2. Variable Font Support (DirectWrite)

`SupportsVariableFonts` is set to `false`. `SetVariationAxes()` stores axes but no `IDWriteFontFace5` axis manipulation is implemented. Needs querying available axes via `IDWriteFontFace5.GetFontAxisValues` and applying user-specified axis values before rasterization.

### 3. Synthetic Bold/Italic (DirectWrite)

DirectWrite ignores `options.Bold` and `options.Italic` from `RasterOptions`. FreeType applies these via `FT_GlyphSlot_Embolden`/`FT_GlyphSlot_Oblique`; DirectWrite equivalent would use `DWRITE_FONT_SIMULATIONS_BOLD` / `DWRITE_FONT_SIMULATIONS_OBLIQUE` passed when creating the font face.

### 4. Native DirectWrite Kerning

`GetKerningPairs()` returns null, delegating to the shared GPOS/kern table parser. DirectWrite has `IDWriteFontFace1.GetKerningPairAdjustments` which could provide authoritative kerning data. Currently works correctly via the shared parser but misses any DirectWrite-specific kerning behavior.

### 5. No DirectWrite Unit Tests

Only factory registration tests exist for the DirectWrite backend. The `tests/bmfont-compare/` comparison tools provide visual validation, but there are no automated unit tests for glyph rasterization, metrics, font loading, or disposal.

### 6. GDI MatchCharHeight Bug

GDI with `HandlesOwnSizing=true` produces wrong metrics when `MatchCharHeight=true` (negative fontSize in .bmfc). Example: Bahnschrift size -12 produces lineHeight=12 instead of the expected 14. FreeType and DirectWrite handle MatchCharHeight correctly. Needs investigation into GDI's TEXTMETRIC calculation path when negative font sizes are used.

### 7. Rounding Differences (lineHeight/base)

The shared OS/2 metrics path uses `Math.Ceiling` while BMFont64/GDI internally use Windows `MulDiv` (round-to-nearest). This causes +-1 lineHeight/base differences on ~7/15 test fonts for both FreeType and DirectWrite vs BMFont64. Fixing this would require either:
- Changing shared pipeline rounding (risks breaking FreeType parity with existing users)
- Making rasterizers fully own their sizing pipeline (architectural change)

Accepted as a known limitation unless user demand justifies the architectural change.

### 8. BMFont64 Channel-Based Outline Rendering

BMFont64 uses `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` settings (values 0-4) to control which channels contain glyph data vs outline data. For example, `alphaChnl=1` (outline in alpha), `redChnl=0, greenChnl=0, blueChnl=0` (glyph in RGB) is the typical outlined font setup. The pixel shader decodes this at render time.

KernSmith uses an `outlineColor` extension instead of the standard channel encoding. Need to support the standard BMFont channel outline behavior as a baseline, with KernSmith extensions overriding when present. See `reference/REF-08-bmfont-internals.md` sections 3 and 13 for the channel encoding spec.

### 9. Space Outline Width Discrepancy vs BMFont64

Phase 78F added empty atlas entries for space (char 32) when outline > 0, using a `1 + 2*outlineThickness` base matching the open-source BMFont `DrawGlyphFromOutline` logic (1x1 transparent image expanded by `AddOutline`). KernSmith produces **9x9** for outline=4, but BMFont64.exe produces **11x9** (width=11, height=9) with identical settings (padding=0, aa=1, forceZero=0).

Investigation traced through the full open-source BMFont pipeline (`DrawGlyphFromOutline` → `TrimLeftAndRight` → AA downscale → empty scanline removal → `AddOutline` → `AddChar` with padding) and confirmed it should produce 9x9. GDI `GetGlyphOutlineW` returns `gmBlackBoxX=1` for Georgia space at size 56, and `GetCharABCWidthsW` returns `abcB=1`. The 2px width difference is unexplained from the open-source code (SourceForge trunk and GitHub mirrors both show the same 1x1 early return). BMFont64 is the same codebase (64-bit build, not a fork), so the difference may be a version-specific change not yet in the public source.

The content is entirely transparent, so the difference has no visual impact. Low priority unless a consumer depends on exact width matching.

### 10. Comparison Tool Consolidation

`CompareGlyphs` is still a separate tool from `GenerateAll` in `tests/bmfont-compare/`. Per the user's step 1-5 workflow, these should be merged into a single tool that generates output from all backends and produces comparison images in one run.

## Files Reference

| File | Relevance |
|------|-----------|
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteRasterizer.cs` | Issues 1-5 |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` | Issue 6 |
| `src/KernSmith/BmFont.cs` | Issue 7 (shared metrics pipeline) |
| `src/KernSmith/Atlas/AtlasBuilder.cs` | Issue 8 (channel encoding) |
| `src/KernSmith/BmFont.cs` | Issue 9 (Phase 78F space outline logic) |
| `tests/bmfont-compare/` | Issues 5, 10 |
| `reference/REF-08-bmfont-internals.md` | Issue 8 (channel encoding spec) |
