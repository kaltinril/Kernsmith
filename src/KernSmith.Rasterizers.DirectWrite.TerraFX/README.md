# KernSmith.Rasterizers.DirectWrite.TerraFX

A DirectWrite rasterizer backend for KernSmith using TerraFX.Interop.Windows.

## Overview

This package provides an alternative rasterizer that uses Windows DirectWrite APIs (via TerraFX interop bindings) instead of FreeType, with native Windows text rendering quality.

Color and variable fonts are not yet implemented in this backend (both capabilities are currently stubbed) — use the FreeType backend for those.

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

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project documentation.

## Third-Party Licenses

See `THIRD-PARTY-NOTICES.md` (packaged at the package root) for third-party license attributions.
