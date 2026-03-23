# KernSmith

KernSmith is a cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF, OTF, WOFF, and WOFF2 files. It combines FreeTypeSharp for glyph rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont `.fnt` + `.png` pairs ready for game engines and rendering frameworks.

## Key Capabilities

- **Multiple font formats** -- TTF, OTF, WOFF, and WOFF2 input
- **BMFont output** -- text, XML, and binary `.fnt` formats with PNG, TGA, or DDS atlas pages
- **Layered effects** -- outline, gradient, and drop shadow compositing
- **Advanced rendering** -- SDF, super sampling, color fonts (COLR/CPAL), variable fonts, channel packing
- **Atlas packing** -- MaxRects and Skyline algorithms with autofit and power-of-two support
- **In-memory pipeline** -- the entire pipeline runs without touching disk unless you explicitly write output
- **Batch generation** -- parallel multi-font generation with font caching and pipeline metrics

## Documentation Sections

| Section | Description |
|---------|-------------|
| [Core Library](core/index.md) | Namespaces, pipeline flow, and key classes in the KernSmith library |
| [CLI Reference](cli/index.md) | Command-line tool for generating bitmap fonts |
| [UI Guide](ui/index.md) | Visual interface for font configuration and preview |
| [API Reference](../api/KernSmith.html) | Auto-generated API documentation |

## Quick Start

Install the NuGet package:

```
dotnet add package KernSmith
```

Generate a bitmap font:

```csharp
using KernSmith;

var result = BmFont.Generate("path/to/font.ttf", new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii
});

result.ToFile("output/myfont");
```

Or use the fluent builder:

```csharp
var result = BmFont.Builder()
    .WithFont("path/to/font.ttf")
    .WithSize(32)
    .WithCharacters(CharacterSet.Ascii)
    .WithKerning()
    .Build();
```
