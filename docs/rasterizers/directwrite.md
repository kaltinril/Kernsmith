# DirectWrite Rasterizer

DirectWrite rasterizer backend using [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) bindings. Provides access to Windows' modern text rendering APIs, including high-quality ClearType subpixel rendering and system font loading.

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

For system fonts, use `GenerateFromSystem`:

```csharp
var result = BmFont.GenerateFromSystem("Segoe UI", new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.DirectWrite
});
```

> [!NOTE]
> Color font (`ColorPaletteIndex`) and variable font (`VariationAxes`) options are not yet honored by this backend -- see [Limitations](#limitations). Use [FreeType](freetype.md) for COLR/CPAL and fvar today.

## Capabilities

- High-quality ClearType-style subpixel rendering
- System font loading by family name
- Synthetic bold and italic
- TTF, OTF, WOFF input
- Fractional font sizes (e.g. `Size = 32.5f`) honored natively

## Limitations

- **Color fonts (COLR/CPAL) are not yet implemented** -- the capability is currently stubbed (`SupportsColorFonts` returns `false`; there is no `TranslateColorGlyphRun` implementation yet). Use [FreeType](freetype.md) for color glyphs.
- **Variable font axes (fvar) are not yet implemented** -- the capability is currently stubbed (`SupportsVariableFonts` returns `false`; there is no `IDWriteFontFace5` axis implementation yet). Use [FreeType](freetype.md) for variable fonts.
- Windows only -- will not load on Linux or macOS
- Requires net10.0-windows (not net8.0-windows)
- No SDF rendering support -- use FreeType for SDF
- No outline stroke support

## Implementation

Uses an isolated `IDWriteFactory5` instance and loads fonts into memory via `IDWriteInMemoryFontFileLoader`. All COM interop is handled internally.

## When to Use

Use DirectWrite when you want Windows' high-quality ClearType subpixel rendering or need to load system-installed fonts by family name on Windows. For color fonts (COLR/CPAL) or variable font axes (fvar), use [FreeType](freetype.md) -- those DirectWrite code paths are currently stubbed and not yet functional.
