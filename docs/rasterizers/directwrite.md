# DirectWrite Rasterizer

DirectWrite rasterizer backend using [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) bindings. Provides access to Windows' modern text rendering APIs including color font and variable font support.

## Install

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
```

## Capabilities

- Color font support (COLR/CPAL)
- Variable font axes
- High-quality ClearType-style subpixel rendering
- System font loading by family name

## Implementation

Uses an isolated `IDWriteFactory5` instance and loads fonts into memory via `IDWriteInMemoryFontFileLoader`. All COM interop is handled internally.

## When to Use

Use DirectWrite when you need color font or variable font support on Windows, or want DirectWrite's rendering quality.
