# bmfontier

Cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF/OTF/WOFF files.

Combines FreeTypeSharp for rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont `.fnt` + `.png` pairs. In-memory by default.

## Features

- Rasterize TTF/OTF/WOFF fonts to bitmap glyph atlases
- BMFont-compatible `.fnt` output (text, XML, and binary formats)
- GPOS kerning pair extraction from OpenType tables
- Configurable texture atlas packing
- Post-processors: SDF, gradient, shadow, outline
- Fully in-memory pipeline (no temp files)
- Cross-platform (Windows, Linux, macOS)

## Installation

```
dotnet add package Bmfontier
```

## Quick Start

```csharp
using Bmfontier;

var result = BmFont.Generate(new BmFontConfig
{
    FontPath = "path/to/font.ttf",
    FontSize = 32,
    Characters = BmFontCharacterSets.Ascii
});

// result.FontDescriptor  - the .fnt content
// result.TexturePages     - the .png atlas pages
```

## License

MIT
