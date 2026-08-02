# Phase 165 — Native Rasterizer: IRasterizer Integration & Metrics

> **Status**: **COMPLETE** (2026-08-01)
> **Created**: 2026-04-01
> **Depends on**: Phase 161 (core table parsers), Phase 164 (scanline rasterizer core)

> **Note**: Phase 161's core table parsers (`HeadTable`, `HheaTable`, `HmtxTable`, `Os2Table`, `CmapTable`, `MaxpTable` in `src/KernSmith.Rasterizers.Native/Internal/Tables/`) are already implemented and available for integration. Phases 162-164 are now **complete**, so nothing blocks the `IRasterizer` wiring below.

> **Input contract from Phase 164** (complete — see `done/phase-164-scanline-rasterizer.md`):
> - The entry point is `ScanlineRasterizer.Rasterize(EdgeSegment[] edges, int width, int height, float originX = 0f, float originY = 0f, AntiAliasMode antiAlias = AntiAliasMode.Grayscale, int bearingX = 0, int bearingY = 0)`. `originX`/`originY` name the **pixel-space point that maps to bitmap (0, 0)**, so this phase places the glyph box by passing the box's top-left rather than allocating a translated `EdgeSegment[]` per glyph.
> - **This phase owns bitmap sizing and padding.** The rasterizer allocates exactly `width * height` and never adds a border of its own; the 1-pixel padding the Phase 164 doc mentions must come from the `width`/`height`/`origin` values passed in here.
> - **`bearingX`/`bearingY` are pass-through only** — they are copied verbatim onto `RasterResult` and never computed or validated. This phase derives them from the glyph box and baseline.
> - `RasterResult` is an internal class with `Bitmap` (fresh `new byte[]`, caller-owned, row-major, **pitch == width**, never pooled), `Width`, `Height`, `BearingX`, `BearingY`. It carries **no advance** — take that from `hmtx` when building `GlyphMetrics`.
> - **`AntiAliasMode` is the existing core `KernSmith.AntiAliasMode`**, not a Native-specific enum, so `RasterOptions.AntiAlias` passes straight through with no mapping. Only `None` is special-cased (threshold at 128); **every other value renders as grayscale**, so `SupportedAntiAliasModes = [None, Grayscale]` below must stay accurate — an unsupported mode will silently render grayscale rather than fail.
> - The rasterizer **clips** anything outside the bitmap and drops non-finite edge coordinates instead of throwing, so a mis-sized box degrades to a cropped glyph rather than an exception. Don't rely on it to surface sizing bugs; the metrics tests must.
> - ⚠️ `ScanlineRasterizerSsimTests` includes an `'O'` @ 12px case sitting at **0.9509** against a 0.95 floor. Any change here that touches `OutlineFlattener` tolerance or glyph placement should re-run that suite first.

## What Shipped

| Item | Where |
|------|-------|
| Full `IRasterizer`: `LoadFont` (CFF rejection), `RasterizeGlyph`, `RasterizeAll`, `GetGlyphMetrics`, `GetFontMetrics`, `GetKerningPairs` (returns `null`) | `src/KernSmith.Rasterizers.Native/NativeRasterizer.cs` |
| Bitmap sizing from the glyph's `glyf` header bounds (tight, curve extrema included), falling back to `GlyphOutline`'s control-hull box only when the header box is degenerate | `Internal/Raster/GlyphBox.cs` (new) |
| `Dictionary<int, int>` cmap memo (the FontStashSharp `Int32Map` optimization is explicitly deferred) | `Internal/NativeFontFace.cs` |
| Explicit `HandlesOwnSizing => false` | `NativeCapabilities.cs` |
| Metrics agreement vs FreeType, end-to-end BMFont generation, glyph-box unit tests | `tests/KernSmith.Rasterizers.Native.Tests/NativeMetricsAgreementTests.cs`, `NativeEndToEndTests.cs`, `GlyphBoxTests.cs` |
| Synthetic OTTO/`CFF ` font builder (assembled from Roboto's real tables — the repo has no CFF fixture) used to test the CFF rejection path | `tests/KernSmith.Rasterizers.Native.Tests/SyntheticFonts.cs` |
| `NativeRasterizerTests.cs` rewritten from stub-assertions to real behavior; Native test project now **170 per TFM** | `tests/KernSmith.Rasterizers.Native.Tests/` |
| `ProjectReference` to `KernSmith.Rasterizers.FreeType` as the metrics baseline (**test-only**) | `tests/KernSmith.Rasterizers.Native.Tests/KernSmith.Rasterizers.Native.Tests.csproj` |

Pipeline as shipped: cmap → `glyf` → `OutlineExtractor` → `GlyphBox` → `OutlineFlattener` (baseline placed on row 0) → `ScanlineRasterizer`. Output is `PixelFormat.Grayscale8` with `Pitch == Width`.

**Beyond the phase proper**, the now-functional backend was surfaced to users: the CLI (`--rasterizer native`), the docfx site (new `docs/rasterizers/native.md` + TOC entries), `README.md`, `COMPARISON.md`, `CHANGELOG.md`, `reference/REF-12-rasterizer-backends.md`, and the `tests/bmfont-compare` harness (Native is now a comparison column).

**Deviations from the plan as written**

- **`GetFontMetrics` uses OS/2 win metrics, not `hhea`** — a deliberate departure from the StbTrueType precedent and from this doc's "Ascent, Descent, LineGap (from hhea)" line. With `hhea`, end-to-end `lineHeight` was **29 vs FreeType's 33** at 32px. FreeType returns `null` from `GetFontMetrics`, so the shared pipeline's OS/2 `usWinAscent`/`usWinDescent` path is what the other backends effectively use; reading OS/2 here reproduces FreeType exactly.
- **Measured agreement with FreeType** (Roboto, 6 sizes, 94-glyph sample, tolerance forced to 0): every disagreement is exactly ±1, none larger. `bearingX` ±1 in 276 cases (always FT−1), `bearingY` ±1 in 269 (always FT+1), `advance` ±1 in 6, `width` ±1 in 2, `height` ±1 in 1. The systematic bearing offset is the expected floor/ceil bitmap-box vs round 26.6-outline-metrics difference. `.fnt` `base`/`lineHeight` match FreeType **exactly** at all six sizes.
- **`RasterizeAll` has no buffer reuse**, contrary to the "batch rasterization with buffer reuse" line in Scope. It loops `RasterizeGlyph`, matching FreeType and StbTrueType. Per-glyph bitmaps must be freshly allocated because the caller retains them (same ownership constraint Phase 164 documented for `RasterResult.Bitmap`).
- **`SuperSample` is ignored** (like FreeType — the shared pipeline renders supersampled and downscales). **`Bold`/`Italic` are ignored rather than thrown** — see the known issue below. `Sdf`/`ColorFont` throw `NotSupportedException`.
- **`Int32Map` deferred.** The cmap memo is a plain `Dictionary<int, int>`; the FontStashSharp specialization noted under "cmap Lookup" is a later optimization, not dropped.
- **Comparison-harness result**: 160/160 configs generated; Native succeeded on all 27 it was run against. Ink coverage tracks StbTrueType almost exactly (plain 12.24% vs 12.27%; fire 53.29% vs 53.23%). Glyph counts and atlas dimensions match FreeType/StbTrueType config-for-config.

## ⚠️ Known Issue Found Here — Synthetic Bold/Italic Silently No-Op on Native

> Tracked as **Issue 5** in [phase-150-deferred-rasterizer-issues.md](../phase-150-deferred-rasterizer-issues.md). Recorded here because Phase 165 is what exposed it.

`src/KernSmith/BmFont.cs:287` skips `BoldPostProcessor` / `ItalicPostProcessor` whenever `options.Bold` / `options.Italic` is set, on the assumption that the backend already applied the emboldening. It **never consults `IRasterizerCapabilities.SupportsSyntheticBold` / `SupportsSyntheticItalic`**.

All four previously shipped backends (FreeType, GDI, DirectWrite, StbTrueType) declare `SupportsSyntheticBold => true`, so the gate has been accidentally correct until now. Native is the first backend to declare `false` (synthetic bold/italic is Phase 167), which exposes the latent bug: **`Bold = true` + Native yields regular-weight glyphs with no warning or error.**

The comparison harness corroborates it — bold/italic/outlined configs show the largest deltas vs FreeType (`Font48Bauhaus_93_Italic` mean 3.93px width delta, plain-synbold 1.98px) while regular upright faces are near-identical.

**Proposed fix**: gate on the capability, not just the option — fall through to the post-processor when the backend reports it cannot do the transform itself. This is a **core shared-pixel-path change and therefore requires a `python tests/bmfont-compare/regression_check.py` run**, but it should be a **no-op for all four shipped backends** since they all report `true`.

## Goal

Wire up the scanline rasterizer to the full `IRasterizer` interface, implement font/glyph metrics, and make the Native rasterizer fully functional as a KernSmith backend for TrueType fonts.

## Scope

### IRasterizer Implementation

Complete the `NativeRasterizer` class:

- `LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex)` — parse tables, build glyph index, cache metrics
- `RasterizeGlyph(int codepoint, RasterOptions options)` — full pipeline: codepoint → glyphIndex → outline → scale → flatten → rasterize → RasterizedGlyph
- `RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)` — batch rasterization with buffer reuse
- `GetGlyphMetrics(int codepoint, RasterOptions options)` — metrics without rasterization
- `GetFontMetrics(RasterOptions options)` — ascent, descent, lineHeight from hhea/OS/2
- `GetKerningPairs(RasterOptions options)` — return null (let shared GPOS/kern parser handle it, same as StbTrueType backend)

### Font Metrics

From `hhea` and `OS/2` tables (already parsed by KernSmith core, but Native rasterizer reads its own copy):
- Ascent, Descent, LineGap (from hhea)
- WinAscent, WinDescent (from OS/2, for cell height sizing)
- Scale to pixels: `metric_pixels = metric_funits * pixelSize / unitsPerEm`
- Round to integers for final metrics

### Glyph Metrics

Per-glyph from `hmtx` and rasterized bitmap:
- `Advance`: from hmtx (scaled to pixels)
- `BearingX`: left edge of bitmap relative to origin (from glyph bbox)
- `BearingY`: top edge of bitmap relative to baseline
- `Width`, `Height`: bitmap dimensions

### cmap Lookup

- Parse cmap table to map Unicode codepoints → glyph indices
- Support Format 4 (BMP, most common) and Format 12 (full Unicode)
- Cache the lookup table (see Int32Map note below)

> **FontStashSharp insight:** FontStashSharp uses a specialized `Int32Map` (integer-keyed hash map) instead of `Dictionary<int, T>` for cmap and glyph lookups. It hashes via `key & int.MaxValue` (no virtual calls), uses static pre-computed primes for bucket sizing, and maintains a free-list for deleted entries to reduce GC pressure. This is most impactful for large glyph sets (CJK, full Unicode). Consider a similar specialized map for the cmap lookup cache.

### Size Conversion

The Native rasterizer sets `HandlesOwnSizing = false`. This means the main pipeline (`BmFont.cs`) handles all ppem/cell-height conversion before calling the rasterizer. The `RasterOptions.Size` value received by the rasterizer is already the correct pixel size — no additional conversion needed.

The Native rasterizer simply uses `RasterOptions.Size` directly as the pixel size for scaling:
```
scaleFactor = options.Size / (float)unitsPerEm
```

### IRasterizerCapabilities

```csharp
SupportsColorFonts = false           // Phase 172
SupportsVariableFonts = false        // Phase 171
SupportsSdf = false                  // Phase 169
SupportsOutlineStroke = false        // Phase 168
SupportsSyntheticBold = false        // Phase 167
SupportsSyntheticItalic = false      // Phase 167
HandlesOwnSizing = false
SupportsSystemFonts = false
SupportedAntiAliasModes = [None, Grayscale]
```

Capabilities updated as features are added in later phases.

### Registration

```csharp
[ModuleInitializer]
internal static void Register()
{
    RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());
}
```

## Testing

### Integration Tests
- Generate BMFont output using `BmFont.Generate()` with `Backend = RasterizerBackend.Native`
- Compare against FreeType and StbTrueType output for Roboto ASCII
- Verify .fnt file metrics match (ascent, descent, lineHeight, glyph positions)

### Metrics Tests  
- Font metrics: verify ascent/descent/lineHeight match FreeType within ±1 pixel
- Glyph metrics: verify advance/bearingX/bearingY match FreeType within ±1 pixel
- Multiple sizes: 12, 16, 24, 32, 48, 96 px

### End-to-End Tests
- Full pipeline: load font → rasterize ASCII → pack atlas → write BMFont
- Verify output is valid BMFont (parseable by BmFontReader)
- Visual comparison of atlas texture

### Regression Tests
- Golden master: render fixed glyphs at fixed sizes, save as reference bitmaps
- Future changes must not regress beyond SSIM threshold

## Success Criteria

- [x] `NativeRasterizer` fully implements `IRasterizer` for TrueType fonts (`LoadFont`, `RasterizeGlyph`, `RasterizeAll`, `GetGlyphMetrics`, `GetFontMetrics`, `GetKerningPairs` → `null`)
- [x] Selectable via `RasterizerBackend.Native` — and additionally surfaced in the CLI (`--rasterizer native`) and the `tests/bmfont-compare` harness
- [x] Font metrics match FreeType within ±1 pixel — `.fnt` `base`/`lineHeight` match **exactly** at 12/16/24/32/48/96 px (via the OS/2 win-metrics deviation noted above)
- [x] Glyph metrics match FreeType within ±1 pixel — every disagreement across 6 sizes × 94 glyphs is exactly ±1; see the measured-agreement bullet
- [x] Full BMFont generation works end-to-end (`NativeEndToEndTests`: load → rasterize → pack → write → re-parse with `BmFontReader`); harness generated 27/27 Native configs
- [x] CFF fonts rejected with clear `RasterizationException` — verified against a synthetic OTTO/`CFF ` font built in `SyntheticFonts.cs`
- [x] All tests pass — 170 in `KernSmith.Rasterizers.Native.Tests` (net8.0 + net10.0), 33 of them new here
- [x] No trimming/AOT warnings
- ⚠️ **Known issue surfaced, not fixed here**: synthetic bold/italic silently no-op on Native — see the section above and Phase 150 Issue 5.
