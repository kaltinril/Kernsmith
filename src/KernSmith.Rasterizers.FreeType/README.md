# KernSmith.Rasterizers.FreeType

A FreeType rasterizer backend for KernSmith.

## Overview

This package provides a cross-platform rasterizer using FreeType. It supports color fonts (COLRv0/CPAL, sbix, CBDT), variable fonts, SDF rendering, and every anti-alias mode.

**Platform**: Cross-platform (`net8.0`, `net10.0`). Native FreeType binaries come from FreeTypeSharp:

- **Windows, Linux x64, macOS** — supported.
- **Android** — supported on arm64 and x86_64. The package's `build/`/`buildTransitive/` targets drop FreeTypeSharp's desktop Linux library from Android builds; without that (earlier package versions), NuGet's RID fallback packages it into the APK and x86_64 fails with `DllNotFoundException`.
- **iOS** — unsupported. An iOS app that references this package fails to link, even if FreeType is never used: FreeTypeSharp 3.1.0's static iOS `libfreetype.a` references libpng symbols that nothing provides. Do not reference this package in iOS apps; use StbTrueType.
- **Linux arm64, Mac Catalyst, Blazor WASM** — unsupported (FreeTypeSharp ships no native library); use StbTrueType.

In trimmed or AOT apps (e.g. Android Release), register the backend explicitly with `RasterizerFactory.Register(RasterizerBackend.FreeType, () => new FreeTypeRasterizer())`. See the [platform support table](https://github.com/kaltinril/Kernsmith#platform-support).

## Usage

Install the package and set the rasterizer backend:

```
dotnet add package KernSmith.Rasterizers.FreeType
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    Backend = RasterizerBackend.FreeType
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the package is sufficient.

## Build

```
dotnet build src/KernSmith.Rasterizers.FreeType/KernSmith.Rasterizers.FreeType.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project documentation.

## Third-Party Licenses

This package uses FreeType (2.13.2, via FreeTypeSharp) under the FreeType License (FTL).

> Portions of this software are copyright © 2023 The FreeType Project (www.freetype.org). All rights reserved.

See `THIRD-PARTY-NOTICES.md` (packaged at the package root) for the full license text.
