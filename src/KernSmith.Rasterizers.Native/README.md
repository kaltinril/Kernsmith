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
Synthetic bold and italic (Phase 167) are outline transforms that match the FreeType backend's:
its embolden strength (`max(0.5px, ppem/24)`, advance grown to match) and its 12° slant. They
apply to variable instances and to every `COLR` layer as well.

Not yet supported — each raises a clear error or is ignored, as noted:

- **CFF/OTF (PostScript) outlines** — rejected at `LoadFont` with a `RasterizationException` (Phase 166)
- **Outline stroking** (Phase 168) — the backend has none of its own; KernSmith core still draws outlines as a bitmap effect
- **SDF** (Phase 169) — rejected with `NotSupportedException`
- **CFF2 variable fonts** (Phase 171 Wave 2) — rejected with the rest of CFF; `avar` version 2
  axis mixing is not applied (its version 1 segment maps are)
- **Color fonts** (Phase 172) — `COLR` version 0 layers with `CPAL` palettes render as RGBA (straight alpha) and `ColorPaletteIndex` selects the palette. `COLR` version 1 glyphs render as plain grayscale coverage, and bitmap-only color fonts (`CBDT`/`sbix`, e.g. Noto Color Emoji) are rejected at `LoadFont` with a `RasterizationException`
- **System fonts by name** — load font bytes instead
- **Anti-aliasing** — `None`, `Grayscale` and `Light` (see [Hinting](#hinting)); an `LCD` request renders as `Grayscale`

The package is not published to NuGet yet, so `RasterizerBackend.Native` resolves only in
builds that reference this project directly. The KernSmith CLI ships it — try
`kernsmith generate -f font.ttf -s 32 --rasterizer native`.

## Hinting

Native has its own auto-hinter (Phase 174), no TrueType bytecode interpreter. It runs for
`AntiAlias = Light` with `EnableHinting` on (`.bmfc`: `antiAlias=light`, `useHinting=1`) and
fits the outline vertically only:

- **Blue zones** — baseline, x-height, cap height, ascender and descender, with the overshoot
  of round glyphs, measured from 'x', 'o', 'H', 'O', 'd' and 'p' after any synthetic style or
  variation instance (`OS/2` `sxHeight`/`sCapHeight` stand in when 'x'/'H' are missing). Edges
  in a zone snap to its pixel line; overshoots under half a pixel snap with the flat glyphs, and
  top zones round up from a third of a pixel.
- **Strokes** — a horizontal stroke (a bottom edge paired with the top edge above it) gets a
  whole-pixel thickness, at least one pixel, rounded from the font's standard stroke when close
  to it; remaining flat edges round to the nearest row.
- **Everything else** moves by one monotone interpolation between the fitted edges, so the
  outline cannot fold over itself. X is untouched: advances and side bearings match unhinted.

Every other request, including the default (`Grayscale` with `EnableHinting` true), renders
unhinted. A font with no x-height and no cap height (no Latin reference glyphs, no `OS/2`
heights) renders unhinted rather than snapped to zones that do not describe it.

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
| 2 | Transforms, in order, in font units | `Internal/Pipeline/IGlyphTransform.cs`, `GlyphTransforms.cs` | implementing `IGlyphTransform` and adding it in `GlyphTransforms.For` — `BoldTransform` then `ItalicTransform` today (167). `WidthScaleTransform`, `SmallCapsTransform` (before bold) and `StrokeTransform` (last, as a separate layer) exist but are not registered until core options reach them (168). Build results with `WithOutline` / `WithAdvanceWidth`. |
| 3 | Bounds → pixel grid | `Internal/Raster/GlyphBox.cs`, `Internal/Outlines/OutlineBounds.cs` | nothing: a replaced outline loses its declared bbox and is measured from its tight bounds. |
| 4 | Scale, then pixel-space hinting | `Internal/Pipeline/GlyphPipeline.cs`, `GlyphHinter.cs`; `Internal/Hinting/` | `GlyphHinter.Apply` runs the auto-hinter (174) for hinted requests: `FontHintMetrics` (per face/size/style/instance, cached) and `GridFitter`. It returns a pixel-space outline with an identity `ToPixels`. |
| 5 | Render-mode dispatch | `Internal/Pipeline/GlyphRenderers.cs`, `IGlyphRenderer.cs`, `CoverageRenderer.cs`, `ColorLayerRenderer.cs` | implementing `IGlyphRenderer` and returning it from `GlyphRenderers.Select` — SDF (169/170), LCD (173). Color layers (172) wrap that renderer in `GlyphRenderers.SelectForFace` and prepare each layer glyph through stages 1–4. `Measure` must match what `Render` produces. |

Invariants:

- Renderers map outline coordinates through `PreparedGlyph.ToPixels`, never a scale of their
  own — a hinter may return an outline already in pixel space.
- A TrueType outline is placed by its left side bearing phantom point, pp1 = `xMin - lsb`
  (a `USE_MY_METRICS` component's, for such a composite; plus its `gvar` delta), which the
  decoder puts at x = 0 — so the outline sits at the `hmtx` bearing even when it disagrees with
  the `glyf` xMin. The declared header box is moved with it.
- Untransformed TrueType glyphs size from the `glyf` header bbox, falling back to the control
  hull when it scales to nothing. A glyph whose outline a variation moved drops the header box
  and is measured tightly. `NativeOutputCharacterizationTests` pins the resulting bytes;
  a deliberate pixel change updates its hashes.
- Blank glyphs (no outline, or an empty box) never reach a renderer — except a glyph with `COLR` layers, whose own outline is not what gets drawn and is often empty.
- `PreparedGlyph.GlyphIndex` is the only way `Measure` can identify the glyph; a hint stage must carry it over (build with `with`).
- The default instance (no axes, or every axis at its default) takes the exact static decode
  path, so non-variable output cannot drift.

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project
documentation.
