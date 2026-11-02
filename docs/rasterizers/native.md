# Native (Experimental)

KernSmith's own rasterizer backend -- a font rasterizer written entirely in C# inside this repository, with no native libraries and no third-party font code. Everything from SFNT table parsing to the scanline fill is KernSmith's own.

> [!IMPORTANT]
> This backend is **experimental** and **not published to NuGet**. `RasterizerBackend.Native` resolves only in builds that reference the `KernSmith.Rasterizers.Native` project directly (a source build) or in the KernSmith CLI, which ships it. In an app built against the released NuGet packages, `RasterizerFactory.Create(RasterizerBackend.Native)` throws "not registered". Use [FreeType](freetype.md) or [StbTrueType](stbtruetype.md) for production work.

**Platform:** Cross-platform (net8.0, net10.0). No native binaries required. Verified on Windows, Linux (x64 and arm64), macOS, Android, iOS (simulator), Mac Catalyst and Blazor WASM (interpreted build). See the [platform support table](../../README.md#platform-support).

## Usage

The rasterizer auto-registers via `[ModuleInitializer]` -- referencing the project is sufficient. Select it in options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    Backend = RasterizerBackend.Native
};

var result = BmFont.Generate("path/to/font.ttf", options);
```

From the CLI:

```
kernsmith generate --font Roboto-Regular.ttf --size 32 --rasterizer native --output roboto
```

`kernsmith list-rasterizers` shows it alongside the other backends.

## Capabilities

- TrueType (`glyf`) outlines
- PostScript (`CFF ` / `CFF2`) outlines -- `.otf` files, including CID-keyed fonts
- TrueType Collections (`.ttc`) via face index
- WOFF and WOFF2 files wrapping either outline format (KernSmith core decompresses them before the backend sees the bytes)
- `cmap` formats 4 and 12 for character lookup, including the Windows symbol (3,0) subtable of fonts such as Wingdings, Webdings and Symbol: each glyph is reached at its U+F0xx code and at its byte decoded as Windows-1252, as BMFont does (`KernSmith.Font.SymbolEncoding`)
- Anti-aliasing:
  - `Grayscale` -- exact-area coverage, hinted at `Normal` by default (below)
  - `None` -- a 50% coverage threshold with dropout control, so strokes thinner than half a pixel still leave a pixel; each glyph box is cropped to its ink
  - `Light` -- grayscale coverage; the anti-alias mode no longer chooses a hinter. `HintLevel.Light` (`.bmfc` `hintLevel=light`, CLI `--hint-level light`) is Native's vertical-only fitter in any mode: baseline, x-height, cap-height, ascender and descender zones and horizontal strokes snap to the pixel grid vertically, advances and horizontal positions stay unhinted. `HintLevel.None` renders unhinted
- Hint levels (`HintLevel`; `.bmfc` `hintLevel=`, CLI `--hint-level`): `Normal` is Native's both-axis auto-hinter for `Grayscale` and `None`, and what a request with no level gets while `EnableHinting` is on -- vertical stems land in whole pixel columns (a sub-pixel stem inside one column at its true coverage, not widened), stem pitches and the font's standard stem width are kept consistent, the y axis is stretched so the x-height lands on a whole row, and every advance stays the font's with the fitted ink placed inside it, so small text is as crisp as the FreeType backend's and a line is exactly as long as unhinted; in bilevel the fit happens before the 50% threshold with whole-pixel stems and counters of at least one pixel. It is at full strength up to 16 ppem; the stem fitting fades out by 24 ppem and the vertical alignment by 40, above which output is exactly unhinted. Pairs the fit leaves touching are opened in the kerning pairs (`BakeHintingIntoKerning`, on by default under `Normal`; `--no-bake-kerning` to turn it off); a fixed-pitch font keeps every advance and gets no pairs. A TrueType font with its own bytecode that asks for integer ppem (`head.flags` bit 3: Arial, Verdana, Tahoma, Georgia and most hinted system fonts) is fitted and rendered at `round(ppem)`, as FreeType and GDI render it, with the font's linear advances rounded at that ppem in both anti-alias modes (its `hdmx` device advances are not used: they go with the bytecode's own outlines, which Native does not draw, and set Native's ink loose), so the designer's x-height and cap rows come out; a font's `gasp` table is honoured, so at the sizes it says not to grid-fit (8 ppem and under for the Microsoft fonts) nothing is fitted. `Full` steps down to `Normal` (there is no TrueType bytecode interpreter or CFF hinter yet)
  - `Lcd` -- LCD subpixel RGBA glyphs, one subpixel per colour channel, alpha = the strongest subpixel. `LcdSubpixelOrder` selects `Rgb` (default) or `Bgr` stripes, or vertical `VRgb`/`VBgr` panels; `LcdFilter` selects `Default`/`Light` (a one-pixel box -- per-channel output matches the FreeType backend's LCD mode), `Smooth` (a five-tap [1,2,3,2,1]/9 kernel, softer fringes) or `None` (unfiltered). `.bmfc`: `lcdOrder=`, `lcdFilter=`; CLI: `--lcd-order`, `--lcd-filter`
- Super sampling (applied by the core pipeline; `Lcd` output always renders at the final size)
- Variable fonts, TrueType and CFF2: `VariationAxes` (or `variationAxes` in a `.bmfc`, `--axis` or `--instance` on the CLI) selects the instance, and outlines, advances and line metrics follow it
- Color fonts (`ColorFont`, `ColorPaletteIndex`): `COLR` version 1 paint graphs (gradients, transforms, composite modes, variable paints) and version 0 layers with `CPAL` palettes, as straight-alpha RGBA. Version 1 gradients and composites are computed in linear light, as the `COLR` spec requires, so anti-aliased edges between overlapping colored layers come out lighter than in a browser. Glyphs with no color definition render as grayscale; color glyphs ignore `Lcd`, as on FreeType
- Color bitmap glyphs (`ColorFont`): `CBDT`/`CBLC` and `sbix` PNG glyphs, so bitmap-only fonts such as Noto Color Emoji work. Each glyph uses the smallest strike at or above the requested size (else the largest), scaled to that size -- the FreeType backend returns the strike's own size instead; at a strike's size the two match. `COLR` wins over a bitmap, a bitmap over the outline. Without `ColorFont`, a bitmap-only font renders grayscale ink coverage. PNGs are decoded by KernSmith's own `KernSmith.Imaging` decoder
- Synthetic bold and italic (`Bold` / `Italic`), applied to the outline before rasterizing and matched to the FreeType backend: the same emboldening strength (growing the advance to match) and the same 12° slant. They apply to every color layer, to variable-font instances and to distance fields too (bitmap color glyphs are left unstyled, as on FreeType). Caller-supplied `BoldPostProcessor` / `ItalicPostProcessor` are skipped when the matching flag is set, as on every other backend
- Synthetic width and slant -- the only backend with them: `WidthPercent` (10-1000; `.bmfc` `widthPercent`, CLI `--width-percent`) condenses or expands outlines, advances and kerning; `ItalicAngle` (±60°, default 12; `.bmfc` `italicAngle`, CLI `--italic-angle`) sets the synthetic italic's slant
- Vector outlines -- the only backend that draws `OutlineMode.Vector` (`.bmfc` `outlineMode=vector`, CLI `--outline-mode vector`): the outline ring is computed from the glyph's true outline with the pen `OutlineShape` names, at the same width, color, cells and offsets as core's bitmap outline effect, but with the body's exact-area edge and solid narrow counters. A face with `COLR` or bitmap color glyphs keeps the bitmap effect under `ColorFont`
- Signed distance fields (`Sdf`), computed from the outline itself. `SdfSpread` and `SdfScale` are honored, and the output uses FreeType's encoding and padding (inside bright, 128 on the edge, the glyph box padded by the spread). With `ColorFont`, color glyphs stay color and the rest render as SDF, as on FreeType
- Multi-channel distance fields (`SdfMode.Msdf` / `SdfMode.Mtsdf`) -- the only backend that reports `SupportsMsdf`. RGB hold three distances whose median keeps corners sharp; MTSDF adds the single-channel field in alpha. They cannot be combined with `ColorFont`
- `Gamma` applied to grayscale coverage with the FreeType backend's contract (1.8 is the identity; not to mono, LCD, distance fields or color glyphs)
- Font and glyph metrics that track FreeType to within a pixel. Unhinted grayscale output measures a mean SSIM of 0.988 or better per font and size against both FreeType and StbTrueType

## Errors and limits

- A damaged font fails with a KernSmith exception, never a crash or hang: a malformed table directory or core table is a `FontFormatException` at load (as are malformed color bitmap tables in a font with no outlines); malformed glyph, variation or color data (a bitmap glyph's PNG included) is a `RasterizationException` for that glyph
- A glyph box larger than 64 ems, or a bitmap larger than 16384 px per side (distance-field padding included), is a `RasterizationException`
- A `Size` that is not a positive finite number, or a negative `Dpi`, is an `ArgumentOutOfRangeException` (a `Dpi` of zero means 72, as on FreeType), as is a palette index the font's `CPAL` lacks. Variation values outside an axis's range clamp to its nearest end
- A distance field without `ColorFont` on a bitmap-only color font is a `NotSupportedException`
- A rasterizer instance is not thread-safe; KernSmith creates one per `Generate` call (and per `GenerateBatch` job), so parallel generation is safe

## Limitations

Each item below either raises a clear error or is ignored, as noted:

- **Hinting** -- Native's own auto-hinter only (`Light` vertical, `Normal` both axes); there is no TrueType bytecode interpreter and no CFF hinter, so `Full` steps down to `Normal` and a font with its own hinting does not get its bytecode's glyph shapes -- `Normal` does render such a font at the integer ppem it was hinted for, honouring its `gasp` ranges, so its sizes and rows match FreeType's and GDI's even where the pixels differ; its advances are the linear ones at that ppem (FreeType's grayscale advances), not the `hdmx` table's, which go with the bytecode's own outlines. The default request (no explicit level, `EnableHinting` on) is `Normal` with its size taper; `EnableHinting = false` is unhinted, and `Lcd` and distance fields are never hinted
- **Vector outlines** -- not for color glyphs (they keep the bitmap outline effect); with SDF/MSDF, `OutlineMode.Vector` is rejected
- **Color bitmaps** -- only PNG strikes: `'jpg '`/`'tiff'` `sbix` graphics and uncompressed `EBDT`-style formats are skipped (the outline, or nothing, draws instead)
- **Variable fonts** -- a font whose `avar` table is version 2 gets only its version 1 axis mappings. The library takes axis values; the CLI's `--instance` resolves a named instance to its axis values
- **System font loading by name** -- not supported; load font bytes instead

## Same bytes on every host

Native produces byte-identical atlas pixels and `.fnt` metrics for a given font and options on every runtime and machine: the CLI, the desktop app, MonoGame/KNI, Blazor WebAssembly (interpreted or AOT), NativeAOT, x64 and arm64, .NET 8 and .NET 10. Its arithmetic is IEEE 754 operations every runtime computes exactly, and the transcendental functions it needs come from its own `DeterministicMath` rather than the host C library. CI proves it on every run: the Native test project asserts the committed hash manifest (`tests/KernSmith.Rasterizers.Native.Tests/Determinism/manifest.json`, the SHA-256 of the `.fnt` and atlas pixels per configuration) on Windows, Linux and macOS, x64 and arm64; the published Blazor WASM sample reports the same hash from headless Chromium; and a NativeAOT build of `tests/KernSmith.DeterminismProbe` reports it from a native binary. Promised are the pixels and metrics, not the `.png` file bytes (the PNG encoder's deflate stream follows the runtime's zlib). See `TESTING-NATIVE.md` in the repository to check your own host.

## When to Use

Use Native when you are building KernSmith from source and want text rendered with no third-party code at all -- for example to audit the whole rasterization path, to run the same rasterizer on every platform including iOS and Mac Catalyst, or for what no other backend produces: multi-channel MSDF/MTSDF, vector outlines, synthetic width and italic angle, and selectable LCD subpixel order and filter. For shipping managed-only builds from NuGet today, use [StbTrueType](stbtruetype.md): it is published.
