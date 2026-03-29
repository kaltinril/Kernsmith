# Alternative Rasterizers

The core KernSmith library uses FreeTypeSharp as its default rasterizer backend. Two additional Windows-only rasterizer packages are available for scenarios where native Windows text rendering is preferred.

## Packages

| Package | Backend | Platform | Key Traits |
|---------|---------|----------|------------|
| **KernSmith** (built-in) | FreeTypeSharp | Cross-platform | Default; Linux, macOS, Windows |
| **KernSmith.Rasterizers.DirectWrite.TerraFX** | DirectWrite via TerraFX | Windows only | Color fonts, variable fonts, high-quality ClearType-style rendering |
| **KernSmith.Rasterizers.Gdi** | Windows GDI | Windows only | Matches BMFont.exe output; uses `GetGlyphOutlineW` with `GGO_GRAY8_BITMAP` |

## Auto-Registration

Both rasterizer packages use `[ModuleInitializer]` to register themselves with `RasterizerFactory` automatically. Simply referencing the package is enough -- no manual setup code is required.

## DirectWrite Rasterizer

`DirectWriteRasterizer` uses the DirectWrite API through [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) bindings. It creates an isolated `IDWriteFactory5` instance and loads fonts into memory via `IDWriteInMemoryFontFileLoader`.

Install:

```
dotnet add package KernSmith.Rasterizers.DirectWrite.TerraFX
```

Select it in options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.DirectWrite
};
```

Capabilities:

- Color font support (COLR/CPAL)
- Variable font axes
- High-quality subpixel rendering

## GDI Rasterizer

`GdiRasterizer` uses Windows GDI via P/Invoke for glyph rasterization. It produces output that closely matches BMFont's built-in rasterizer, which is useful for pixel-perfect compatibility with existing `.fnt` assets.

Install:

```
dotnet add package KernSmith.Rasterizers.Gdi
```

Select it in options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.Gdi
};
```

The GDI rasterizer also supports loading system-installed fonts by family name via `LoadSystemFont()`, matching BMFont's behavior for fonts like "Arial" or "Batang".

## Choosing a Rasterizer

- Use the **default FreeType backend** for cross-platform projects.
- Use **DirectWrite** when you need color font or variable font support on Windows, or want DirectWrite's rendering quality.
- Use **GDI** when you need pixel-perfect compatibility with BMFont.exe output on Windows.
