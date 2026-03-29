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

### Customize options

Use `BuildOptions()` to get a `FontGeneratorOptions` instance, modify it, then generate manually:

```csharp
FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);
options.SuperSampleLevel = 2; // Add 2x super sampling

BmFontResult result = BmFont.GenerateFromSystem(bmfcSave.FontName, options);
```

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

## Architecture

```
KernSmith.MonoGameGum ─┐
KernSmith.FnaGum ──────┤──> KernSmith.GumCommon ──> KernSmith (core)
KernSmith.KniGum ──────┘
```
