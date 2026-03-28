# KernSmith.Rasterizers.Gdi

A GDI rasterizer backend for KernSmith.

## Overview

This package provides a rasterizer that uses Windows GDI text rendering APIs. It is designed to match BMFont's original output for pixel-perfect parity, making it useful for validating KernSmith output against reference BMFont files.

**Platform**: Windows only (`net8.0-windows`, `net10.0-windows`).

## Usage

Install the package and set the rasterizer backend:

```
dotnet add package KernSmith.Rasterizers.Gdi
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.Gdi
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the package is sufficient.

## Build

```
dotnet build src/KernSmith.Rasterizers.Gdi/KernSmith.Rasterizers.Gdi.csproj
```

See the [root README](../../README.md) for full project documentation.
