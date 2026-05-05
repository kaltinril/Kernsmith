# GDI Rasterizer

Windows GDI rasterizer backend for KernSmith. Produces output that closely matches BMFont's built-in rasterizer for pixel-perfect compatibility.

## Installation

```
dotnet add package KernSmith.Rasterizers.Gdi
```

**Platform:** Windows only (net8.0-windows, net10.0-windows)

## Usage

The rasterizer auto-registers via `[ModuleInitializer]` -- referencing the package is sufficient. Select it in options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.Gdi
};

var result = BmFont.Generate("path/to/font.ttf", options);
```

For system fonts, use `GenerateFromSystem`:

```csharp
var result = BmFont.GenerateFromSystem("Arial", new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.Gdi
});
```

## Capabilities

- Pixel-perfect BMFont.exe compatibility
- System font loading by family name (e.g., "Arial", "Batang")
- Uses `GetGlyphOutlineW` with `GGO_GRAY8_BITMAP`

## Limitations

- Windows only -- will not load on Linux or macOS
- No SDF rendering support
- No color font support
- No variable font axis support
- No outline stroke support
- Cannot apply synthetic bold when a native bold face exists for the same family -- use FreeType or DirectWrite if you need to force synthetic bold in that scenario
- Fractional font sizes are rounded to the nearest integer. The underlying Win32 `LOGFONTW.lfHeight` field is integer-only, so values like `Size = 32.5f` are silently rounded on assignment. Use FreeType, StbTrueType, or DirectWrite if you need true fractional sizes

## When to Use

Use GDI when you need pixel-perfect compatibility with existing BMFont.exe-generated `.fnt` assets, or when validating KernSmith output against BMFont reference files.
