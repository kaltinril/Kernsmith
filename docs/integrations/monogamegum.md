# KernSmith.MonoGameGum

Runtime bitmap font generation for MonoGame + Gum games.

## Install

```
dotnet add package KernSmith.MonoGameGum
```

**Targets:** net8.0, net9.0

## Setup

One-line initialization in your game's `Initialize()` or `LoadContent()`:

```csharp
using KernSmith.Gum;
using RenderingLibrary;

CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmithFontCreator(GraphicsDevice);
```

Gum will now use KernSmith to generate fonts on demand whenever a `BmfcSave` is loaded. No `.fnt` or `.png` files need to exist on disk.

## How It Works

`KernSmithFontCreator` implements Gum's `IInMemoryFontCreator` interface. When Gum requests a font:

1. `GumFontGenerator` (from GumCommon) maps the `BmfcSave` to `FontGeneratorOptions`
2. KernSmith generates the bitmap font atlas in memory
3. `KernSmithFontCreator` converts atlas pages into MonoGame `Texture2D` objects
4. A `BitmapFont` is constructed and returned to Gum

## Registering Fonts

Registering font data explicitly is recommended for all platforms to ensure consistent cross-platform rendering. Some targets may not have access to system fonts, but even on desktop this guarantees identical results regardless of what is installed on the OS. Register fonts before initializing `KernSmithFontCreator`:

```csharp
using KernSmith.Gum;

// Recommended — uses TitleContainer.OpenStream, works on all platforms
KernSmithFontCreator.RegisterFont("Roboto", "Content/Fonts/Roboto-Regular.ttf");
KernSmithFontCreator.RegisterFont("Roboto", "Content/Fonts/Roboto-Bold.ttf", style: "Bold");
```

The file-path overload uses `TitleContainer.OpenStream` internally, so the path is relative to the title container root (typically the Content directory). This is the simplest approach and works on all MonoGame platforms.

For fonts loaded from embedded resources, archives, or HTTP, use the `byte[]` overload instead:

```csharp
using KernSmith.Gum;

byte[] fontData = File.ReadAllBytes("path/to/font.ttf");
KernSmithFontCreator.RegisterFont("MyFont", fontData);
```

Registered fonts take priority over system fonts, with automatic fallback to the OS font store. Register all font families and style variants your Gum layouts reference (Regular, Bold, Italic, Bold Italic).

On platforms without system fonts (Blazor WASM, mobile, containers) registration is required. On desktop it is not strictly necessary but is recommended so that every build renders identical fonts.

## Dependencies

This package automatically pulls in:
- `KernSmith.GumCommon` (shared generation logic)
- `KernSmith` (core library)
- `Gum.MonoGame`
- `MonoGame.Framework.DesktopGL`

See [Integrations Overview](index.md) for channel configuration and option customization.
