# KernSmith.Rasterizers.StbTrueType

A pure C# rasterizer backend for KernSmith using StbTrueTypeSharp.

## Overview

This package provides a cross-platform rasterizer with zero native dependencies. It supports SDF rendering and works on any .NET platform including Blazor WASM, iOS AOT, and serverless environments.

**Platform**: Cross-platform (`net8.0`, `net10.0`). No native libraries required.

## Usage

Install the package and set the rasterizer backend:

```
dotnet add package KernSmith.Rasterizers.StbTrueType
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.StbTrueType
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the package is sufficient.

## Limitations

- No TrueType hinting (lower quality at small sizes < 16px)
- No color font support (COLR/CPAL)
- No variable font support
- No synthetic bold/italic
- TTF only (no OTF/CFF outlines)
- No system font loading

For these features, use the FreeType backend.

## Build

```
dotnet build src/KernSmith.Rasterizers.StbTrueType/KernSmith.Rasterizers.StbTrueType.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/KernSmith) for full project documentation.
