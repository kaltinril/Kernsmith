# Writing a Custom Rasterizer Backend

KernSmith's rasterizer system is pluggable. Any library that implements two interfaces — `IRasterizer` and `IRasterizerCapabilities` — and registers with `RasterizerFactory` works as a backend. This guide shows you how.

## Quick Start

The fastest way to get started is to copy the example project:

```bash
cp -r samples/KernSmith.Rasterizer.Example MyRasterizer
```

This gives you a skeleton project with all required interfaces stubbed out. See the example's [README](../../samples/KernSmith.Rasterizer.Example/README.md) for a step-by-step customization guide.

## The Plugin Contract

Your backend implements two interfaces from `KernSmith.Rasterizer`:

### IRasterizer

**Required members** (must implement):

| Member | Purpose |
|--------|---------|
| `Capabilities` | Returns your `IRasterizerCapabilities` instance |
| `LoadFont(ReadOnlyMemory<byte>, int)` | Loads a font from raw file bytes |
| `RasterizeGlyph(int, RasterOptions)` | Renders one glyph to a bitmap. Return `null` for missing glyphs |
| `RasterizeAll(IEnumerable<int>, RasterOptions)` | Renders multiple glyphs, skipping missing ones |
| `Dispose()` | Releases native resources |

**Optional members** (have default implementations — override only if your engine provides native support):

| Member | Default | Purpose |
|--------|---------|---------|
| `LoadSystemFont(string)` | Throws `NotSupportedException` | Load a system font by family name |
| `GetGlyphMetrics(int, RasterOptions)` | Returns `null` | Get metrics without rasterizing |
| `GetFontMetrics(RasterOptions)` | Returns `null` | Provide native font-wide metrics (ascent, descent, line height) |
| `GetKerningPairs(RasterOptions)` | Returns `null` | Provide pre-scaled kerning pairs |
| `SetVariationAxes(IReadOnlyList<VariationAxis>, Dictionary<string, float>)` | No-op | Apply variable font axis values |
| `SelectColorPalette(int)` | No-op | Select a CPAL color palette |

When optional methods return `null`, KernSmith falls back to its built-in TTF table parsers.

### IRasterizerCapabilities

**Required members:**

| Member | Type | Purpose |
|--------|------|---------|
| `SupportsColorFonts` | `bool` | Can render COLR/CPAL/sbix/CBDT color glyphs |
| `SupportsVariableFonts` | `bool` | Can apply fvar axis coordinates |
| `SupportsSdf` | `bool` | Can produce signed distance field output |
| `SupportsOutlineStroke` | `bool` | Can stroke glyph outlines |
| `SupportedAntiAliasModes` | `IReadOnlyList<AntiAliasMode>` | Which AA modes are supported (`None`, `Grayscale`, `Light`, `Lcd`) |

**Optional members** (default to `false`):

| Member | Purpose |
|--------|---------|
| `HandlesOwnSizing` | Set to `true` if your engine handles point-to-pixel conversion internally |
| `SupportsSystemFonts` | Set to `true` if you implement `LoadSystemFont()` |

## RasterizedGlyph Output

Each rasterized glyph must provide all 8 properties:

```csharp
new RasterizedGlyph
{
    Codepoint = codepoint,
    GlyphIndex = glyphIndex,       // Internal font glyph index
    BitmapData = pixelBytes,       // Grayscale (1 bpp) or RGBA (4 bpp)
    Width = bitmapWidth,
    Height = bitmapHeight,
    Pitch = bytesPerRow,           // May include row padding
    Metrics = new GlyphMetrics(bearingX, bearingY, advance, width, height),
    Format = PixelFormat.Grayscale8  // or PixelFormat.Rgba32
};
```

## Registration

Backends register with `RasterizerFactory` via a `[ModuleInitializer]`. This runs automatically when your assembly loads — no manual wiring needed.

Since `RasterizerBackend` is an enum in the core assembly, third-party backends use a numeric cast. Pick a value of 100 or higher to avoid collisions with built-in backends (FreeType=0, Gdi=1, DirectWrite=2):

```csharp
using System.Runtime.CompilerServices;
using KernSmith;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.MyRasterizer;

internal static class MyRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register((RasterizerBackend)100, () => new MyRasterizer());
    }
#pragma warning restore CA2255
}
```

Built-in backends are auto-discovered by `RasterizerFactory` on first access. For third-party backends, the `[ModuleInitializer]` fires when your assembly loads. If your assembly isn't loaded automatically (e.g., nothing in the app references a type from it), add a direct type reference or call `RuntimeHelpers.RunModuleConstructor()`:

```csharp
// Only needed if .NET doesn't load your assembly automatically
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
    typeof(KernSmith.Rasterizers.MyRasterizer.MyRasterizer).Module.ModuleHandle);
```

## Packaging

Structure your NuGet package like this:

- Package ID: `KernSmith.Rasterizers.YourName`
- Dependency: `KernSmith` (the core library)
- TFM: `net10.0` for cross-platform, or `net10.0-windows` / `net10.0-linux` if platform-specific

No special metapackage or framework reference is needed.

## Testing

Validate your backend against FreeType reference output:

1. Load a test font (e.g., Roboto-Regular.ttf) with both FreeType and your backend
2. Rasterize the same character set at the same size
3. Compare glyph metrics (advance, bearings) — small rounding differences (±1px) are normal
4. Compare bitmap output visually or via pixel diff

Use `RasterizerFactory.IsRegistered()` to verify your module initializer fires correctly:

```csharp
// Force assembly load (needed for third-party backends not in the auto-discovery list)
RuntimeHelpers.RunModuleConstructor(typeof(MyRasterizer).Module.ModuleHandle);

// Verify registration
Assert.True(RasterizerFactory.IsRegistered((RasterizerBackend)100));

// Create and use
using var rasterizer = RasterizerFactory.Create((RasterizerBackend)100);
Assert.NotNull(rasterizer.Capabilities);
```
