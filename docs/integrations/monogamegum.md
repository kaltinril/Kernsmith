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

## Dependencies

This package automatically pulls in:
- `KernSmith.GumCommon` (shared generation logic)
- `KernSmith` (core library)
- `Gum.MonoGame`
- `MonoGame.Framework.DesktopGL`

See [Integrations Overview](index.md) for channel configuration and option customization.
