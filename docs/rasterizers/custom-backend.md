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
| `GetFontMetrics(RasterOptions)` | Returns `null` | Provide native font-wide metrics (ascent, descent, line height). Round scaled metrics with `RasterizerFontMetrics.CeilingToPixels`, as the built-in backends do, so float noise does not add a pixel |
| `GetKerningPairs(RasterOptions)` | Returns `null` | Provide pre-scaled kerning pairs |
| `SetVariationAxes(IReadOnlyList<VariationAxis>, Dictionary<string, float>)` | No-op | Apply variable font axis values |
| `SelectColorPalette(int)` | No-op | Select a CPAL color palette |
| `RasterizeOutlineLayer(int, RasterOptions, int, OutlineShape, float)` | Returns `null` | Draw the outline ring for `OutlineMode.Vector` from the vector outline; `null` keeps core's bitmap outline for that glyph. Used only when `SupportsOutlineStroke` is true |

When optional methods return `null`, KernSmith falls back to its built-in TTF table parsers.

**Symbol fonts.** A font whose only usable `cmap` subtable is the Windows symbol one (3,0) -- Wingdings, Webdings, Symbol -- maps its glyphs at U+F020-U+F0FF. Core's parser also exposes each glyph at that byte decoded as Windows-1252, as BMFont does, so `RasterizeGlyph` may be asked for either code. When a codepoint has no glyph in such a font, look it up again at `KernSmith.Font.SymbolEncoding.ToSymbolCode(codepoint)`; `SymbolEncoding.IsSymbolFont(fontData, faceIndex)` tells you whether the font is one.

### IRasterizerCapabilities

**Required members:**

| Member | Type | Purpose |
|--------|------|---------|
| `SupportsColorFonts` | `bool` | Can render COLR/CPAL/sbix/CBDT color glyphs |
| `SupportsVariableFonts` | `bool` | Can apply fvar axis coordinates |
| `SupportsSdf` | `bool` | Can produce signed distance field output |
| `SupportsOutlineStroke` | `bool` | Implements `RasterizeOutlineLayer`, so `OutlineMode.Vector` outlines are drawn by the backend; `false` keeps core's bitmap outline effect |
| `SupportedAntiAliasModes` | `IReadOnlyList<AntiAliasMode>` | Which AA modes are supported (`None`, `Grayscale`, `Light`, `Lcd`). KernSmith hands an unlisted `Light` or `Lcd` request to the backend as `Grayscale` (when listed); `None` is always passed through. An `Lcd` request is never super sampled: the backend gets the final size and `SuperSample = 1` |

**Optional members** (default to `false`):

| Member | Purpose |
|--------|---------|
| `HandlesOwnSizing` | Set to `true` if your engine handles point-to-pixel conversion internally |
| `SupportsSystemFonts` | Set to `true` if you implement `LoadSystemFont()` |
| `SupportsSyntheticBold` | Set to `true` if your engine can apply synthetic bold (emboldening) to glyph outlines |
| `SupportsSyntheticItalic` | Set to `true` if your engine can apply synthetic italic (oblique/shear) to glyph outlines |
| `SupportsMsdf` | Set to `true` if you render `SdfMode.Msdf` / `Mtsdf` (RGBA multi-channel fields); core rejects those modes otherwise |
| `SupportsWidthScale` | Set to `true` if you honor `RasterOptions.WidthPercent`; core throws `NotSupportedException` for a non-default width otherwise |
| `SupportsItalicAngle` | Set to `true` if your synthetic italic slants by `RasterOptions.ItalicAngle`; core throws `NotSupportedException` for a non-default angle otherwise |

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

Since `RasterizerBackend` is an enum in the core assembly, third-party backends use a numeric cast. Pick a value of 100 or higher to avoid collisions with built-in backends (values 0-4 are reserved by the core enum: FreeType=0, Gdi=1, DirectWrite=2, StbTrueType=3, Native=4):

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
