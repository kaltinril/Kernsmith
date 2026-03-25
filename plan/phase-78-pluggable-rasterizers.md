# Phase 78 — Pluggable Rasterizer Backends

> **Status**: Planning
> **Created**: 2026-03-22
> **Goal**: Make the rasterizer backend swappable so users can choose FreeType, GDI, DirectWrite, or other engines.

---

## Motivation

KernSmith currently uses FreeType (via FreeTypeSharp) as its sole rasterizer. This works cross-platform but produces different output from BMFont (which uses Windows GDI). Users on Windows who need exact BMFont parity -- or prefer ClearType rendering -- should be able to select a GDI or DirectWrite backend.

Key differences between backends:
- **Hinting**: GDI/DirectWrite use Windows native hinting; FreeType uses its own interpreter (v40)
- **Anti-aliasing**: GDI supports ClearType (subpixel); FreeType supports grayscale, light, LCD
- **Metrics**: GDI uses TEXTMETRIC; FreeType uses hhea/OS2 table metrics
- **Font resolution**: GDI accesses Windows font system directly; FreeType needs file paths
- **Outline stroking**: GDI uses `GetGlyphOutline` vector data; FreeType uses `FT_Stroker`

## Current Architecture

KernSmith already has the Strategy pattern via `IRasterizer`:

```csharp
// src/KernSmith/Rasterizer/IRasterizer.cs
public interface IRasterizer : IDisposable
{
    void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0);
    RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options);
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);
    GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options);
}
```

Only one implementation exists: `FreeTypeRasterizer`.

## Design

### Rasterizer Backends

| Backend | Platform | Package | Notes |
|---------|----------|---------|-------|
| FreeType | All | `KernSmith` (built-in) | Current default, cross-platform |
| GDI | Windows | `KernSmith.Rasterizers.Gdi` | Exact BMFont parity via P/Invoke |
| DirectWrite | Windows | `KernSmith.Rasterizers.DirectWrite` | Modern Windows, best quality |
| SkiaSharp | All | `KernSmith.Rasterizers.Skia` | Future option, large dependency |
| SixLabors.Fonts | All | `KernSmith.Rasterizers.SixLabors` | Future option, pure managed |

### Capabilities Interface

Different backends support different features. Add a query mechanism:

```csharp
public interface IRasterizerCapabilities
{
    bool SupportsClearType { get; }
    bool SupportsColorFonts { get; }
    bool SupportsVariableFonts { get; }
    bool SupportsSdf { get; }
    bool SupportsOutlineStroke { get; }
    IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; }
}
```

### Factory / Selection

```csharp
public enum RasterizerBackend
{
    Auto,       // Platform-best: DirectWrite on Windows, FreeType elsewhere
    FreeType,   // Cross-platform (current default)
    Gdi,        // Windows only -- exact BMFont match
    DirectWrite // Windows only -- best quality
}

public static class RasterizerFactory
{
    public static IRasterizer Create(RasterizerBackend backend = RasterizerBackend.FreeType);
}
```

### BMFC Config Extension

Add optional `rasterizer=freetype|gdi|directwrite|auto` to `.bmfc` files. Default is `freetype` for backward compatibility.

### FontGeneratorOptions Extension

`FontGeneratorOptions` already has `public IRasterizer? Rasterizer { get; set; }` for injecting a custom instance. Add a separate `RasterizerBackend` property for enum-based selection (the factory creates the instance):
```csharp
public RasterizerBackend Backend { get; set; } = RasterizerBackend.FreeType;
```

> **Note:** The property is named `Backend` (not `Rasterizer`) to avoid conflicting with the existing `IRasterizer? Rasterizer` property. When `Rasterizer` is set (custom instance), it takes precedence over `Backend`.

## Implementation Plan

### Phase 78A: Foundation (Small)
- Add `IRasterizerCapabilities` interface
- Add `RasterizerBackend` enum
- Add `RasterizerFactory` with FreeType-only initially
- Add `Rasterizer` property to `FontGeneratorOptions`
- Update `BmFont.cs` to use factory instead of direct `FreeTypeRasterizer` construction
- Parse `rasterizer=` from .bmfc files

### Phase 78B: GDI Backend (Medium)
- New project: `KernSmith.Rasterizers.Gdi`
- Implement `GdiRasterizer : IRasterizer`
- Use P/Invoke for `CreateFont`, `GetGlyphOutline` (GGO_GRAY8_BITMAP), `GetTextMetrics`, `GetCharABCWidths`
- Handle `CreateCompatibleDC`, `SelectObject`, cleanup
- Support synthetic bold/italic via `lfWeight`/`lfItalic`
- Map `AntiAliasMode` to `ANTIALIASED_QUALITY` / `CLEARTYPE_QUALITY`
- System font resolution via GDI (no file path needed -- uses font name directly)

### Phase 78C: DirectWrite Backend (Medium-Large)
- New project: `KernSmith.Rasterizers.DirectWrite`
- Use Vortice.Windows (`Vortice.DirectWrite`, `Vortice.Direct2D1`)
- Implement `DirectWriteRasterizer : IRasterizer`
- Use `IDWriteFontFace.GetGlyphRunOutline` for vector outlines
- Use `ID2D1RenderTarget.DrawGlyphRun` for bitmap rasterization
- Support color fonts (COLR/CPAL), variable fonts, subpixel positioning

### Phase 78D: CLI and UI Integration (Small)
- CLI: Add `--rasterizer freetype|gdi|directwrite|auto` flag
- UI: Add rasterizer dropdown in font config panel
- Show capabilities (grayed-out options when backend doesn't support them)

## Reference Material

### GDI Rasterization from .NET

Key Win32 APIs via P/Invoke:
- `CreateFont()` / `CreateFontIndirectW()` -- create HFONT
- `CreateCompatibleDC()` -- device context
- `SelectObject()` -- bind font to DC
- `GetGlyphOutline()` with `GGO_GRAY8_BITMAP` -- 65-level grayscale glyph bitmaps
- `GetTextMetrics()` -- font-wide metrics (tmAscent, tmDescent, tmHeight)
- `GetCharABCWidths()` -- per-char advance widths (A + B + C spacing)
- `GLYPHMETRICS` struct -- per-glyph width, height, bearings

P/Invoke helpers: CsWin32 (Microsoft source generator) or PInvoke.Gdi32 NuGet.

### DirectWrite from .NET

Use Vortice.Windows (actively maintained, targets .NET 9/10):
- `Vortice.DirectWrite` -- font face, metrics, glyph outlines
- `Vortice.Direct2D1` -- bitmap rasterization via render targets
- Three measuring modes: Natural, GDI Classic, GDI Natural

### Prior Art

- **FontStashSharp** -- C# bitmap font library with pluggable rasterizers via `IFontLoader`. Ships three backends: StbTrueType, FreeType, SixLabors.Fonts. Each in its own NuGet package. Closest architectural reference.
- **SDL_ttf 3.0** -- Text engine abstraction for swappable rendering backends
- **Avalonia UI** -- `IDrawingContextImpl` for swappable rendering (Skia, Direct2D)
- **LayoutFarm/Typography** -- C# font renderer with pluggable glyph path builders

## Key Source Files

| What | Location |
|------|----------|
| IRasterizer interface | `src/KernSmith/Rasterizer/IRasterizer.cs` |
| FreeTypeRasterizer | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` |
| RasterOptions | `src/KernSmith/Rasterizer/RasterOptions.cs` |
| FontGeneratorOptions | `src/KernSmith/Config/FontGeneratorOptions.cs` |
| BmFont orchestration | `src/KernSmith/BmFont.cs` |
| Font metrics reference | `reference/REF-09-font-metrics-and-sizing.md` |

---

> **Review 2026-03-24**: Fixed `RasterizeAll` return type from `IEnumerable` to `IReadOnlyList` to match actual interface. Renamed proposed `Rasterizer` property to `Backend` to avoid conflict with existing `IRasterizer? Rasterizer` property on `FontGeneratorOptions`. All file paths verified.
