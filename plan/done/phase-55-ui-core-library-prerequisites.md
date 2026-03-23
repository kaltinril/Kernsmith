# Phase 55 — Core Library Prerequisites for UI

> **Status**: Complete — all needed items implemented; remaining items deferred as not currently required
> **Created**: 2026-03-21
> **Goal**: Add missing API surface, builder methods, and data exposure needed by the KernSmith UI application (Phases 60-69).

---

## Overview

During UI planning (Phases 60-69), several gaps were identified where the UI needs functionality that the core KernSmith NuGet library does not currently provide. This phase documents every gap, organized by category, so the core library team can implement them **before** or **in parallel** with UI development.

### Guiding Principles

- **No breaking changes** to the existing public API.
- **All new public API** must have XML documentation.
- **Tests** cover all new functionality.
- **Backward compatible** — existing consumers are unaffected.

---

## Category 1: Internal Types That Need Public Exposure

Types that exist internally but are not accessible to NuGet consumers.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 1.1 | Expose font metadata reading without generation | `TtfFontReader` is `internal`. The UI needs to read `FontInfo` from font bytes without going through the full generation pipeline — to populate font name, metrics, character coverage, and metadata display immediately on font load. **Options**: (a) Make `TtfFontReader` public, (b) Add a `BmFont.ReadFontInfo(byte[] fontData)` static method that delegates internally, (c) Expose `IFontReader` publicly with a factory. Option (b) is preferred — it keeps the internal parser hidden while providing a clean entry point. | High | Phase 60, 61 |
| 1.2 | Expose atlas size estimation | `AtlasSizeEstimator` is `internal static`. The UI wants to show estimated atlas dimensions before generation starts (Phase 61 task 7.2, Phase 63 texture config). **Options**: (a) Add `BmFont.EstimateAtlasSize(FontGeneratorOptions)` static method, (b) Make `AtlasSizeEstimator` public. Option (a) is preferred — keeps the estimator internal while exposing a high-level method. Should return width, height, and estimated page count. | Medium | Phase 61, 63 |

---

## Category 2: Missing Builder Methods

`BmFontBuilder` fluent methods that are missing for options the UI needs to configure.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 2.1 | Add `WithCollectMetrics(bool)` | `FontGeneratorOptions.CollectMetrics` exists but has no builder fluent method. The UI needs `builder.WithCollectMetrics(true)` to enable pipeline timing display in the status bar and metrics panel. Straightforward addition. | High | Phase 63 |
| 2.2 | ~~Add `WithSdfSpread(int)`~~ | **Deferred** — add when SDF spread UI control is needed. | ~~Medium~~ | ~~Phase 66~~ |
| 2.3 | ~~Add `WithOutputFormat(OutputFormat)` or document design decision~~ | **Deferred** — current auto-detection works fine. | ~~Low~~ | ~~Phase 63~~ |
| 2.4 | ~~Add `WithAdaptivePaddingFactor(float)`~~ | **Deferred** — not needed for current use cases. | ~~Low~~ | ~~Phase 63~~ |
| 2.5 | ~~Add `WithBitDepth(int)`~~ | **Deferred** — auto-detection handles grayscale vs RGBA. | ~~Low~~ | ~~Phase 63~~ |
| 2.6 | ~~Add texture compression options~~ | **Deferred** — PNG default is sufficient. | ~~Low~~ | ~~Phase 63~~ |

---

## Category 3: Missing Data on FontInfo / Sub-types

Standard OpenType table fields that the parsers currently skip but the UI needs for font metadata display.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 3.1 | ~~Expand `Os2Metrics` with additional fields~~ | **Not needed** — font authoring metadata, not relevant to bitmap font generation. | ~~Medium~~ | ~~Phase 61~~ |
| 3.2 | ~~Expand `NameInfo` with additional name records~~ | **Not needed** — font authoring metadata, not relevant to bitmap font generation. | ~~Medium~~ | ~~Phase 61~~ |
| 3.3 | ~~Expand `HheaTable` with additional fields~~ | **Not needed** — font authoring metadata, not relevant to bitmap font generation. | ~~Low~~ | ~~Phase 61~~ |
| 3.4 | ~~Expand `HeadTable` with additional fields and DateTime helpers~~ | **Not needed** — font authoring metadata, not relevant to bitmap font generation. | ~~Low~~ | ~~Phase 61~~ |
| 3.5 | ~~Add CPAL palette data to `FontInfo`~~ | **Deferred** — color font rendering works without exposing palette. | ~~Medium~~ | ~~Phase 66~~ |
| 3.6 | ~~Add `.ttc` face enumeration API~~ | **Deferred** — not blocking any current feature. | ~~Medium~~ | ~~Phase 66~~ |

---

## Category 4: Cancellation and Progress Reporting

The UI runs generation on a background thread and needs to cancel or report progress.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 4.1 | Add `CancellationToken` to `BmFontBuilder.Build()` | Generation can take seconds for large character sets (1000+ glyphs with effects and supersampling). The UI needs to cancel in-progress generation when the user changes settings (debounced re-generation), closes the preview, or exits the app. Currently the only option is to let generation complete and discard the result. **Proposed API**: `builder.Build(CancellationToken cancellationToken = default)`. Internally, check the token between pipeline stages and throw `OperationCanceledException`. | High | Phase 60, 64, 68 |
| 4.2 | ~~Add `CancellationToken` to `BmFont.GenerateBatch()`~~ | **Deferred** — generation completes in milliseconds; not needed until performance becomes a concern. | ~~High~~ | ~~Phase 66~~ |
| 4.3 | ~~Add progress reporting to generation pipeline~~ | **Deferred** — generation completes in milliseconds. | ~~Medium~~ | ~~Phase 64, 66, 68~~ |

---

## Category 5: API Refinements

Existing API surface that needs adjustment for UI use cases.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 5.1 | ~~Support supplementary plane fallback characters~~ | **Deferred** — edge case for emoji fallback. | ~~Low~~ | ~~Phase 66~~ |
| 5.2 | ~~Enable `.bmfc` project save without prior generation~~ | **Complete** — already implemented via `ProjectService.SaveProject()`. | ~~Medium~~ | ~~Phase 65~~ |
| 5.3 | Document or add raw pixel data accessor on atlas pages | `AtlasPage.PixelData` is expected to contain raw RGBA pixels for `Texture2D.SetData<byte>()` in MonoGame. `BmFontResult.GetPngData()` returns encoded PNG bytes (not suitable for GPU upload). **Action items**: (a) Verify that `AtlasPage.PixelData` is indeed raw RGBA byte array (width * height * 4 bytes), (b) If so, add XML documentation clarifying the format (RGBA, row-major, top-to-bottom, premultiplied or straight alpha), (c) If not raw RGBA, add a `GetRawPixelData(int pageIndex)` method that returns unencoded pixels. | High | Phase 64, 68 |
| 5.4 | Document `PipelineMetrics` stage names | UI phase plans reference stage names that may be incorrect. The actual stages reported by `PipelineMetrics` need to be documented so the UI can map them to display labels. **Known stages**: FontParsing, CharsetResolution, Rasterization, EffectsCompositing, PostProcessing, SuperSampleDownscale, CellEqualization, AtlasSizeEstimation, AtlasPacking, AtlasEncoding, ModelAssembly, Total. **Action**: (a) Verify this list against the implementation, (b) Add XML docs on the `PipelineMetrics` class listing all possible stage keys, (c) Consider adding a `StageNames` static property or enum. | Low | Phase 63 |

---

## Implementation Order

Ordered by priority and dependency. High-priority items block UI Phase 60.

### Must-Have Before Phase 60 (UI MVP)

| Item | Category | Effort |
|------|----------|--------|
| 1.1 — Public font metadata reading | Exposure | Small |
| 4.1 — CancellationToken on Build() | Cancellation | Medium |
| 5.3 — Raw pixel data verification/docs | API | Small |

### Must-Have Before Phase 63 (Atlas/Texture Config)

| Item | Category | Effort |
|------|----------|--------|
| 1.2 — Public atlas size estimation | Exposure | Small |
| 2.1 — WithCollectMetrics builder method | Builder | Small |
| 5.4 — PipelineMetrics stage docs | API | Small |

### ~~Must-Have Before Phase 64 (Preview/Visualization)~~

| Item | Category | Status |
|------|----------|--------|
| ~~4.3 — Progress reporting~~ | ~~Cancellation~~ | Deferred |

### ~~Must-Have Before Phase 65 (Project File Operations)~~

| Item | Category | Status |
|------|----------|--------|
| ~~5.2 — Standalone .bmfc save~~ | ~~API~~ | Complete |

### ~~Must-Have Before Phase 66 (Advanced Features)~~

| Item | Category | Status |
|------|----------|--------|
| ~~2.2 — WithSdfSpread builder method~~ | ~~Builder~~ | Deferred |
| ~~3.5 — CPAL palette data on FontInfo~~ | ~~Data~~ | Deferred |
| ~~3.6 — .ttc face enumeration~~ | ~~Data~~ | Deferred |
| ~~4.2 — CancellationToken on GenerateBatch~~ | ~~Cancellation~~ | Deferred |
| ~~5.1 — Supplementary plane fallback~~ | ~~API~~ | Deferred |

### ~~Nice-to-Have (implement if time permits)~~

| Item | Category | Status |
|------|----------|--------|
| ~~2.3 — WithOutputFormat~~ | ~~Builder~~ | Deferred |
| ~~2.4 — WithAdaptivePaddingFactor~~ | ~~Builder~~ | Deferred |
| ~~2.5 — WithBitDepth~~ | ~~Builder~~ | Deferred |
| ~~2.6 — Compression options~~ | ~~Builder~~ | Deferred |
| ~~3.1 — Os2Metrics expansion~~ | ~~Data~~ | Not needed |
| ~~3.2 — NameInfo expansion~~ | ~~Data~~ | Not needed |
| ~~3.3 — HheaTable expansion~~ | ~~Data~~ | Not needed |
| ~~3.4 — HeadTable expansion~~ | ~~Data~~ | Not needed |

---

## Estimated Effort by Category

| Category | Items | Status | Notes |
|----------|-------|--------|-------|
| Internal to Public exposure | 2 | Complete | 1.1, 1.2 implemented |
| Missing builder methods | 6 | 1 complete, 5 deferred | 2.1 complete; 2.2-2.6 deferred |
| FontInfo data expansion | 6 | 4 not needed, 2 deferred | 3.1-3.4 not needed; 3.5-3.6 deferred |
| Cancellation and Progress | 3 | 1 complete, 2 deferred | 4.1 complete; 4.2-4.3 deferred |
| API refinements | 4 | 2 complete, 2 deferred | 5.2-5.3 complete (5.3 docs, 5.4 docs); 5.1 deferred |
| **Total** | **21 items** | **7 complete, 4 not needed, 10 deferred** | |

---

## Success Criteria

- All **High** priority items are implemented and tested before UI Phase 60 begins.
- **Medium** priority items are implemented before the phase that needs them (see Implementation Order above).
- **Low** priority items are documented with workarounds if not implemented in time.
- No breaking changes to the existing public API surface.
- All new public API has XML documentation with `<summary>`, `<param>`, and `<returns>` tags.
- Tests cover all new functionality with both positive and edge cases.
- `PipelineMetrics` stage names are verified against the implementation and documented.

---

## Dependencies

- **Phase 21 findings** (code review): Items H3 (codepoint truncation) and Q1 (QueryAtlasSizeCore duplication) overlap with items 5.1 and 1.2 respectively. Coordinate fixes.
- **Phase 50** (layer retention): Independent of this phase. Can proceed in parallel.
- **Phase 30** (WASM rasterization): Independent. CancellationToken support (4.1) should work with both FreeType and future WASM rasterizers.

---

## Open Questions

1. **Item 2.3 (OutputFormat on builder)**: Is output format a generation-time or write-time concern? If generation-time, should it affect encoding (e.g., skip PNG encoding if output will be DDS)? Decision needed before implementation.
2. **Item 2.5 (BitDepth)**: What does "8-bit" mean for KernSmith? 8-bit grayscale (single channel) or 8-bit indexed color? BMFont uses it to mean grayscale vs full color. Need to define semantics.
3. **Item 3.5 (CPAL data)**: Should color palette data live on `FontInfo` directly or on a separate `ColorFontInfo` sub-object? If the font has no color glyphs, the sub-object would be null.
4. **Item 4.3 (Progress)**: Should progress be reported per-glyph during rasterization, or only at stage boundaries? Per-glyph gives smoother progress bars but adds overhead from `IProgress<T>.Report()` calls on every glyph.
