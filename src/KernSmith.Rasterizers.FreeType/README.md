# KernSmith.Rasterizers.FreeType

A FreeType rasterizer backend for KernSmith.

## Overview

This package provides a cross-platform rasterizer using FreeType. It supports color fonts (COLRv0/CPAL, sbix, CBDT), variable fonts, SDF rendering, and outline stroke effects.

**Platform**: Cross-platform (`net8.0`, `net10.0`).

## Usage

Install the package and set the rasterizer backend:

```
dotnet add package KernSmith.Rasterizers.FreeType
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.FreeType
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the package is sufficient.

## Build

```
dotnet build src/KernSmith.Rasterizers.FreeType/KernSmith.Rasterizers.FreeType.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/KernSmith) for full project documentation.
