# KernSmith.Rasterizers.DirectWrite.TerraFX

A DirectWrite rasterizer backend for KernSmith using TerraFX.Interop.Windows.

## Overview

This package provides an alternative rasterizer that uses Windows DirectWrite APIs (via TerraFX interop bindings) instead of FreeType. It supports color fonts and variable fonts with native Windows text rendering quality.

**Platform**: Windows only (`net10.0-windows`).

## Usage

Install the package and set the rasterizer backend:

```
dotnet add package KernSmith.Rasterizers.DirectWrite.TerraFX
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.DirectWrite
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the package is sufficient.

## Build

```
dotnet build src/KernSmith.Rasterizers.DirectWrite.TerraFX/KernSmith.Rasterizers.DirectWrite.TerraFX.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/KernSmith) for full project documentation.
