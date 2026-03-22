# Phase 55 — Core Library Prerequisites for UI

> **Status**: Partial — High/Medium priority items complete. CancellationToken, progress reporting, and Nice-to-Have items deferred.
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
| 2.2 | Add `WithSdfSpread(int)` | `ExtendedMetadata.SdfSpread` exists in the output model but there is no way to configure the SDF spread radius via the builder. `FontGeneratorOptions` has no `SdfSpread` property either. **Requires**: (a) Add `SdfSpread` property to `FontGeneratorOptions`, (b) Wire it through the SDF pipeline, (c) Add `WithSdfSpread(int)` to builder. The UI wants a spread slider (1-32 range). | Medium | Phase 66 |
| 2.3 | Add `WithOutputFormat(OutputFormat)` or document design decision | Output format (Text/XML/Binary) is currently a write-time concern passed to `BmFontResult.ToFile()`, not a generation-time concern. The builder cannot set it. **Resolution options**: (a) Add `OutputFormat` to `FontGeneratorOptions` and builder so config is fully self-contained — the UI can save/load complete project state from a single options object, (b) Accept this as by-design and have the UI store output format separately. **Recommendation**: Option (a) for UI ergonomics. The format property on options would default to `Text` and be used by `ToFile()` when no explicit format is passed. | Low | Phase 63 |
| 2.4 | Add `WithAdaptivePaddingFactor(float)` | BMFont has an "Adaptive padding factor" in its export options that adjusts glyph padding proportionally to glyph size. KernSmith has no equivalent. **Requires**: (a) Add `AdaptivePaddingFactor` to `FontGeneratorOptions`, (b) Wire through to padding calculation during rasterization, (c) Add builder method. Needed for BMFont parity. | Low | Phase 63 |
| 2.5 | Add `WithBitDepth(int)` | BMFont supports 8-bit and 32-bit texture output. KernSmith always outputs 32-bit RGBA or 8-bit grayscale based on context (SDF → grayscale, otherwise → RGBA) but has no explicit bit depth control. **Requires**: Define what bit depth means for KernSmith — is it channel depth (8 vs 16 per channel) or total depth (8-bit indexed vs 32-bit RGBA)? Likely the latter. | Low | Phase 63 |
| 2.6 | Add texture compression options | BMFont supports DDS compression (BC1/BC3/BC5) and PNG compression levels. KernSmith's `DdsEncoder` and `PngEncoder` have no compression configuration. **Requires**: (a) Add `DdsCompression` enum (None/BC1/BC3/BC5), (b) Add `PngCompressionLevel` property, (c) Wire through encoders, (d) Add builder methods. | Low | Phase 63 |

---

## Category 3: Missing Data on FontInfo / Sub-types

Standard OpenType table fields that the parsers currently skip but the UI needs for font metadata display.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 3.1 | Expand `Os2Metrics` with additional fields | **Current fields**: WeightClass, WidthClass, TypoAscender, TypoDescender, TypoLineGap, WinAscent, WinDescent, XHeight, CapHeight, Panose, FirstCharIndex, LastCharIndex. **Missing fields needed for UI**: `XAvgCharWidth` (average character width), `SubscriptSizeX`, `SubscriptSizeY`, `SuperscriptSizeX`, `SuperscriptSizeY`, `StrikeoutSize`, `StrikeoutPosition`. All are standard OS/2 table fields present in the binary data — just not currently parsed. | Medium | Phase 61 |
| 3.2 | Expand `NameInfo` with additional name records | **Current fields**: FontFamily, FontSubfamily, FullName, PostScriptName, Copyright, Trademark. **Missing name IDs**: UniqueId (3), Version (5), Manufacturer (8), Designer (9), Description (10), License (13), LicenseUrl (14). These are standard name table records. The parser already reads the name table — just needs to extract additional IDs. | Medium | Phase 61 |
| 3.3 | Expand `HheaTable` with additional fields | **Current fields**: Ascender, Descender, LineGap, AdvanceWidthMax, NumberOfHMetrics. **Missing**: `MinLeftSideBearing`, `MinRightSideBearing`, `XMaxExtent`. Standard hhea fields already present in the binary data. | Low | Phase 61 |
| 3.4 | Expand `HeadTable` with additional fields and DateTime helpers | **Current fields**: UnitsPerEm, XMin, YMin, XMax, YMax, IndexToLocFormat, Created, Modified. **Missing**: `MacStyle` (uint16 bit flags — bold, italic, underline, outline, shadow, condensed, extended), `LowestRecPPEM` (smallest readable size in pixels). **Also**: `Created` and `Modified` are stored as `long` (raw OpenType LongDateTime from 1904-01-01 epoch). Add computed `DateTime CreatedUtc` and `DateTime ModifiedUtc` properties for convenience. | Low | Phase 61 |
| 3.5 | Add CPAL palette data to `FontInfo` | `HasColorGlyphs` is a boolean flag. The UI needs richer color font data for the palette selector: palette count, palette colors (as `Color[]` arrays for swatch preview), and COLR table type detection (v0 vs v1). **Requires**: (a) Parse CPAL table (palette count + color entries), (b) Detect COLR table version, (c) Add `ColorPaletteCount`, `ColorPalettes`, and `ColrVersion` properties to `FontInfo` or a new `ColorFontInfo` sub-type. | Medium | Phase 66 |
| 3.6 | Add `.ttc` face enumeration API | To populate a face selector dropdown for TrueType Collection files, the UI needs to enumerate all faces (name + style) without loading each one via the rasterizer. **Proposed API**: `BmFont.EnumerateFaces(byte[] fontData)` returning `IReadOnlyList<FontFaceInfo>` where `FontFaceInfo` contains `Index`, `FamilyName`, `SubfamilyName`, `FullName`. For non-.ttc files, returns a single-element list. | Medium | Phase 66 |

---

## Category 4: Cancellation and Progress Reporting

The UI runs generation on a background thread and needs to cancel or report progress.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 4.1 | Add `CancellationToken` to `BmFontBuilder.Build()` | Generation can take seconds for large character sets (1000+ glyphs with effects and supersampling). The UI needs to cancel in-progress generation when the user changes settings (debounced re-generation), closes the preview, or exits the app. Currently the only option is to let generation complete and discard the result. **Proposed API**: `builder.Build(CancellationToken cancellationToken = default)`. Internally, check the token between pipeline stages and throw `OperationCanceledException`. | High | Phase 60, 64, 68 |
| 4.2 | Add `CancellationToken` to `BmFont.GenerateBatch()` | Batch generation of multiple fonts cannot be cancelled. `BatchOptions` has no `CancellationToken` property. **Proposed API**: Add `CancellationToken` parameter to `GenerateBatch()` and `GenerateBatchCombined()`. Check between font jobs and between pipeline stages within each job. | High | Phase 66 |
| 4.3 | Add progress reporting to generation pipeline | No `IProgress<T>` parameter or callback mechanism on `Build()` or `GenerateBatch()`. The UI can currently only show "Generating..." and "Done" — no per-stage or percentage progress. **Proposed API**: `builder.Build(IProgress<GenerationProgress>? progress = null, CancellationToken ct = default)` where `GenerationProgress` contains `Stage` (enum), `StageDescription` (string), `Percentage` (0.0-1.0), and `GlyphsProcessed` / `TotalGlyphs` (for rasterization stage). **Pipeline stages to report**: FontParsing, CharsetResolution, Rasterization, EffectsCompositing, PostProcessing, SuperSampleDownscale, CellEqualization, AtlasSizeEstimation, AtlasPacking, AtlasEncoding, ModelAssembly. | Medium | Phase 64, 66, 68 |

---

## Category 5: API Refinements

Existing API surface that needs adjustment for UI use cases.

| # | Task | Details | Priority | Needed By |
|---|------|---------|----------|-----------|
| 5.1 | Support supplementary plane fallback characters | `WithFallbackCharacter(char)` takes `char` which is 16-bit (BMP only). Characters above U+FFFF (supplementary plane, e.g., emoji) cannot be set as the fallback character. **Options**: (a) Add `WithFallbackCodepoint(int codepoint)` overload alongside the existing method, (b) Change the existing method signature to `int` (breaking change — avoid). Option (a) is preferred. | Low | Phase 66 |
| 5.2 | Enable `.bmfc` project save without prior generation | `BmFontResult.ToBmfc()` throws if `SourceOptions` is null. The UI needs to save project configuration as a `.bmfc` file without generating first — the user configures settings, saves the project, and may generate later. **Options**: (a) Add standalone `BmfcConfigWriter.Write(FontGeneratorOptions, Stream)` public method (may already exist internally), (b) Add `BmFont.Builder().ToBmfcConfig()` that builds a `BmfcConfig` from the builder state without running generation, (c) Ensure `BmfcConfig.FromOptions(FontGeneratorOptions)` is public. | Medium | Phase 65 |
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

### Must-Have Before Phase 64 (Preview/Visualization)

| Item | Category | Effort |
|------|----------|--------|
| 4.3 — Progress reporting | Cancellation | Large |

### Must-Have Before Phase 65 (Project File Operations)

| Item | Category | Effort |
|------|----------|--------|
| 5.2 — Standalone .bmfc save | API | Small |

### Must-Have Before Phase 66 (Advanced Features)

| Item | Category | Effort |
|------|----------|--------|
| 2.2 — WithSdfSpread builder method | Builder | Medium |
| 3.5 — CPAL palette data on FontInfo | Data | Medium |
| 3.6 — .ttc face enumeration | Data | Medium |
| 4.2 — CancellationToken on GenerateBatch | Cancellation | Small |
| 5.1 — Supplementary plane fallback | API | Small |

### Nice-to-Have (implement if time permits)

| Item | Category | Effort |
|------|----------|--------|
| 2.3 — WithOutputFormat | Builder | Small |
| 2.4 — WithAdaptivePaddingFactor | Builder | Medium |
| 2.5 — WithBitDepth | Builder | Medium |
| 2.6 — Compression options | Builder | Medium |
| 3.1 — Os2Metrics expansion | Data | Small |
| 3.2 — NameInfo expansion | Data | Small |
| 3.3 — HheaTable expansion | Data | Small |
| 3.4 — HeadTable expansion | Data | Small |

---

## Estimated Effort by Category

| Category | Items | Effort | Notes |
|----------|-------|--------|-------|
| Internal to Public exposure | 2 | Small | Thin public wrappers around existing internals |
| Missing builder methods | 6 | Medium | 2.1 is trivial; 2.2 needs pipeline work; 2.4-2.6 need new engine features |
| FontInfo data expansion | 6 | Medium | Parser changes for 3.1-3.4 are mechanical; 3.5-3.6 need new table parsers |
| Cancellation and Progress | 3 | Large | 4.1-4.2 require token threading through pipeline; 4.3 requires progress hooks at each stage |
| API refinements | 4 | Small-Medium | Mostly documentation and small additions |
| **Total** | **21 items** | | |

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
