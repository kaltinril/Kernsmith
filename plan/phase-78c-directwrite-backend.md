# Phase 78C -- DirectWrite Rasterizer Backend

> **Status**: Planning
> **Size**: Medium-Large
> **Created**: 2026-03-25
> **Dependencies**: Phase 78A (foundation), Phase 78B recommended (proves the abstraction with GDI first)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Implement a DirectWrite-based rasterizer backend for modern Windows rendering with color font and variable font support.

---

## Overview

DirectWrite is the modern Windows text rendering API. It provides higher-quality rendering than GDI, supports color fonts (COLR/CPAL), variable fonts, and subpixel positioning. This backend uses Vortice.Windows for .NET interop.

### Priority and Scope

**This phase is lower priority than GDI (78B).** GDI is the primary goal because it produces BMFont-identical output. DirectWrite is a nice-to-have for modern rendering features (color fonts, variable fonts, subpixel positioning). **If scope needs cutting, this phase goes first** -- before 78B.

DirectWrite does NOT produce BMFont-identical output. It uses different hinting algorithms and metrics calculations than GDI/BMFont. Users who need exact BMFont parity should use the GDI backend.

### Why Vortice.Windows

Vortice.Windows is a community .NET wrapper for Windows native APIs (DirectX, Direct2D, DirectWrite). It's actively maintained and targets modern .NET. However, it's a heavy dependency (pulls in DirectX/Direct2D interop), which is a key reason this backend is an optional NuGet add-on rather than bundled in core.

## Prerequisites from Phase 78A Deferrals

### Resolve FreeType-specific downcasts in `BmFont.cs`

`SetVariationAxes()` and `SelectColorPalette()` in `BmFont.cs` are currently called via `rasterizer is FreeTypeRasterizer` downcast. DirectWrite supports both variable fonts and color palettes, so these capabilities must be promoted to `IRasterizer` (as optional methods with default no-op implementations) or extracted into a new `IRasterizerConfiguration` interface BEFORE the DirectWrite backend can use them. This does NOT block Phase 78B since GDI reports `false` for both `SupportsColorFonts` and `SupportsVariableFonts`.

## Tasks

### 1. New Project

- Path: `src/KernSmith.Rasterizers.DirectWrite/KernSmith.Rasterizers.DirectWrite.csproj`
- TFM: `net10.0-windows` (Windows-only)
- Namespace: `KernSmith.Rasterizers.DirectWrite`
- Separate NuGet package
- Dependencies: `Vortice.DirectWrite`, `Vortice.Direct2D1`
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

### 11. Disposal

- Release `IDWriteFactory`, `IDWriteFontFace`, `ID2D1Factory` COM objects
- Release any custom font loaders/streams
- Use `Dispose` pattern appropriate for COM interop (Vortice handles via `IDisposable`)

## Files Created/Changed

| File | Change |
|------|--------|
| `src/KernSmith.Rasterizers.DirectWrite/KernSmith.Rasterizers.DirectWrite.csproj` | New project file |
| `src/KernSmith.Rasterizers.DirectWrite/DirectWriteRasterizer.cs` | New -- main rasterizer implementation |
| `src/KernSmith.Rasterizers.DirectWrite/DirectWriteFontLoader.cs` | New -- custom font file loader for in-memory fonts |
| `src/KernSmith.Rasterizers.DirectWrite/DirectWriteRegistration.cs` | New -- factory registration |

## Testing

- **Windows-only CI runner** required
- **Color font rendering**: test with a COLR/CPAL font, verify colored glyph output
- **Variable font rendering**: test with a variable font, verify axis application
- **Metrics comparison**: compare against FreeType and GDI for the same font/size
- **Golden image comparison**: where possible, compare rendered output against reference images
- **Lifecycle**: verify all COM objects are properly released in `Dispose`
- **Edge cases**: missing glyphs, emoji/color glyphs, very large/small sizes

## Reference

### DirectWrite from .NET via Vortice.Windows

Vortice.Windows is actively maintained and targets .NET 9/10:
- `Vortice.DirectWrite` -- font face, metrics, glyph outlines
- `Vortice.Direct2D1` -- bitmap rasterization via render targets
- Three measuring modes: Natural, GDI Classic, GDI Natural
- Color font support via `IDWriteFactory4` and later interfaces
- Variable font support via `IDWriteFontFace5` and later interfaces
