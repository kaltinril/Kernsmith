# KernSmith.Rasterizers.Native

A fully custom, pure C# rasterizer backend for KernSmith. Zero external NuGet dependencies.

## Overview

This package provides the `Native` rasterizer — a font rasterizer owned entirely by
KernSmith, with no native libraries and no third-party font dependencies. It targets
feature parity with the FreeType backend over time, doing outline-level work (synthetic
bold/italic, stroking, SDF) in managed C#.

**Platform**: Cross-platform (`net8.0`, `net10.0`). No native libraries required.
Trim- and AOT-friendly — ideal for Blazor WASM, iOS AOT, and serverless.

## Status

This is an early scaffold (Phase 161). It loads and validates fonts and parses the core
SFNT tables (`head`, `hhea`, `hmtx`, `OS/2`, `cmap`), but glyph outline decoding and
rasterization arrive in later phases. Calling the render methods currently throws
`NotImplementedException`.

## Usage

```
dotnet add package KernSmith.Rasterizers.Native
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.Native
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the package is
sufficient.

## Build

```
dotnet build src/KernSmith.Rasterizers.Native/KernSmith.Rasterizers.Native.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/KernSmith) for full project
documentation.
