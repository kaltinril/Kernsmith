# Phase 165 — Native Rasterizer: IRasterizer Integration & Metrics

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 161 (core table parsers), Phase 164 (scanline rasterizer core)

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
- Cache the lookup table

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

- [ ] `NativeRasterizer` fully implements `IRasterizer` for TrueType fonts
- [ ] Selectable via `RasterizerBackend.Native`
- [ ] Font metrics match FreeType within ±1 pixel
- [ ] Glyph metrics match FreeType within ±1 pixel
- [ ] Full BMFont generation works end-to-end
- [ ] CFF fonts rejected with clear `RasterizationException`
- [ ] All tests pass
- [ ] No trimming/AOT warnings
