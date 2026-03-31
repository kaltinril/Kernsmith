# FreeType (Default)

The built-in rasterizer backend, included in the core `KernSmith` package. Uses [FreeTypeSharp](https://github.com/nicholasgasior/FreeTypeSharp) 3.1.0 for glyph rasterization.

## Platform

Cross-platform -- Linux, macOS, Windows.

## Installation

FreeType is bundled with the core package. No additional NuGet references are needed:

```
dotnet add package KernSmith
```

## Usage

FreeType is the default backend. No additional packages or configuration needed:

```csharp
var result = BmFont.Generate("path/to/font.ttf", new FontGeneratorOptions
{
    Size = 32
});
```

To explicitly select it:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.FreeType
};
```

## Capabilities

- TTF, OTF, WOFF, WOFF2 input
- Hinting and anti-aliasing (Grayscale, Light, None modes)
- SDF (Signed Distance Field) rendering
- Outline stroke
- Synthetic bold and italic
- Super sampling

## Limitations

- Does not support color fonts (COLR/CPAL) -- use DirectWrite for that
- Does not support variable font axes -- use DirectWrite for that
- Cannot load system-installed fonts by family name -- provide font file bytes directly

## When to Use

Use FreeType for cross-platform projects, SDF rendering, or when you don't need Windows-specific features like color fonts or BMFont pixel-perfect parity. This is the recommended default for most use cases.
