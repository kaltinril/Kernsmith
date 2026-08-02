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

In development. As of Phase 165 the backend renders TrueType (`glyf`) outlines end to end:
SFNT table parsing, cmap lookup (formats 4 and 12), outline extraction, adaptive curve
flattening, and a signed-area scanline fill, with font and glyph metrics that track FreeType
to within a pixel.

Not yet supported — each raises a clear error or is ignored:

- **CFF/OTF (PostScript) outlines** — rejected at `LoadFont` with a `RasterizationException` (Phase 166)
- **WOFF/WOFF2** — supply raw TTF/TTC bytes
- **Hinting** — none, so small sizes are softer than FreeType or GDI output
- **Synthetic bold/italic** (Phase 167), **outline stroking** (Phase 168), **SDF** (Phase 169),
  **variable fonts** (Phase 171), **color fonts** (Phase 172)
- **System fonts by name** — load font bytes instead
- **Anti-aliasing** — `None` and `Grayscale` only; no `Light` or `LCD`

The package is not published to NuGet yet, so `RasterizerBackend.Native` resolves only in
builds that reference this project directly. The KernSmith CLI ships it — try
`kernsmith generate -f font.ttf -s 32 --rasterizer native`.

## Usage

Reference the project from a source build (there is no `dotnet add package` yet):

```xml
<ProjectReference Include="path/to/src/KernSmith.Rasterizers.Native/KernSmith.Rasterizers.Native.csproj" />
```

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    Backend = RasterizerBackend.Native
};
```

The rasterizer auto-registers via `[ModuleInitializer]`, so referencing the project is
sufficient.

## Build

```
dotnet build src/KernSmith.Rasterizers.Native/KernSmith.Rasterizers.Native.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project
documentation.
