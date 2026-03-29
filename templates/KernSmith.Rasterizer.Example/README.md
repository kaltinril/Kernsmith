# KernSmith Rasterizer Backend Template

This directory contains a skeleton rasterizer backend that implements the KernSmith plugin contract (`IRasterizer` + `IRasterizerCapabilities`). It compiles and registers with `RasterizerFactory` but throws `NotImplementedException` from the core methods -- your job is to fill in the rendering logic.

The template contains four files:

| File | Purpose |
|------|---------|
| `MyRasterizer.cs` | `IRasterizer` implementation with all required methods and commented-out optional overrides |
| `MyRasterizerCapabilities.cs` | `IRasterizerCapabilities` implementation reporting what your backend supports |
| `MyRasterizerRegistration.cs` | `[ModuleInitializer]` that auto-registers the backend with `RasterizerFactory` |
| `KernSmith.Rasterizers.MyRasterizer.csproj` | Project file referencing the `KernSmith` NuGet package |

## How to Create Your Own Backend

Follow these steps to turn this skeleton into a working rasterizer backend.

### Step 1: Copy and Rename

Copy the entire `templates/KernSmith.Rasterizer.Example/` directory to a new location in your solution (e.g. `src/KernSmith.Rasterizers.Skia/`).

Rename the files, replacing `MyRasterizer` with your backend name:

- `MyRasterizer.cs` -> `SkiaRasterizer.cs`
- `MyRasterizerCapabilities.cs` -> `SkiaRasterizerCapabilities.cs`
- `MyRasterizerRegistration.cs` -> `SkiaRasterizerRegistration.cs`
- `KernSmith.Rasterizers.MyRasterizer.csproj` -> `KernSmith.Rasterizers.Skia.csproj`

### Step 2: Update Namespaces and Class Names

Find and replace `MyRasterizer` with your backend name throughout all files. The things to change:

- **Namespace**: `KernSmith.Rasterizers.MyRasterizer` -> `KernSmith.Rasterizers.Skia`
- **Class names**: `MyRasterizer`, `MyRasterizerCapabilities`, `MyRasterizerRegistration`
- **Constructor call** in registration: `() => new MyRasterizer()` -> `() => new SkiaRasterizer()`

### Step 3: Pick a Backend ID

The `RasterizerBackend` enum lives in the core KernSmith assembly, so third-party backends cannot add named values to it. Instead, cast an integer to the enum:

```csharp
RasterizerFactory.Register((RasterizerBackend)100, () => new SkiaRasterizer());
```

Built-in values are `FreeType=0`, `Gdi=1`, `DirectWrite=2`. Pick a unique value of 100 or higher to avoid collisions with future built-in backends. If you publish your backend as a package, document the value you chose so consumers know.

### Step 4: Implement Capabilities

Edit your capabilities class to report what your rendering engine actually supports. Each property controls whether KernSmith will call certain methods or offer certain features:

| Property | When to return `true` |
|----------|----------------------|
| `SupportsColorFonts` | Your engine can render COLR/CPAL, sbix, or CBDT/CBLC color glyphs |
| `SupportsVariableFonts` | Your engine can apply variable font axis coordinates |
| `SupportsSdf` | Your engine can produce signed distance field bitmaps |
| `SupportsOutlineStroke` | Your engine can stroke glyph outlines at a given width |
| `SupportedAntiAliasModes` | List the `AntiAliasMode` values your engine handles (at minimum `None`) |
| `HandlesOwnSizing` (optional) | Your engine interprets the `Size` field in `RasterOptions` directly, bypassing KernSmith's cell-height-to-ppem conversion |
| `SupportsSystemFonts` (optional) | Your engine can load system-installed fonts by family name via `LoadSystemFont()` |

The optional properties have default interface implementations that return `false`. Uncomment and set them only if applicable.

### Step 5: Implement Rasterization

These are the three required methods in your `IRasterizer` implementation.

**`LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)`**

Called once before any rasterization. Parse or load the font bytes into whatever native handle your engine needs. Store the handle as a field -- you will use it in `RasterizeGlyph`. The `faceIndex` parameter selects a face within `.ttc` font collections (usually 0).

**`RasterizeGlyph(int codepoint, RasterOptions options)`**

Render a single Unicode codepoint and return a `RasterizedGlyph`, or `null` if the font has no glyph for that codepoint. The returned object must have:

- `Codepoint`: the input codepoint
- `GlyphIndex`: the font's glyph index for this codepoint (0 if unknown)
- `BitmapData`: a `byte[]` of pixel data, either 1 byte/pixel (grayscale) or 4 bytes/pixel (RGBA)
- `Width` / `Height`: bitmap dimensions in pixels
- `Pitch`: bytes per scanline row (may be larger than `Width` if your engine adds row padding)
- `Metrics`: a `GlyphMetrics` with `BearingX`, `BearingY`, `Advance`, `Width`, `Height` in pixels
- `Format`: `PixelFormat.Grayscale8` or `PixelFormat.Rgba32`

**`RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)`**

Render multiple glyphs. The template provides a default loop that calls `RasterizeGlyph` for each codepoint. Override this if your engine supports batch rendering for better performance.

### Step 6: Optional Overrides

The following methods have default interface implementations (return `null` or do nothing). Uncomment and implement them in your rasterizer class when your engine provides native support. If omitted, KernSmith falls back to its built-in TTF table parsers.

**`GetFontMetrics(RasterOptions options)`** -- Return a `RasterizerFontMetrics` with `Ascent`, `Descent`, and `LineHeight` values from your engine. Implement this when your engine calculates font metrics differently from raw TTF table values (e.g. after hinting or size-specific adjustments).

**`GetKerningPairs(RasterOptions options)`** -- Return a list of `ScaledKerningPair(leftCodepoint, rightCodepoint, pixelAmount)` already scaled to the requested size. Implement this when your engine has its own kerning logic (e.g. reading GPOS via HarfBuzz).

**`LoadSystemFont(string familyName)`** -- Load a font from the OS font store by family name instead of from raw bytes. Only called when your `SupportsSystemFonts` capability is `true`.

**`SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes)`** -- Apply variable font axis values to your loaded font. Only called when `SupportsVariableFonts` is `true`.

**`SelectColorPalette(int paletteIndex)`** -- Select a CPAL color palette by index. Only called when `SupportsColorFonts` is `true`.

### Step 7: Wire Into Your Application

Add a project reference (or NuGet package reference) from your application to your rasterizer project:

```xml
<ProjectReference Include="..\KernSmith.Rasterizers.Skia\KernSmith.Rasterizers.Skia.csproj" />
```

The `[ModuleInitializer]` in the registration class runs automatically when the assembly loads. However, .NET may not load the assembly until a type from it is actually used. To force loading at startup, add this to your application's initialization:

```csharp
using System.Runtime.CompilerServices;

// Force the rasterizer assembly to load, triggering its ModuleInitializer
RuntimeHelpers.RunModuleConstructor(
    typeof(KernSmith.Rasterizers.Skia.SkiaRasterizer).Module.ModuleHandle);
```

Then create your bitmap font using the backend:

```csharp
var config = new BitmapFontConfig
{
    FontPath = "myfont.ttf",
    Backend = (RasterizerBackend)100,  // your backend ID
    FontSize = 32
};

var result = KernSmith.KernSmith.Generate(config);
```

### Step 8: Test

Verify that your backend is registered and produces correct output.

**Check registration:**

```csharp
bool registered = RasterizerFactory.IsRegistered((RasterizerBackend)100);
// Should be true after assembly loading
```

**Compare against FreeType reference:** Generate the same font at the same size with both FreeType (the default backend) and your backend. Compare glyph metrics (advance, bearings) and bitmap dimensions. Small pixel-level differences are expected due to different rasterization engines, but metrics should be close.

**Test edge cases:**

- Missing glyphs (codepoints not in the font) should return `null` from `RasterizeGlyph`
- Empty codepoint list to `RasterizeAll` should return an empty list
- Calling `Dispose()` twice should not throw
- Loading a corrupt or zero-length font should throw a descriptive exception, not crash
