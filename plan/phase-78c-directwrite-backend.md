# Phase 78C -- DirectWrite Rasterizer Backend

> **Status**: In Progress
> **Size**: Medium-Large
> **Created**: 2026-03-25
> **Dependencies**: Phase 78A (foundation), Phase 78B recommended (proves the abstraction with GDI first)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Implement a DirectWrite-based rasterizer backend for modern Windows rendering with color font and variable font support.

---

## Overview

DirectWrite is the modern Windows text rendering API. It provides higher-quality rendering than GDI, supports color fonts (COLR/CPAL), variable fonts, and subpixel positioning. This backend uses TerraFX.Interop.Windows for .NET interop.

### Priority and Scope

**This phase is lower priority than GDI (78B).** GDI is the primary goal because it produces BMFont-identical output. DirectWrite is a nice-to-have for modern rendering features (color fonts, variable fonts, subpixel positioning). **If scope needs cutting, this phase goes first** -- before 78B.

DirectWrite does NOT produce BMFont-identical output. It uses different hinting algorithms and metrics calculations than GDI/BMFont. Users who need exact BMFont parity should use the GDI backend.

### Why TerraFX.Interop.Windows

TerraFX.Interop.Windows provides raw 1:1 COM bindings generated directly from Windows SDK metadata. It is maintained by Tanner Gooding, a member of the Microsoft .NET team, and is MIT licensed. It offers complete DirectWrite API coverage with zero abstraction overhead, making it ideal for precise COM interop. Unlike higher-level wrappers, TerraFX generates bindings mechanically so they stay in sync with the Windows SDK. The package targets net10.0 only, which aligns with our TFM. Note: because TerraFX exposes raw COM pointers, a `ComPtr<T>` helper is needed for safe COM reference-counting and lifetime management.

## Prerequisites from Phase 78A Deferrals

### Resolve FreeType-specific downcasts in `BmFont.cs`

`SetVariationAxes()` and `SelectColorPalette()` in `BmFont.cs` are currently called via `rasterizer is FreeTypeRasterizer` downcast. DirectWrite supports both variable fonts and color palettes, so these capabilities must be promoted to `IRasterizer` (as optional methods with default no-op implementations) or extracted into a new `IRasterizerConfiguration` interface BEFORE the DirectWrite backend can use them. This does NOT block Phase 78B since GDI reports `false` for both `SupportsColorFonts` and `SupportsVariableFonts`.

## Lessons from 78B/78BB

- **IRasterizer interface grew in 78BB**: DirectWrite must implement these new members added in 78BB: `GetFontMetrics(RasterOptions)` returning `RasterizerFontMetrics`, `GetKerningPairs(RasterOptions)` returning `ScaledKerningPair[]?`, `LoadSystemFont(string familyName)`, plus capabilities `HandlesOwnSizing` and `SupportsSystemFonts`. Also `SuperSample` property exists on `RasterOptions`.
- **Don't chase BMFont parity**: 78BB proved rendering path differences are architectural (8x supersample was attempted and reverted because BMFont uses GGO_NATIVE + polygon fill vs GDI's GGO_GRAY8_BITMAP). DirectWrite uses its own rendering pipeline -- don't try to match BMFont or GDI output pixel-for-pixel.
- **Default interface methods are the proven pattern**: 78BB successfully extended IRasterizer with default implementations (returning null/false) so FreeType was completely unaffected (330/330 tests pass). Use the same pattern for any DirectWrite-specific extensions.
- **Pipeline captures metrics/kerning before disposal**: BmFont.cs now calls `GetFontMetrics()` and `GetKerningPairs()` on the rasterizer and stores results before disposing it. DirectWrite should implement both -- DirectWrite's DWRITE_FONT_METRICS are high-quality and its kerning via IDWriteFontFace1.GetKerningPairAdjustments is authoritative.
- **FreeType downcast resolution pattern is proven**: 78BB added optional interface methods with defaults. Apply the same approach to promote `SetVariationAxes()` and `SelectColorPalette()` to `IRasterizer` before starting DirectWrite.

## Tasks

### 1. New Project

- Path: `src/KernSmith.Rasterizers.DirectWrite.TerraFX/KernSmith.Rasterizers.DirectWrite.TerraFX.csproj`
- TFM: `net10.0-windows` only (Windows-only, no net8.0)
- Namespace: `KernSmith.Rasterizers.DirectWrite.TerraFX`
- Separate NuGet package
- Dependencies: `TerraFX.Interop.Windows`
- References `KernSmith` core library

### 2. Implement `DirectWriteRasterizer : IRasterizer`

Core rasterizer class implementing the full `IRasterizer` interface.

### 3. Font Loading from Bytes

Load font from `ReadOnlyMemory<byte>` via one of:
- `IDWriteFactory.CreateFontFileReference` with a custom `IDWriteFontFileStream`
- Custom `IDWriteFontFileLoader` implementation that wraps the byte buffer
- In-memory font collection via `IDWriteFactory.CreateCustomFontCollection`

The approach must avoid writing temp files to disk.

### 4. Bitmap Rasterization

- Create `ID2D1BitmapRenderTarget` (WIC bitmap render target) at the glyph dimensions
- Use `ID2D1RenderTarget.DrawGlyphRun` to rasterize individual glyphs
- Extract pixel data from the bitmap render target into `RasterizedGlyph.BitmapData`
- Handle coordinate system differences (DirectWrite Y-up vs bitmap Y-down)

### 5. Color Font Support (COLR/CPAL)

- Use `IDWriteFactory4.TranslateColorGlyphRun` to decompose color glyphs into layered runs
- Render each color layer with appropriate brush colors from CPAL palette
- Composite layers into final Rgba32 bitmap

### 6. Variable Font Support

- Use `IDWriteFontFace5` (or later) to access font variation axes
- Apply axis values from `FontGeneratorOptions.VariationAxes`
- Query available axes via `IDWriteFontFace5.GetFontAxisValues`

### 7. Subpixel Positioning

- DirectWrite supports subpixel glyph positioning natively
- Map to `RasterOptions` settings where applicable
- Three measuring modes available: Natural, GDI Classic, GDI Natural

### 8. Implement `IRasterizerCapabilities`

Report DirectWrite capabilities:
- `SupportsColorFonts`: true
- `SupportsVariableFonts`: true
- `SupportsSdf`: false (use FreeType for SDF)
- `SupportsOutlineStroke`: false (reuse post-processor pipeline)
- `SupportedAntiAliasModes`: `[None, Normal]` (ClearType excluded from atlas output)

### 9. Effects Pipeline

Same as GDI backend: DirectWrite produces compatible `RasterizedGlyph` output with correct metrics. Existing post-processor pipeline handles outline, shadow, and gradient effects.

### 10. Static Registration with Factory

```csharp
RasterizerFactory.Register(RasterizerBackend.DirectWrite, () => new DirectWriteRasterizer());
```

### 11. Implement 78BB IRasterizer Members

- Implement `GetFontMetrics(RasterOptions)` using DWRITE_FONT_METRICS (ascent, descent, lineGap, unitsPerEm, etc.)
- Implement `GetKerningPairs(RasterOptions)` using IDWriteFontFace1.GetKerningPairAdjustments
- Implement `LoadSystemFont(string familyName)` using IDWriteFactory.GetSystemFontCollection
- Set `HandlesOwnSizing = true` (DirectWrite handles its own sizing like GDI does)
- Set `SupportsSystemFonts = true`
- Handle `SuperSample` from RasterOptions if applicable to DirectWrite rendering

### 12. Disposal

- Release `IDWriteFactory`, `IDWriteFontFace`, `ID2D1Factory` COM objects
- Release any custom font loaders/streams
- Use `ComPtr<T>` helper to ensure deterministic release of all COM references

## Files Created/Changed

| File | Change |
|------|--------|
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/KernSmith.Rasterizers.DirectWrite.TerraFX.csproj` | New project file |
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteRasterizer.cs` | New -- main rasterizer implementation |
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteFontLoader.cs` | New -- custom font file loader for in-memory fonts |
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/DirectWriteRegistration.cs` | New -- factory registration |
| `src/KernSmith/Rasterizer/IRasterizer.cs` | Promote `SetVariationAxes()` and `SelectColorPalette()` from FreeType downcasts to interface methods with default no-op implementations |

## Comparison Tools

Multi-backend visual comparison tools for validating output across rasterizers:
- `tests/bmfont-compare/GenerateAll/` -- generates atlas PNGs + .fnt files from FreeType, GDI, and DirectWrite with fire-effect and plain configs. Usage: `dotnet run --framework net10.0-windows -- <output-dir>`
- `tests/bmfont-compare/CompareGlyphs/` -- extracts individual glyphs using .fnt coordinates and produces side-by-side comparison PNGs (comparison.png, comparison2.png). Also compares against BMFont64 if its output is present. Usage: `dotnet run --framework net10.0-windows -- <data-dir>`

## Testing

- **Windows-only CI runner** required
- **Color font rendering**: test with a COLR/CPAL font, verify colored glyph output
- **Variable font rendering**: test with a variable font, verify axis application
- **Metrics comparison**: compare against FreeType and GDI for the same font/size
- **Golden image comparison**: where possible, compare rendered output against reference images
- **Lifecycle**: verify all COM objects are properly released in `Dispose`
- **Edge cases**: missing glyphs, emoji/color glyphs, very large/small sizes

## Reference

### DirectWrite from .NET via TerraFX.Interop.Windows

TerraFX provides raw 1:1 COM bindings generated from Windows SDK metadata:
- Complete DirectWrite API coverage (IDWriteFactory, IDWriteFontFace, etc.)
- Complete Direct2D API coverage (ID2D1Factory, ID2D1BitmapRenderTarget, etc.)
- Three measuring modes: Natural, GDI Classic, GDI Natural
- Color font support via `IDWriteFactory4` and later interfaces
- Variable font support via `IDWriteFontFace5` and later interfaces
- Requires a `ComPtr<T>` helper for COM lifetime management (prevent leaks)
