# API Reference Guide

This is a curated, task-oriented guide to KernSmith's public API. For the complete,
auto-generated member listing of every type in the core KernSmith library, see the [generated API reference](../../api/KernSmith.html).

This guide focuses on the surface most applications use:

| Page | Covers |
|------|--------|
| [BmFont entry point](bmfont.md) | The static `BmFont` class -- `Generate`, `GenerateFromSystem`, `FromConfig`, `Load`, `Builder`, batch, and font registration |
| [Fluent builder](builder.md) | `BmFont.Builder()` and every `With*` method on `BmFontBuilder` |
| [FontGeneratorOptions](options.md) | Every configurable property, its default, and what it does |
| [BmFontResult and output](result.md) | The generation result and how to read or write its output |
| [BmFontModel](model.md) | The parsed `.fnt` descriptor structure (`InfoBlock`, `CommonBlock`, `CharEntry`, etc.) |
| [Exceptions](exceptions.md) | Each exception type and when it is thrown |

## Two ways to configure generation

KernSmith offers two equivalent styles. Both ultimately call the same pipeline.

Options object:

```csharp
using KernSmith;

var result = BmFont.Generate("font.ttf", new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii,
    Kerning = true
});
```

Fluent builder:

```csharp
var result = BmFont.Builder()
    .WithFont("font.ttf")
    .WithSize(32)
    .WithCharacters(CharacterSet.Ascii)
    .WithKerning()
    .Build();
```

## Namespaces at a glance

| Namespace | What you find here |
|-----------|--------------------|
| `KernSmith` | `BmFont`, `BmFontBuilder`, `FontGeneratorOptions`, `CharacterSet`, enums, and the exception types |
| `KernSmith.Output` | `BmFontResult` |
| `KernSmith.Output.Model` | `BmFontModel` and its blocks (`InfoBlock`, `CommonBlock`, `CharEntry`, `PageEntry`, `KerningEntry`, `ExtendedMetadata`) |
| `KernSmith.Atlas` | `AtlasPage`, `PixelFormat` |

> All public types in this guide were verified against the source for KernSmith 0.15.1.
