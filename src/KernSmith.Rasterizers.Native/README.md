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
to within a pixel.

Not yet supported — each raises a clear error or is ignored, as noted:

- **CFF/OTF (PostScript) outlines** — rejected at `LoadFont` with a `RasterizationException` (Phase 166)
- **WOFF2** — not yet supported by KernSmith core (WOFF1 is decompressed by core before rasterization, so WOFF-packaged TrueType fonts work)
- **Hinting** — none, so small sizes are softer than FreeType or GDI output
- **Synthetic bold/italic** (Phase 167) — `Bold`/`Italic` are ignored
- **Outline stroking** (Phase 168) — the backend has none of its own; KernSmith core still draws outlines as a bitmap effect
- **SDF** (Phase 169) — rejected with `NotSupportedException`
- **Variable fonts** (Phase 171) — axes are ignored; the default instance renders
- **Color fonts** (Phase 172) — `COLR` version 0 layers with `CPAL` palettes render as RGBA (straight alpha) and `ColorPaletteIndex` selects the palette. `COLR` version 1 glyphs render as plain grayscale coverage, and bitmap-only color fonts (`CBDT`/`sbix`, e.g. Noto Color Emoji) are rejected at `LoadFont` with a `RasterizationException`
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

## Glyph pipeline (contributors)

`RasterizeGlyph` and `GetGlyphMetrics` run the same stages, so metrics and bitmaps cannot
disagree. Everything is internal; add a feature to its stage rather than to `NativeRasterizer`.

| # | Stage | Code | Add a feature by |
|---|-------|------|------------------|
| 1 | Decode → `DecodedGlyph` (cubic outline, advance, optional declared bbox; font units) | `Internal/Outlines/IOutlineDecoder.cs`, `TrueTypeOutlineDecoder.cs` | implementing `IOutlineDecoder` and returning it from `OutlineDecoders.ForFace` — CFF (166). Variation deltas (171) go inside the decoder; `Decode` receives the `RasterOptions`. |
| 2 | Transforms, in order, in font units | `Internal/Pipeline/IGlyphTransform.cs`, `GlyphTransforms.cs` | implementing `IGlyphTransform` and adding it in `GlyphTransforms.For` — bold/italic (167), stroke/small caps/condensed (168). Build results with `WithOutline` / `WithAdvanceWidth`. |
| 3 | Bounds → pixel grid | `Internal/Raster/GlyphBox.cs`, `Internal/Outlines/OutlineBounds.cs` | nothing: a replaced outline loses its declared bbox and is measured from its tight bounds. |
| 4 | Scale, then pixel-space hint hook | `Internal/Pipeline/GlyphPipeline.cs`, `GlyphHinter.cs` | filling in `GlyphHinter.Apply` — auto-hinting (174). |
| 5 | Render-mode dispatch | `Internal/Pipeline/GlyphRenderers.cs`, `IGlyphRenderer.cs`, `CoverageRenderer.cs`, `ColorLayerRenderer.cs` | implementing `IGlyphRenderer` and returning it from `GlyphRenderers.Select` — SDF (169/170), LCD (173). Color layers (172) wrap that renderer in `GlyphRenderers.SelectForFace` and prepare each layer glyph through stages 1–4. `Measure` must match what `Render` produces. |

Invariants:

- Renderers map outline coordinates through `PreparedGlyph.ToPixels`, never a scale of their
  own — a hinter may return an outline already in pixel space.
- Untransformed TrueType glyphs size from the `glyf` header bbox, falling back to the control
  hull when it scales to nothing. `NativeOutputCharacterizationTests` pins the resulting bytes;
  a deliberate pixel change updates its hashes.
- Blank glyphs (no outline, or an empty box) never reach a renderer — except a glyph with `COLR` layers, whose own outline is not what gets drawn and is often empty.
- `PreparedGlyph.GlyphIndex` is the only way `Measure` can identify the glyph; a hint stage must carry it over (build with `with`).

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project
documentation.
