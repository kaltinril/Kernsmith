# GDI Rasterizer

Windows GDI rasterizer backend for KernSmith. Produces output that closely matches BMFont's built-in rasterizer for pixel-perfect compatibility.

## Install

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
```

## Capabilities

- Pixel-perfect BMFont.exe compatibility
- System font loading by family name (e.g., "Arial", "Batang")
- Uses `GetGlyphOutlineW` with `GGO_GRAY8_BITMAP`

## When to Use

Use GDI when you need pixel-perfect compatibility with existing BMFont.exe-generated `.fnt` assets, or when validating KernSmith output against BMFont reference files.
