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

Experimental: everything below works and is tested, but the package is not published yet
(see the end of this section). The backend renders TrueType (`glyf`) and PostScript (`CFF ` /
`CFF2`, typically `.otf`) outlines end to end: SFNT table parsing, cmap lookup (formats 4 and
12), outline decoding (including a Type 2 charstring interpreter with CID-keyed font support),
adaptive curve flattening, and an exact-area scanline fill, with font and glyph metrics that
track FreeType to within a pixel. WOFF and WOFF2 files work too: KernSmith core decompresses
them before the backend sees the bytes.

- **Variable fonts** render at the requested instance: `fvar`/`avar` normalization, `gvar`
  outline deltas (composites included) for TrueType and charstring `blend`/`vsindex` for CFF2,
  `HVAR` advances (phantom points when a TrueType font has no `HVAR`) and `MVAR` line metrics.
  `avar` version 2 axis mixing is not applied (its version 1 segment maps are), and blended
  CFF2 Private DICT hint values are read at their defaults (the auto-hinter measures its zones
  from outlines instead). Named instances are not selectable; core passes axis values only.
- **Synthetic bold and italic** are outline transforms that match the FreeType backend's: its
  embolden strength (`max(0.5px, ppem/24)`, advance grown to match) and its 12° slant. They
  apply to variable instances, to every color layer and before distance-field generation.
- **Distance fields** — `Sdf` is computed from the outline and matches FreeType's SDF output
  (encoding, padding, `SdfSpread`); multi-channel `SdfMode.Msdf` / `SdfMode.Mtsdf` use the
  same encoding and padding as RGBA. Native is the only backend with MSDF/MTSDF.
- **Color fonts** — with `ColorFont`, `COLR` version 1 paint graphs (every Paint format:
  gradients, transforms, composite modes, variable paints) and version 0 layers with `CPAL`
  palettes render as straight-alpha RGBA, and `ColorPaletteIndex` selects the palette. Version 1
  gradients and composites are computed in linear light, as the `COLR` spec requires. Glyphs with
  no color definition render as grayscale. Color glyphs stay color under single-channel SDF;
  MSDF/MTSDF with `ColorFont` is rejected.
- **Anti-aliasing** — `None` (50% threshold with dropout control, boxes cropped to the ink),
  `Grayscale`, `Light` (grayscale plus the auto-hinter below) and `Lcd` (see LCD below). SDF and
  MSDF take precedence over LCD, and color glyphs ignore LCD, as on FreeType.
- **LCD** — `Lcd` renders RGBA glyphs, one subpixel per colour channel, alpha = the strongest
  subpixel. `LcdSubpixelOrder` picks the panel: `Rgb` (default) or `Bgr` stripes side by side,
  or `VRgb`/`VBgr` stacked (rows resolved in thirds, so horizontal edges fringe). `LcdFilter`
  picks the low-pass kernel: `Default`/`Light` = one-pixel box [1,1,1]/3 (per-channel match for
  the FreeType backend's LCD mode), `Smooth` = [1,2,3,2,1]/9 (softer fringes), `None` = raw
  third-pixel coverage. `.bmfc`: `lcdOrder=`, `lcdFilter=`; CLI: `--lcd-order`, `--lcd-filter`.
  With `SuperSampleLevel > 1` core renders LCD at the final size (super sampling would average
  the subpixels away), so the output equals the non-supersampled one.

Not supported — each raises a clear error or is ignored, as noted:

- **Outline stroking** (Phase 168) — the backend has none of its own; KernSmith core still draws outlines as a bitmap effect
- **Bitmap-only color fonts** (`CBDT`/`sbix`, e.g. Noto Color Emoji) — rejected at `LoadFont` with a `RasterizationException`
- **TrueType bytecode hinting** — never run; the auto-hinter is Native's own and fits y only
- **System fonts by name** — `LoadSystemFont` throws `NotSupportedException`; load font bytes instead
- **`Gamma`** — not applied in any mode

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

It applies to TrueType and CFF outlines and to `COLR` version 0 layers and version 1 glyph clips.
Every other request, including the default (`Grayscale` with `EnableHinting` true), `Lcd` and
every distance field (SDF, MSDF, MTSDF), renders unhinted. A font with no x-height and no cap height (no Latin reference glyphs, no `OS/2`
heights) renders unhinted rather than snapped to zones that do not describe it.

## Errors, limits and threads

- **Damaged fonts** fail with KernSmith exceptions, never a crash or hang: a malformed table
  directory or core table is a `FontFormatException` at `LoadFont` (a bitmap-only color font is
  a `RasterizationException`); malformed glyph, variation or color data is a
  `RasterizationException` from `RasterizeGlyph`/`GetGlyphMetrics`/`SetVariationAxes`. A seeded
  fuzz harness (`NativeFuzzTests`) holds this for every committed fixture.
- **Size limits** — a glyph box larger than 64 ems, or a bitmap larger than 16384 px per side
  (distance-field padding included), is a `RasterizationException`, so damaged metrics or a huge
  `SdfSpread`/DPI cannot allocate without bound. `SdfSpread` below one pixel (and NaN) pads by one.
- **Options** — a `Size` that is not a positive finite number, or a negative `Dpi`, is an
  `ArgumentOutOfRangeException` (a `Dpi` of zero means 72, as on FreeType); so is a palette index
  the font's `CPAL` lacks. Variation values
  outside an axis's range clamp to its nearest end; NaN leaves the axis at its default.
- **Threads** — an instance is not thread-safe. KernSmith core never shares one: each
  `BmFont.Generate` (and each `GenerateBatch` job) creates its own, so parallel generation is
  safe and byte-identical to serial.

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
disagree — except where the box alone decides the metrics (below), and there
`NativeMetricsEquivalenceTests` holds them equal for every mapped codepoint, fixture and mode.
Everything is internal; add a feature to its stage rather than to `NativeRasterizer`.

| # | Stage | Code | Add a feature by |
|---|-------|------|------------------|
| 1 | Decode → `DecodedGlyph` (cubic outline, advance, optional declared bbox; font units) | `Internal/Outlines/IOutlineDecoder.cs`, `TrueTypeOutlineDecoder.cs`, `CffOutlineDecoder.cs` (interpreter in `Internal/Cff/`); variations in `Internal/Variations/` | implementing `IOutlineDecoder` and returning it from `OutlineDecoders.ForFace`. Variation deltas live inside the decoder: both decoders receive the face's `FontVariations` (set by `SetVariationAxes`); `TrueTypeOutlineDecoder` applies `gvar`; `CffOutlineDecoder` feeds CFF2 `blend` per-`vsindex` region scalars through `CharstringContext.BlendScalars`. |
| 2 | Transforms, in order, in font units | `Internal/Pipeline/IGlyphTransform.cs`, `GlyphTransforms.cs` | implementing `IGlyphTransform` and adding it in `GlyphTransforms.For` — `BoldTransform` then `ItalicTransform` today (167). `WidthScaleTransform`, `SmallCapsTransform` (before bold) and `StrokeTransform` (last, as a separate layer) exist but are not registered until core options reach them (168). Build results with `WithOutline` / `WithAdvanceWidth`. |
| 3 | Bounds → pixel grid | `Internal/Raster/GlyphBox.cs`, `Internal/Outlines/OutlineBounds.cs` | nothing: a replaced outline loses its declared bbox and is measured from its tight bounds. |
| 4 | Scale, then pixel-space hinting | `Internal/Pipeline/GlyphPipeline.cs`, `GlyphHinter.cs`; `Internal/Hinting/` | `GlyphHinter.Apply` runs the auto-hinter (174) for hinted requests: `FontHintMetrics` (per face/size/style/instance, cached) and `GridFitter`. It returns a pixel-space outline with an identity `ToPixels`. |
| 5 | Render-mode dispatch | `Internal/Pipeline/GlyphRenderers.cs`, `IGlyphRenderer.cs`, `CoverageRenderer.cs`, `SdfRenderer.cs`, `MsdfRenderer.cs` (distance code and edge coloring in `Internal/Distance/`), `LcdRenderer.cs` (kernels in `Internal/Raster/LcdFilterKernel.cs`), `ColorLayerRenderer.cs`, `PaintGraphRenderer.cs` | implementing `IGlyphRenderer` and returning it from `GlyphRenderers.Select` (distance fields first, then LCD, else coverage). Color glyphs (172) wrap that renderer in `GlyphRenderers.SelectForFace`: `ColorLayerRenderer` sends `COLR` version 1 paint graphs to `PaintGraphRenderer` and draws version 0 layers itself, preparing every clip or layer glyph through stages 1–4. `Measure` must match what `Render` produces. |

Invariants:

- Renderers map outline coordinates through `PreparedGlyph.ToPixels`, never a scale of their
  own — a hinter may return an outline already in pixel space.
- A TrueType outline is placed by its left side bearing phantom point, pp1 = `xMin - lsb`
  (a `USE_MY_METRICS` component's, for such a composite; plus its `gvar` delta), which the
  decoder puts at x = 0 — so the outline sits at the `hmtx` bearing even when it disagrees with
  the `glyf` xMin. The declared header box is moved with it. A `USE_MY_METRICS` composite takes
  the component's advance too (with the component's `HVAR` or phantom-point delta at an instance).
- Untransformed TrueType glyphs size from the `glyf` header bbox, falling back to the control
  hull when it scales to nothing. A stale header — one that misses a stored point by more than a
  unit and the drawn outline too — is replaced by the points' control box (FreeType's box), so a
  damaged header can neither clip nor shift the ink; correct headers are used unchanged. A glyph whose outline a variation moved drops the header box
  and is measured tightly. `NativeOutputCharacterizationTests` pins the resulting bytes;
  a deliberate pixel change updates its hashes.
- `AntiAliasMode.None` output is cropped to its ink after rendering (`ScanlineRasterizer.RasterizeMono`;
  color glyphs in `ColorLayerRenderer`): thresholding empties edge rows and columns of the
  outward-rounded box. A None `Measure` therefore renders too (into pooled scratch).
- Blank glyphs (no outline, or an empty box) never reach a renderer — except a `COLR` color glyph, whose own outline is not what gets drawn and is often empty.
- `PreparedGlyph.GlyphIndex` is the only way `Measure` can identify the glyph; a hint stage must carry it over (build with `with`).
- The default instance (no axes, or every axis at its default) takes the exact static decode
  path, so non-variable output cannot drift.
- Metrics shortcut: `GetGlyphMetrics` skips decoding an untransformed, unhinted, default-instance
  simple `glyf` glyph and measures its header box once its stored coordinates (read, not
  decoded into an outline) show the header holds them (`IOutlineDecoder.TryDecodeBounds` →
  `GlyphPipeline.TryPrepareBox`, which applies the same `GlyphLimits`), but only for a renderer
  implementing `IBoxMeasuringRenderer` whose `MeasuresFromBox` is true for the request (coverage
  except `None`, SDF, MSDF). A mode whose extent reads the outline or pixels must answer false.
- Per-glyph allocation is what the glyph keeps (bitmap, outline commands, the exact edge array,
  small result objects); `NativeAllocationTests` pins it. Simple `glyf` glyphs decode through
  the decoder's `GlyfScratch` (per instance — instances are single-threaded); the extractor and
  flattener rent from `ArrayPool<T>.Shared`, because renderers are shared singletons.

## Golden masters (contributors)

`NativeGoldenMasterTests` and its sibling classes pin Native's exact output — every fixture
font across sizes, anti-alias modes, distance fields, styles, variation instances and color, and
`BmFont.Generate` output — against `tests/KernSmith.Rasterizers.Native.Tests/Goldens/`. Any
changed byte fails, naming the changed glyphs and writing actual (and, for reference cases,
expected and diff) images under `GoldenFailures/` in the test output folder. After a deliberate
output change, regenerate and review the diff:

```
KERNSMITH_UPDATE_GOLDENS=1 dotnet test --project tests/KernSmith.Rasterizers.Native.Tests -- --filter-class "*NativeGolden*"
```

`NativeSimilarityMatrixTests` scores Native against FreeType and stb_truetype; its measured table
is in `plan/phase-179-validation-golden-masters.md`.

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project
documentation.
