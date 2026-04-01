# KernSmith.GumCommon

Shared integration logic that bridges KernSmith with Gum's font system. You typically don't install this directly -- it's pulled in automatically by the framework-specific packages (MonoGameGum, KniGum, FnaGum).

## Install

```
dotnet add package KernSmith.GumCommon
```

**Targets:** net8.0, net10.0

## Key Class: GumFontGenerator

`GumFontGenerator` maps Gum's `BmfcSave` font descriptor to KernSmith's `FontGeneratorOptions` and runs the generation pipeline.

### Generate directly

```csharp
using KernSmith.Gum;
using KernSmith.Output;
using RenderingLibrary.Graphics.Fonts;

BmfcSave bmfcSave = new BmfcSave();
bmfcSave.FontName = "Arial";
bmfcSave.FontSize = 24;

BmFontResult result = GumFontGenerator.Generate(bmfcSave);
```

### Select a rasterizer backend

Pass a `RasterizerBackend` to use a specific backend instead of the default (FreeType). This is required on platforms where native libraries are unavailable, such as Blazor WASM:

```csharp
BmFontResult result = GumFontGenerator.Generate(bmfcSave, RasterizerBackend.StbTrueType);
```

### Customize options

Use `BuildOptions()` to get a `FontGeneratorOptions` instance, modify it, then generate manually:

```csharp
FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);
options.Backend = RasterizerBackend.StbTrueType;
options.SuperSampleLevel = 2; // Add 2x super sampling

BmFontResult result = BmFont.GenerateFromSystem(bmfcSave.FontName, options);
```

## Font Registration

`GumFontGenerator.Generate()` calls `BmFont.GenerateFromSystem()` internally. On platforms without system font access, register font data first so that family names resolve correctly:

```csharp
using KernSmith;

BmFont.RegisterFont("Arial", arialFontData);
BmFont.RegisterFont("Arial", arialBoldData, style: "Bold");
```

See the [MonoGameGum](monogamegum.md) or [KniGum](knigum.md) integration pages for full setup examples.

## Channel Configuration

`GumFontGenerator` automatically configures texture channels to match BMFont's conventions:

- **Without outline** -- Alpha = glyph shape, RGB = white. Produces white text with alpha transparency.
- **With outline** -- Alpha = outline, RGB = glyph. Uses color channel separation.

## What Gets Mapped

| BmfcSave Property | FontGeneratorOptions |
|-------------------|---------------------|
| FontSize | Size |
| FontName | (passed to GenerateFromSystem) |
| IsBold / IsItalic | Bold / Italic |
| UseSmoothing | AntiAlias (Grayscale or None) |
| OutlineThickness | Outline |
| SpacingHorizontal/Vertical | Spacing |
| OutputWidth/Height | MaxTextureWidth/Height |
| Character ranges | Characters (CharacterSet) |
| (backend parameter) | Backend |

## Architecture

```
KernSmith.MonoGameGum ─┐
KernSmith.FnaGum ──────┤──> KernSmith.GumCommon ──> KernSmith (core)
KernSmith.KniGum ──────┘
```
