# KernSmith.KniGum

Runtime bitmap font generation for KNI + Gum games.

## Install

```
dotnet add package KernSmith.KniGum
```

**Target:** net8.0

## Setup

One-line initialization in your game's `Initialize()` or `LoadContent()`:

```csharp
using KernSmith.Gum;
using RenderingLibrary;

CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmithFontCreator(GraphicsDevice);
```

The `KernSmithFontCreator` class and namespace are the same as the MonoGame package -- only the underlying framework dependencies differ.

## Registering Fonts

Some KNI targets (such as Blazor WASM) may not have access to system fonts. Registering font data explicitly is recommended for all platforms to ensure consistent cross-platform rendering. Register font data before initializing `KernSmithFontCreator`:

```csharp
using KernSmith;

// Load font bytes from embedded resources, content pipeline, etc.
byte[] robotoData = LoadEmbeddedResource("Fonts.Roboto-Regular.ttf");
byte[] robotoBoldData = LoadEmbeddedResource("Fonts.Roboto-Bold.ttf");

// Register before Gum initialization
BmFont.RegisterFont("Roboto", robotoData);
BmFont.RegisterFont("Roboto", robotoBoldData, style: "Bold");
```

With fonts registered, `GenerateFromSystem()` (used internally by `KernSmithFontCreator`) will find them by family name. Registered fonts take priority over system fonts, with automatic fallback to the OS font store on platforms that support it.

Register all font families and style variants your Gum layouts reference. A typical setup covers Regular, Bold, Italic, and Bold Italic:

```csharp
BmFont.RegisterFont("Roboto", robotoRegular);
BmFont.RegisterFont("Roboto", robotoBold, style: "Bold");
BmFont.RegisterFont("Roboto", robotoItalic, style: "Italic");
BmFont.RegisterFont("Roboto", robotoBoldItalic, style: "Bold Italic");
```

On Blazor WASM this is required -- without registration, font generation will fail because there are no system fonts to discover. On desktop platforms it is recommended for consistency so that every build renders identical fonts regardless of what is installed on the OS.

## Dependencies

This package automatically pulls in:
- `KernSmith.GumCommon` (shared generation logic)
- `KernSmith` (core library)
- `Gum.KNI`
- `nkast.Xna.Framework`

See [Integrations Overview](index.md) for channel configuration and option customization.
