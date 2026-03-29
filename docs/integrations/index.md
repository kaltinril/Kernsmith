# Gum Framework Integrations

KernSmith provides integration packages that generate bitmap fonts at runtime for games built with the [Gum](https://docs.flatredball.com/gum) UI framework. These packages replace BMFont.exe in the font pipeline, generating `BitmapFont` instances entirely in memory with no disk I/O.

For initial Gum project setup, see the [Gum documentation](https://docs.flatredball.com/gum).

## Packages

| Package | Framework | NuGet ID |
|---------|-----------|----------|
| **KernSmith.MonoGameGum** | MonoGame + Gum | `KernSmith.MonoGameGum` |
| **KernSmith.FnaGum** | FNA + Gum | `KernSmith.FnaGum` |
| **KernSmith.KniGum** | KNI + Gum | `KernSmith.KniGum` |
| **KernSmith.GumCommon** | Shared (no framework dependency) | `KernSmith.GumCommon` |

All three framework packages share the same `KernSmithFontCreator` class and depend on `KernSmith.GumCommon` for the shared generation logic.

> **Note:** `KernSmith.FnaGum` is planned but not yet available on NuGet. The project files are maintained in the repository.

## How It Works

The integration has two layers:

1. **GumFontGenerator** (in `KernSmith.GumCommon`) -- Maps Gum's `BmfcSave` font descriptor to KernSmith's `FontGeneratorOptions` and runs the generation pipeline. This handles font size, bold/italic, anti-aliasing, outline thickness, spacing, texture dimensions, character ranges, and channel configuration.

2. **KernSmithFontCreator** (in each framework package) -- Implements Gum's `IInMemoryFontCreator` interface. Calls `GumFontGenerator.Generate()`, then converts the resulting atlas pages into framework-native `Texture2D` objects and constructs a `BitmapFont`.

## Quick Start (MonoGame)

Install the package:

```
dotnet add package KernSmith.MonoGameGum
```

Wire it up in your game's initialization (one line):

```csharp
using KernSmith.Gum;
using RenderingLibrary;

// In your Game.Initialize() or LoadContent():
CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmithFontCreator(GraphicsDevice);
```

Gum will now use KernSmith to generate fonts on demand whenever a `BmfcSave` is loaded. No `.fnt` or `.png` files need to exist on disk.

## Quick Start (FNA / KNI)

The setup is identical -- only the NuGet package name changes:

```
dotnet add package KernSmith.FnaGum
```

or

```
dotnet add package KernSmith.KniGum
```

The `KernSmithFontCreator` class and namespace (`KernSmith.Gum`) are the same across all three packages.

## Channel Configuration

`GumFontGenerator` automatically configures texture channels to match BMFont's conventions so that Gum's runtime renders correctly:

- **Without outline** -- Alpha channel contains the glyph shape; RGB channels are white (One). This produces white text with alpha transparency.
- **With outline** -- Alpha channel contains the outline; RGB channels contain the glyph. The outline uses color channel separation.

## Customizing Options

If you need to adjust generation options beyond what `BmfcSave` provides, use `GumFontGenerator.BuildOptions()` to get a `FontGeneratorOptions` instance, modify it, then generate manually:

```csharp
using KernSmith;
using KernSmith.Gum;
using RenderingLibrary.Graphics.Fonts;

BmfcSave bmfcSave = /* your BmfcSave instance */;

FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);
options.SuperSampleLevel = 2; // Add 2x super sampling

BmFontResult result = BmFont.GenerateFromSystem(bmfcSave.FontName, options);
```

## Architecture

```
KernSmith.MonoGameGum ─┐
KernSmith.FnaGum ──────┤──> KernSmith.GumCommon ──> KernSmith (core)
KernSmith.KniGum ──────┘
```

- `KernSmith.GumCommon` contains `GumFontGenerator` and has no framework dependency beyond Gum's rendering library.
- Each framework package references the appropriate Gum variant (`Gum.MonoGame`, `Gum.FNA`, or `Gum.KNI`) and shares the same `KernSmithFontCreator` source file via linked compilation.
