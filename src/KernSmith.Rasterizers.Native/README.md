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
TrueType variable fonts (Phase 171) render at the requested instance:
`fvar`/`avar` normalization, `gvar` outline deltas (composites included), `HVAR` advances
(phantom points when a font has no `HVAR`) and `MVAR` line metrics.
`AntiAliasMode.Lcd` (Phase 173) renders LCD subpixel glyphs: RGBA, one horizontal subpixel per
colour channel in RGB order, alpha = the strongest subpixel, filtered with a one-pixel box
(per-channel output matches the FreeType backend's LCD mode).

Not yet supported — each raises a clear error or is ignored, as noted:

- **CFF/OTF (PostScript) outlines** — rejected at `LoadFont` with a `RasterizationException` (Phase 166)
- **Hinting** — none, so small sizes are softer than FreeType or GDI output
- **Synthetic bold/italic** (Phase 167) — `Bold`/`Italic` are ignored
- **Outline stroking** (Phase 168) — the backend has none of its own; KernSmith core still draws outlines as a bitmap effect
- **SDF** (Phase 169) — rejected with `NotSupportedException`
- **CFF2 variable fonts** (Phase 171 Wave 2) — rejected with the rest of CFF; `avar` version 2
  axis mixing is not applied (its version 1 segment maps are)
- **Color fonts** (Phase 172) — `ColorFont` throws `NotSupportedException`
- **System fonts by name** — load font bytes instead
- **`Light` anti-aliasing** — renders as `Grayscale` until hinting lands (Phase 174); KernSmith core degrades the request before Native sees it
- **LCD options** — RGB order and the one-pixel box filter only; no BGR, vertical or alternative filter selection yet

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
| 1 | Decode → `DecodedGlyph` (cubic outline, advance, optional declared bbox; font units) | `Internal/Outlines/IOutlineDecoder.cs`, `TrueTypeOutlineDecoder.cs`; variations in `Internal/Variations/` | implementing `IOutlineDecoder` and returning it from `OutlineDecoders.ForFace` — CFF (166). Variation deltas live inside the decoder: `TrueTypeOutlineDecoder` reads the face's `FontVariations` (set by `SetVariationAxes`); a CFF2 decoder would read the same coordinates. |
| 2 | Transforms, in order, in font units | `Internal/Pipeline/IGlyphTransform.cs`, `GlyphTransforms.cs` | implementing `IGlyphTransform` and adding it in `GlyphTransforms.For` — bold/italic (167), stroke/small caps/condensed (168). Build results with `WithOutline` / `WithAdvanceWidth`. |
| 3 | Bounds → pixel grid | `Internal/Raster/GlyphBox.cs`, `Internal/Outlines/OutlineBounds.cs` | nothing: a replaced outline loses its declared bbox and is measured from its tight bounds. |
| 4 | Scale, then pixel-space hint hook | `Internal/Pipeline/GlyphPipeline.cs`, `GlyphHinter.cs` | filling in `GlyphHinter.Apply` — auto-hinting (174). |
| 5 | Render-mode dispatch | `Internal/Pipeline/GlyphRenderers.cs`, `IGlyphRenderer.cs`, `CoverageRenderer.cs`, `LcdRenderer.cs` (filters in `Internal/Raster/LcdFilter.cs`) | implementing `IGlyphRenderer` and returning it from `GlyphRenderers.Select` — SDF (169/170), color (172). `Measure` must match what `Render` produces. |

Invariants:

- Renderers map outline coordinates through `PreparedGlyph.ToPixels`, never a scale of their
  own — a hinter may return an outline already in pixel space.
- Untransformed TrueType glyphs size from the `glyf` header bbox, falling back to the control
  hull when it scales to nothing. A glyph whose outline a variation moved drops the header box
  and is measured tightly. `NativeOutputCharacterizationTests` pins the resulting bytes;
  a deliberate pixel change updates its hashes.
- Blank glyphs (no outline, or an empty box) never reach a renderer.
- The default instance (no axes, or every axis at its default) takes the exact static decode
  path, so non-variable output cannot drift.

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project
documentation.
