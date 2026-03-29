# KernSmith.FnaGum

Runtime bitmap font generation for FNA + Gum games.

> **Status:** Planned -- this package is not yet available on NuGet. The project files are maintained in the repository.

## Setup (when available)

```
dotnet add package KernSmith.FnaGum
```

**Target:** net8.0

The setup and usage will be identical to the MonoGame and KNI packages:

```csharp
using KernSmith.Gum;
using RenderingLibrary;

CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmithFontCreator(GraphicsDevice);
```

See [Integrations Overview](index.md) for channel configuration and option customization.
