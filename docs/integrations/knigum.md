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

## Dependencies

This package automatically pulls in:
- `KernSmith.GumCommon` (shared generation logic)
- `KernSmith` (core library)
- `Gum.KNI`
- `nkast.Xna.Framework`

See [Integrations Overview](index.md) for channel configuration and option customization.
