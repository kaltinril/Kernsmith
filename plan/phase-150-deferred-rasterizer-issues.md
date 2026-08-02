# Phase 150 -- Deferred Rasterizer Issues

> **Status**: Planning
> **Size**: Medium
> **Created**: 2026-03-30
> **Updated**: 2026-08-01
> **Dependencies**: Phase 78C (DirectWrite backend)
> **Origin**: [Phase 78G -- Remaining Rasterizer Issues](done/phase-78g-remaining-issues.md)
> **Goal**: Track and resolve deferred rasterizer issues from Phase 78G that require significant effort or carry notable risk.

---

## Overview

During Phase 78C DirectWrite implementation and Phase 78G triage, four issues were identified as worth tracking but not blocking Phase 78 completion. These issues involve complex API surface (DirectWrite COM interfaces, GDI sizing edge cases) and are deferred until there is user demand or strategic need. **Issue 5 was added 2026-08-01** from Phase 165 — it is a *core* (not backend-specific) defect and is the highest-importance item in this doc.

> **Last validated 2026-07-28** — issues 1-3 still match the code byte-for-byte. Issue 4 was re-confirmed as **still reproducing** via a full end-to-end trace (the hedge asking for re-validation has been replaced with the root-cause chain below).

## Priority Rankings

Each issue is ranked 1 (low) to 5 (high) on three dimensions:

| # | Issue | Ease | Break Risk | Importance | Status |
|---|-------|------|------------|------------|--------|
| 1 | Color Font Rendering (DW) | 1 | 2 | 2 | Open |
| 2 | Variable Font Support (DW) | 2 | 2 | 2 | Open |
| 3 | Native DW Kerning | 3 | 2 | 1 | Open |
| 4 | GDI MatchCharHeight Bug | 2 | 3 | 2 | Open |
| 5 | **Synthetic Bold/Italic Ignores Capabilities (core)** | 4 | 2 | 4 | Open |

**Legend**: Ease = ease to implement (5=easy). Break Risk = chance of breaking other things (5=high risk). Importance = importance to implement (5=critical).

## Issues

### 1. Color Font Rendering (DirectWrite) — Open

> Ease: 1 | Break Risk: 2 | Importance: 2

`SupportsColorFonts` is set to `false`. `SelectColorPalette()` stores the palette index in `_colorPaletteIndex` but it is never used during rasterization. Implementing color font support requires `IDWriteFactory4.TranslateColorGlyphRun` to decompose COLR/CPAL color glyphs into layered runs, plus a D2D dependency to render each color layer with the appropriate brush color. The current `IDWriteGlyphRunAnalysis` approach cannot render color glyphs.

Note that `BmFont.cs:201-205` gates the call on `rasterizer.Capabilities.SupportsColorFonts`, so with the flag `false` (`DirectWriteCapabilities.cs:12`) `SelectColorPalette` is unreachable from the generation pipeline today — it is dead code until the capability flips.

### 2. Variable Font Support (DirectWrite) — Open

> Ease: 2 | Break Risk: 2 | Importance: 2

`SupportsVariableFonts` is set to `false`. `SetVariationAxes()` stores axes in `_variationAxes` but the stored values are never applied during rasterization. No code casts `_fontFace` to `IDWriteFontFace5`.

**API correction**: there is no `SetFontAxisValues` — that API does not exist. `IDWriteFontFace5` exposes `GetFontAxisValues`, `GetFontAxisValueCount`, `HasVariations`, and `GetFontResource`. Axis values are *read* from an existing face, never set on it; applying them means creating a **new** face: `IDWriteFontFace5::GetFontResource` → `IDWriteFontResource::CreateFontFace(simulations, axisValues, axisValueCount, &newFace)`. So the implementation is: cast `_fontFace` to `IDWriteFontFace5`, query available axes with `GetFontAxisValueCount`/`GetFontAxisValues`, obtain the `IDWriteFontResource`, and call `CreateFontFace` with the user-specified axis values to get the instanced face used for rasterization.

As with issue 1, `BmFont.cs:194-199` gates `SetVariationAxes` on `rasterizer.Capabilities.SupportsVariableFonts`, so with the flag `false` (`DirectWriteCapabilities.cs:13`) the method is unreachable from the pipeline today.

### 3. Native DirectWrite Kerning — Open

> Ease: 3 | Break Risk: 2 | Importance: 1

`GetKerningPairs()` explicitly returns null, delegating to the shared GPOS/kern table parser. DirectWrite has `IDWriteFontFace1.GetKerningPairAdjustments` which could provide authoritative kerning data. Currently works correctly via the shared parser but misses any DirectWrite-specific kerning behavior. Optimization opportunity, not a functional issue.

### 4. GDI MatchCharHeight Bug — Open

> Ease: 2 | Break Risk: 3 | Importance: 2

GDI with `HandlesOwnSizing=true` produces wrong metrics when `MatchCharHeight=true` (negative fontSize in `.bmfc`). Example: Bahnschrift size `-12` produces `lineHeight=12 base=10` instead of BMFont's `lineHeight=14 base=12`.

**Confirmed still reproducing (2026-07-28).** Root-cause chain, end to end:

1. `src/KernSmith/Config/BmfcConfigReader.cs:102-113` — a negative `fontSize` is immediately absolutized: `options.Size = Math.Abs(sizeVal); options.MatchCharHeight = true;`. The sign itself is discarded here.
2. `src/KernSmith/Rasterizer/RasterOptions.cs:76-97` — `FromGeneratorOptions` copies 15 fields but **not** `MatchCharHeight`; `RasterOptions` has no such member, so the flag can never reach *any* backend.
3. `src/KernSmith/BmFont.cs:227-240` — the sole consumer of `MatchCharHeight`. It only decides whether to run the cell-height→ppem conversion, and the entire block is **skipped** when `Capabilities.HandlesOwnSizing` is true.
4. `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:733` — `HandlesOwnSizing => true` (the interface default is `false`, `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs:27`). GDI is therefore handed `Size=12` with no knowledge that an em-height match was requested.
5. `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:345` — `LfHeight` is **always positive**, i.e. GDI cell-height mode. GDI's em-height mode requires a **negative** `lfHeight`, and nothing in the file ever produces one.
6. `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:260-268` — `GetFontMetrics` returns `LineHeight = ceil(tm.TmHeight / aa) = 12` and `Ascent = 10`.
7. `src/KernSmith/Output/BmFontModelBuilder.cs:52-56` — rasterizer-supplied metrics are used verbatim, so the `.fnt` gets `common lineHeight=12 base=10`.

Corroborating generated artifacts (local; `tests/bmfont-compare/` is gitignored):

- BMFont reference — `tests/bmfont-compare/gum-bmfont/Font12Bahnschrift.fnt:2` → `lineHeight=14 base=12`
- KernSmith GDI — `tests/bmfont-compare/output/Font12Bahnschrift-gdi.fnt:2` → `lineHeight=12 base=10`

**Other backends**: FreeType and DirectWrite **honor** `MatchCharHeight` — they take the `!HandlesOwnSizing` branch and use `fontSize` directly as ppem. Only GDI ignores it. They are not pixel-exact with BMFont either (`output/Font12Bahnschrift-freetype.fnt:2` and `output/Font12Bahnschrift-directwrite.fnt:2` are both `lineHeight=15 base=12` vs BMFont's `14`/`12`), but that residual ±1 is the separate accepted-limitation rounding issue documented in [phase-78g-remaining-issues.md:84-89](done/phase-78g-remaining-issues.md), not this bug.

**Fix shape (not yet implemented)** — two viable paths:

- Add `MatchCharHeight` to `RasterOptions` (copy it in `FromGeneratorOptions`) and let `GdiRasterizer.CreateHFont` negate `LfHeight` when the flag is set; or
- Preserve the `.bmfc` sign end to end instead of calling `Math.Abs` at `BmfcConfigReader.cs:106`, letting a negative size flow through to `LfHeight`.

Either path changes pixel output, so `python tests/bmfont-compare/regression_check.py` is required before the work is considered done.

### 5. Synthetic Bold/Italic Silently No-Op When the Backend Can't Do It — Open

> Ease: 4 | Break Risk: 2 | Importance: 4
> **Found**: 2026-08-01 during [Phase 165](done/phase-165-irasterizer-integration.md) (Native rasterizer `IRasterizer` integration).
> **Unlike issues 1-4 this is a core defect, not a backend limitation** — the bug is in `src/KernSmith/BmFont.cs`, shared by every backend.

`src/KernSmith/BmFont.cs:287` skips `BoldPostProcessor` / `ItalicPostProcessor` whenever `options.Bold` / `options.Italic` is set:

```csharp
if (processor is BoldPostProcessor && options.Bold)
    continue;
if (processor is ItalicPostProcessor && options.Italic)
    continue;
```

The assumption is "the rasterizer already applied the emboldening/slant, so don't double-apply it." But the gate **never consults `IRasterizerCapabilities.SupportsSyntheticBold` / `SupportsSyntheticItalic`** — it keys off the *user option* alone.

**Why it went unnoticed**: all four previously shipped backends (FreeType, GDI, DirectWrite, StbTrueType) declare `SupportsSyntheticBold => true`, so the gate has been accidentally correct for the entire life of the code.

**How it surfaced**: the Native backend is the first to declare `SupportsSyntheticBold => false` / `SupportsSyntheticItalic => false` (synthetic transforms are Phase 167). Native therefore *ignores* `Bold`/`Italic`, and the core *also* skips the post-processor — so `Bold = true` + `RasterizerBackend.Native` produces **regular-weight glyphs with no warning, no error, and no diagnostic**.

The `tests/bmfont-compare` harness corroborates this: bold/italic/outlined configs show the largest Native-vs-FreeType deltas (`Font48Bauhaus_93_Italic` mean 3.93px width delta; plain-synbold 1.98px), while regular upright faces are near-identical.

**Fix shape**: gate on the capability, not just the option — run the post-processor when the backend reports it cannot apply the transform itself. Roughly:

```csharp
if (processor is BoldPostProcessor && options.Bold
    && rasterizer.Capabilities.SupportsSyntheticBold)
    continue;
```

**Regression requirement**: this is a **core shared-pixel-path change**, so `python tests/bmfont-compare/regression_check.py` is required before it is considered done. It should come back **identical for FreeType, GDI, DirectWrite and StbTrueType** (all four report `true`, so the branch is unchanged for them) — any pixel delta on those four means the fix is wrong. Native is the only column expected to change.

## Files Reference

| File | Relevance |
|------|-----------|
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteRasterizer.cs` | Issues 1-3 |
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteCapabilities.cs` | Issues 1-2 — `:12-13` is where `SupportsColorFonts`/`SupportsVariableFonts` actually live |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` | Issue 4 — `HandlesOwnSizing` (`:733`), `LfHeight` (`:345`), `GetFontMetrics` (`:260-268`) |
| `src/KernSmith/Config/BmfcConfigReader.cs` | Issue 4 — `:102-113`, where the negative-size sign is discarded |
| `src/KernSmith/Rasterizer/RasterOptions.cs` | Issue 4 — `:76-97`, `MatchCharHeight` is never copied to the backend options |
| `src/KernSmith/BmFont.cs` | Issue 4 — `:227-240`, the sizing decision point; also gates issues 1-2 at `:194-205`. Issue 5 — `:287-290`, the bold/italic post-processor skip that ignores capabilities |
| `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs` | Issue 5 — `SupportsSyntheticBold` / `SupportsSyntheticItalic`, the flags the fix must consult |
| `src/KernSmith.Rasterizers.Native/NativeCapabilities.cs` | Issue 5 — the first backend to report `false`, which is what exposed the bug |
