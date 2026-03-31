# Phase 150 -- Deferred Rasterizer Issues

> **Status**: Planning
> **Size**: Medium
> **Created**: 2026-03-30
> **Updated**: 2026-03-30
> **Dependencies**: Phase 78C (DirectWrite backend)
> **Origin**: [Phase 78G -- Remaining Rasterizer Issues](done/phase-78g-remaining-issues.md)
> **Goal**: Track and resolve deferred rasterizer issues from Phase 78G that require significant effort or carry notable risk.

---

## Overview

During Phase 78C DirectWrite implementation and Phase 78G triage, four issues were identified as worth tracking but not blocking Phase 78 completion. These issues involve complex API surface (DirectWrite COM interfaces, GDI sizing edge cases) and are deferred until there is user demand or strategic need.

## Priority Rankings

Each issue is ranked 1 (low) to 5 (high) on three dimensions:

| # | Issue | Ease | Break Risk | Importance | Status |
|---|-------|------|------------|------------|--------|
| 1 | Color Font Rendering (DW) | 1 | 2 | 2 | Open |
| 2 | Variable Font Support (DW) | 2 | 2 | 2 | Open |
| 3 | Native DW Kerning | 3 | 2 | 1 | Open |
| 4 | GDI MatchCharHeight Bug | 2 | 3 | 2 | Open |

**Legend**: Ease = ease to implement (5=easy). Break Risk = chance of breaking other things (5=high risk). Importance = importance to implement (5=critical).

## Issues

### 1. Color Font Rendering (DirectWrite) — Open

> Ease: 1 | Break Risk: 2 | Importance: 2

`SupportsColorFonts` is set to `false`. `SelectColorPalette()` stores the palette index in `_colorPaletteIndex` but it is never used during rasterization. Implementing color font support requires `IDWriteFactory4.TranslateColorGlyphRun` to decompose COLR/CPAL color glyphs into layered runs, plus a D2D dependency to render each color layer with the appropriate brush color. The current `IDWriteGlyphRunAnalysis` approach cannot render color glyphs.

### 2. Variable Font Support (DirectWrite) — Open

> Ease: 2 | Break Risk: 2 | Importance: 2

`SupportsVariableFonts` is set to `false`. `SetVariationAxes()` stores axes in `_variationAxes` but the stored values are never applied during rasterization. No code casts `_fontFace` to `IDWriteFontFace5` or calls `GetFontAxisValues()`/`SetFontAxisValues()`. Needs querying available axes via `IDWriteFontFace5.GetFontAxisValues` and applying user-specified axis values before rasterization.

### 3. Native DirectWrite Kerning — Open

> Ease: 3 | Break Risk: 2 | Importance: 1

`GetKerningPairs()` explicitly returns null, delegating to the shared GPOS/kern table parser. DirectWrite has `IDWriteFontFace1.GetKerningPairAdjustments` which could provide authoritative kerning data. Currently works correctly via the shared parser but misses any DirectWrite-specific kerning behavior. Optimization opportunity, not a functional issue.

### 4. GDI MatchCharHeight Bug — Open

> Ease: 2 | Break Risk: 3 | Importance: 2

GDI with `HandlesOwnSizing=true` produces wrong metrics when `MatchCharHeight=true` (negative fontSize in .bmfc). Example: Bahnschrift size -12 produces lineHeight=12 instead of the expected 14. FreeType and DirectWrite handle MatchCharHeight correctly. `HandlesOwnSizing` is implemented and the sizing logic exists in `CreateHFont`, but the specific negative-fontSize scenario described here needs re-validation to confirm whether this is still reproducing.

## Files Reference

| File | Relevance |
|------|-----------|
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteRasterizer.cs` | Issues 1-3 |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` | Issue 4 |
