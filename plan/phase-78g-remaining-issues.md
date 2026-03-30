# Phase 78G -- Remaining Rasterizer Issues

> **Status**: In Progress
> **Size**: Small-Medium
> **Created**: 2026-03-27
> **Updated**: 2026-03-29
> **Dependencies**: Phase 78C (DirectWrite backend)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Track and resolve remaining open issues discovered during Phase 78C DirectWrite work.

---

## Overview

During Phase 78C implementation and multi-backend comparison testing, several issues were identified that are outside the scope of 78C's core rasterization goal. This phase collects them for future resolution.

## Priority Rankings

Each issue is ranked 1 (low) to 5 (high) on three dimensions:

| # | Issue | Ease | Break Risk | Importance | Status |
|---|-------|------|------------|------------|--------|
| 1 | Color Font Rendering (DW) | 1 | 2 | 2 | Open |
| 2 | Variable Font Support (DW) | 2 | 2 | 2 | Open |
| 3 | Synthetic Bold/Italic (DW) | 4 | 1 | 3 | **Resolved** |
| 4 | Native DW Kerning | 3 | 2 | 1 | Open |
| 5 | DirectWrite Unit Tests | 4 | 1 | 4 | **Resolved** |
| 6 | GDI MatchCharHeight Bug | 2 | 3 | 2 | Open (needs re-validation) |
| 7 | Rounding Differences | 1 | 5 | 1 | Accepted limitation |
| 8 | Channel-Based Outline Rendering | — | — | — | **Resolved** |
| 9 | Space Outline Width Discrepancy | — | — | — | **Accepted** (no visual impact) |
| 10 | Comparison Tool Consolidation | 4 | 1 | 3 | **Resolved** |
| 11 | FromConfig bold/italic bug | — | — | — | **Resolved** |
| 12 | ForceSyntheticBold/Italic API | — | — | — | **Resolved** |
| 13 | DW system font simulations bug | — | — | — | **Resolved** |
| 14 | GDI synthetic italic via MAT2 | — | — | — | **Resolved** |
| 15 | CLI flags for ForceSynthetic | 4 | 1 | 3 | Open |
| 16 | UI controls for ForceSynthetic | 3 | 1 | 3 | Open |
| 17 | Documentation for bold/italic | 4 | 1 | 3 | Open |
| 18 | DW synthetic bold strength | — | — | — | **Accepted** (DW limitation) |

**Legend**: Ease = ease to implement (5=easy). Break Risk = chance of breaking other things (5=high risk). Importance = importance to implement (5=critical).

**Recommended order** (highest value first): #5, #3, #10, #4, #2, #1, #6, #7

## Issues

### 1. Color Font Rendering (DirectWrite) — Open

> Ease: 1 | Break Risk: 2 | Importance: 2

`SupportsColorFonts` is set to `false`. `SelectColorPalette()` stores the palette index in `_colorPaletteIndex` but it is never used during rasterization. Implementing color font support requires `IDWriteFactory4.TranslateColorGlyphRun` to decompose COLR/CPAL color glyphs into layered runs, plus a D2D dependency to render each color layer with the appropriate brush color. The current `IDWriteGlyphRunAnalysis` approach cannot render color glyphs.

### 2. Variable Font Support (DirectWrite) — Open

> Ease: 2 | Break Risk: 2 | Importance: 2

`SupportsVariableFonts` is set to `false`. `SetVariationAxes()` stores axes in `_variationAxes` but the stored values are never applied during rasterization. No code casts `_fontFace` to `IDWriteFontFace5` or calls `GetFontAxisValues()`/`SetFontAxisValues()`. Needs querying available axes via `IDWriteFontFace5.GetFontAxisValues` and applying user-specified axis values before rasterization.

### 3. ~~Synthetic Bold/Italic (DirectWrite)~~ — Resolved

DirectWrite now caches font faces per simulation combo (None, Bold, Oblique, Bold|Oblique). `GetFontFaceForOptions()` maps `options.Bold` → `DWRITE_FONT_SIMULATIONS_BOLD`, `options.Italic` → `DWRITE_FONT_SIMULATIONS_OBLIQUE`. For system fonts, the font file is extracted via `GetFiles()` so simulated variants can be created. All cached faces are disposed in `Cleanup()`.

### 4. Native DirectWrite Kerning — Open

> Ease: 3 | Break Risk: 2 | Importance: 1

`GetKerningPairs()` explicitly returns null, delegating to the shared GPOS/kern table parser. DirectWrite has `IDWriteFontFace1.GetKerningPairAdjustments` which could provide authoritative kerning data. Currently works correctly via the shared parser but misses any DirectWrite-specific kerning behavior. Optimization opportunity, not a functional issue.

### 5. ~~No DirectWrite Unit Tests~~ — Resolved

13 DirectWrite unit tests added in `DirectWriteRasterizerTests.cs`, mirroring the GDI test patterns: factory registration, font loading, glyph rasterization, metrics, capabilities, disposal, pixel format. Gated with `#if DIRECTWRITE` for `net10.0-windows` only (TerraFX package constraint).

### 6. GDI MatchCharHeight Bug — Open (needs re-validation)

> Ease: 2 | Break Risk: 3 | Importance: 2

GDI with `HandlesOwnSizing=true` produces wrong metrics when `MatchCharHeight=true` (negative fontSize in .bmfc). Example: Bahnschrift size -12 produces lineHeight=12 instead of the expected 14. FreeType and DirectWrite handle MatchCharHeight correctly. `HandlesOwnSizing` is implemented and the sizing logic exists in `CreateHFont`, but the specific negative-fontSize scenario described here needs re-validation to confirm whether this is still reproducing.

### 7. Rounding Differences (lineHeight/base) — Accepted limitation

> Ease: 1 | Break Risk: 5 | Importance: 1

The shared OS/2 metrics path in `BmFontModelBuilder.cs` uses `Math.Ceiling` while BMFont64/GDI internally use Windows `MulDiv` (round-to-nearest). This causes +-1 lineHeight/base differences on ~7/15 test fonts for both FreeType and DirectWrite vs BMFont64. Fixing this would require either:
- Changing shared pipeline rounding (risks breaking FreeType parity with existing users)
- Making rasterizers fully own their sizing pipeline (architectural change)

Accepted as a known limitation unless user demand justifies the architectural change.

### ~~8. BMFont64 Channel-Based Outline Rendering~~ — Resolved

Full BMFont channel specification (values 0-4) is now implemented:
- `ChannelContent` enum in `Config/ChannelContent.cs` with all 5 values (Glyph=0, Outline=1, GlyphAndOutline=2, Zero=3, One=4)
- `ChannelConfig` in `Config/ChannelConfig.cs` with per-channel configuration and optional inversion
- `BmFontModelBuilder` reads `options.Channels` and writes `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` to common block
- `XmlFormatter` outputs all four channel values

### ~~9. Space Outline Width Discrepancy vs BMFont64~~ — Accepted

KernSmith produces **9x9** for outline=4 (formula: `1 + 2*thickness`), matching the open-source BMFont logic. BMFont64.exe produces **11x9** with identical settings. The 2px width difference is unexplained from the public source and may be a version-specific change. Content is entirely transparent — no visual impact. Accepted as-is.

### 10. ~~Comparison Tool Consolidation~~ — Resolved

CompareGlyphs logic merged into GenerateAll. Produces 4 fixed comparison images plus per-config comparisons. Output goes to `tests/bmfont-compare/output/`. Supports `--no-compare` and `--config` flags. comparison3/4 show bold/italic with 11 columns: normal vs real face vs synthetic per backend (FT, GDI, DW, BMFont).

### 11. ~~FromConfig bold/italic bug~~ — Resolved

`BmFont.FromConfig()` system font path was not doing bold/italic variant lookup — always loaded the regular face and relied entirely on synthetic styling. Fixed by routing through `GenerateFromSystem()` which tries the styled variant first.

### 12. ~~ForceSyntheticBold/Italic API~~ — Resolved

Added `ForceSyntheticBold` and `ForceSyntheticItalic` to `FontGeneratorOptions`, `RasterOptions`, and `BmFontBuilder` (via `WithForceSyntheticBold()`/`WithForceSyntheticItalic()`). When set, skips the native bold/italic face lookup and forces the rasterizer to apply synthetic styling on the regular face. Also forces the `LoadFont(data)` path instead of `LoadSystemFont` to prevent GDI's font mapper from silently selecting the real styled face.

### 13. ~~DW system font simulations bug~~ — Resolved

`DirectWriteRasterizer.LoadSystemFont()` always created the font face with `DWRITE_FONT_SIMULATIONS_NONE` and `GetFontFaceForOptions()` silently returned the base face because there was no font file to recreate with simulations. Fixed by extracting the `IDWriteFontFile` from the system font face via `GetFiles()`, enabling simulation variant creation for system fonts.

### 14. ~~GDI synthetic italic via MAT2~~ — Resolved

GDI has no built-in synthetic oblique API like FreeType's `FT_GlyphSlot_Oblique`. When `ForceSyntheticItalic` is set, `LfItalic` is set to 0 (preventing GDI font mapper from selecting the real italic face) and a horizontal shear transform is applied via `MAT2.EM21` in `GetGlyphOutlineW` (~20° slant, `tan(20°) ≈ 0.364`).

## Remaining Work

### 15. CLI flags for ForceSynthetic — Open

> Ease: 4 | Break Risk: 1 | Importance: 3

CLI has `-b`/`--bold` and `-i`/`--italic` but no `--synthetic-bold` or `--synthetic-italic` flags. Add these to `GenerateCommand.cs`.

### 16. UI controls for ForceSynthetic — Open

> Ease: 3 | Break Risk: 1 | Importance: 3

UI has Bold/Italic checkboxes but no synthetic option. Add a second column of checkboxes:

```
[ ] Bold      [ ] Synthetic bold
[ ] Italic    [ ] Synthetic italic
[ ] AA        [ ] Hinting
```

"Synthetic bold/italic" auto-checks Bold/Italic when enabled. Unchecking Bold/Italic disables the synthetic checkbox. Tooltips explain native face vs synthetic emboldening/oblique.

### 17. Documentation for bold/italic and ForceSynthetic — Open

> Ease: 4 | Break Risk: 1 | Importance: 3

Missing documentation:
- CLI docs (`tools/KernSmith.Cli/README.md`, `docs/cli/commands.md`) describe `-b`/`-i` as "synthetic" when the actual behavior is "native face with synthetic fallback"
- Root `README.md` has no examples of `.WithBold()`, `.WithForceSyntheticBold()`
- Core docs (`docs/core/index.md`) don't mention bold/italic API at all
- `.bmfc` config roundtrip for `ForceSyntheticBold`/`ForceSyntheticItalic` not implemented in `BmfcConfigReader`/`BmfcConfigWriter`

### 18. DW synthetic bold strength — Accepted limitation

DirectWrite's `DWRITE_FONT_SIMULATIONS_BOLD` has a fixed internal strength that cannot be tuned. It produces lighter synthetic bold than FreeType's `ppem/24`. Users who want heavier text can use an outline with matching color as a workaround.

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
