# KernSmith.Rasterizers.Native

A fully custom, pure C# rasterizer backend for KernSmith. Zero external NuGet dependencies.

## Overview

This package provides the `Native` rasterizer — a font rasterizer owned entirely by
KernSmith, with no native libraries and no third-party font dependencies. It targets
feature parity with the FreeType backend over time, doing outline-level work (synthetic
bold/italic, stroking, SDF) in managed C#.

**Platform**: Cross-platform (`net8.0`, `net10.0`). No native libraries required.
Trim- and AOT-friendly. Verified on Windows, Linux (x64 and arm64), macOS, Android, iOS, and Mac Catalyst; Blazor WASM
is expected to work but not yet verified — see the
[platform support table](https://github.com/kaltinril/Kernsmith#platform-support).

## Status

In development. As of Phase 165 the backend renders TrueType (`glyf`) outlines end to end:
SFNT table parsing, cmap lookup (formats 4 and 12), outline extraction, adaptive curve
flattening, and a signed-area scanline fill, with font and glyph metrics that track FreeType
to within a pixel. WOFF and WOFF2 files wrapping TrueType outlines work too: KernSmith core
decompresses them before the backend sees the bytes.

Not yet supported — each raises a clear error or is ignored, as noted:

- **CFF/OTF (PostScript) outlines** — rejected at `LoadFont` with a `RasterizationException` (Phase 166)
- **Hinting** — none, so small sizes are softer than FreeType or GDI output
- **Synthetic bold/italic** (Phase 167) — `Bold`/`Italic` are ignored
- **Outline stroking** (Phase 168) — the backend has none of its own; KernSmith core still draws outlines as a bitmap effect
- **SDF** (Phase 169) — rejected with `NotSupportedException`
- **Variable fonts** (Phase 171) — axes are ignored; the default instance renders
- **Color fonts** (Phase 172) — `ColorFont` throws `NotSupportedException`
- **System fonts by name** — load font bytes instead
- **Anti-aliasing** — `None` and `Grayscale` only; a `Light` or `LCD` request renders as `Grayscale`

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
