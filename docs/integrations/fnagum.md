# KernSmith.FnaGum

Runtime bitmap font generation for FNA + Gum games.

> **Status:** Excluded -- this package is currently removed from the solution (`KernSmith.sln`)
> and is not built or published to NuGet. It needs FNA framework references resolved before it can
> be re-added. The project files are maintained in the repository, but the setup steps below will
> not work until the package is restored to the build and shipped. MonoGameGum, KniGum, and
> GumCommon are the active Gum integrations in the meantime.

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
