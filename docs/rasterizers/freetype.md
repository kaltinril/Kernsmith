# FreeType (Default)

The FreeType rasterizer backend for KernSmith. Uses [FreeTypeSharp](https://github.com/nicholasgasior/FreeTypeSharp) 3.1.0 for glyph rasterization.

## Platform

Cross-platform -- Linux, macOS, Windows.

## Installation

```
dotnet add package KernSmith.Rasterizers.FreeType
```

## Usage

FreeType is the default backend. Install the package and it auto-registers via `[ModuleInitializer]`:

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

- TTF, OTF, WOFF input
- Color fonts (COLR/CPAL)
- Variable font axes (fvar)
- Hinting and anti-aliasing (Grayscale, Light, None modes)
- SDF (Signed Distance Field) rendering
- Outline stroke
- Synthetic bold and italic
- Super sampling
- Fractional font sizes (e.g. `Size = 32.5f`) honored natively

FreeType is the only backend that implements COLR/CPAL color glyphs and fvar variable font axes.

## Limitations

- Does not load WOFF2 input -- decompress to TTF/OTF/WOFF first
- Cannot load system-installed fonts by family name -- provide font file bytes directly

## When to Use

Use FreeType for cross-platform projects, color or variable fonts, SDF rendering, or when you don't need Windows-specific features like BMFont pixel-perfect parity. This is the recommended default for most use cases.
