# DirectWrite Rasterizer

DirectWrite rasterizer backend using [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) bindings. Provides access to Windows' modern text rendering APIs including color font and variable font support.

## Installation

```
dotnet add package KernSmith.Rasterizers.DirectWrite.TerraFX
```

**Platform:** Windows only (net10.0-windows)

## Usage

The rasterizer auto-registers via `[ModuleInitializer]` -- referencing the package is sufficient. Select it in options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.DirectWrite
};

var result = BmFont.Generate("path/to/font.ttf", options);
```

For color fonts with a specific palette:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.DirectWrite,
    ColorPaletteIndex = 0
};
```

For variable fonts with custom axis values:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.DirectWrite,
    VariationAxes = new Dictionary<string, float> { ["wght"] = 700, ["wdth"] = 100 }
};
```

## Capabilities

- Color font support (COLR/CPAL)
- Variable font axes (fvar)
- High-quality ClearType-style subpixel rendering
- System font loading by family name
- Synthetic bold and italic
- TTF, OTF, WOFF, WOFF2 input

## Limitations

- Windows only -- will not load on Linux or macOS
- Requires net10.0-windows (not net8.0-windows)
- No SDF rendering support -- use FreeType for SDF
- No outline stroke support

## Implementation

Uses an isolated `IDWriteFactory5` instance and loads fonts into memory via `IDWriteInMemoryFontFileLoader`. All COM interop is handled internally.

## When to Use

Use DirectWrite when you need color font or variable font support on Windows, or want DirectWrite's high-quality rendering. It is the only backend that supports COLR/CPAL color glyphs and fvar variable font axes.
