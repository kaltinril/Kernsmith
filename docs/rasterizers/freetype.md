# FreeType (Default)

The FreeType rasterizer backend for KernSmith. Uses [FreeTypeSharp](https://github.com/ryancheung/FreeTypeSharp) 3.1.0 for glyph rasterization.

## Platform

Cross-platform -- Windows, Linux x64, macOS, and Android (Android x86_64 needs `KernSmith.Rasterizers.FreeType` 0.23.0 or later, which carries the Android packaging fix; arm64 works in all versions). Linux arm64, Mac Catalyst, and Blazor WASM are unsupported (FreeTypeSharp ships no native library for them). iOS is unsupported: an iOS app that references the FreeType package fails to link, because FreeTypeSharp's static iOS library references libpng symbols that nothing provides -- use StbTrueType there. See the [platform support table](../../README.md#platform-support).

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
    Backend = RasterizerBackend.FreeType
};
```

## Capabilities

- TTF, OTF, WOFF, WOFF2 input
- Color fonts (COLR/CPAL)
- Variable font axes (fvar)
- Hinting and anti-aliasing (None, Grayscale, Light, Lcd modes)
- SDF (Signed Distance Field) rendering
- Outline stroke
- Synthetic bold and italic
- Super sampling
- Fractional font sizes (e.g. `Size = 32.5f`) honored natively

FreeType is the only backend that implements COLR/CPAL color glyphs and fvar variable font axes.

## Limitations

- Cannot load system-installed fonts by family name -- provide font file bytes directly

## When to Use

Use FreeType for cross-platform projects, color or variable fonts, SDF rendering, or when you don't need Windows-specific features like BMFont pixel-perfect parity. This is the recommended default for most use cases.
