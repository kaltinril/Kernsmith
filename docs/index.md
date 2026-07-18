# KernSmith

KernSmith is a cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF, OTF, and WOFF files. It combines FreeTypeSharp for glyph rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont `.fnt` + `.png` pairs ready for game engines and rendering frameworks.

## Key Capabilities

- **Multiple font formats** -- TTF, OTF, and WOFF input
- **BMFont output** -- text, XML, and binary `.fnt` formats with PNG, TGA, or DDS atlas pages
- **Layered effects** -- outline, gradient, and drop shadow compositing
- **Advanced rendering** -- SDF, super sampling, color fonts (COLR/CPAL), variable fonts, channel packing
- **Atlas packing** -- MaxRects and Skyline algorithms with autofit and power-of-two support
- **In-memory pipeline** -- the entire pipeline runs without touching disk unless you explicitly write output
- **Batch generation** -- parallel multi-font generation with font caching and pipeline metrics
- **Config formats** -- read and write BMFont `.bmfc` and libGDX Hiero `.hiero` configuration files

## Documentation Sections

| Section | Description |
|---------|-------------|
| [Core Library](core/index.md) | Namespaces, pipeline flow, and key classes in the KernSmith library |
| [API Reference Guide](api-reference/index.md) | Curated guide to the public API: `BmFont`, the fluent builder, `FontGeneratorOptions`, `BmFontResult`, `BmFontModel`, and exceptions |
| [CLI Reference](cli/index.md) | Command-line tool for generating bitmap fonts |
| [UI Guide](ui/index.md) | Visual interface for font configuration and preview |
| [Alternative Rasterizers](rasterizers/index.md) | GDI, DirectWrite (Windows), and StbTrueType (cross-platform managed) backends |
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
