# Native (Experimental)

KernSmith's own rasterizer backend -- a font rasterizer written entirely in C# inside this repository, with no native libraries and no third-party font code. Everything from SFNT table parsing to the scanline fill is KernSmith's own.

> [!IMPORTANT]
> This backend is **experimental** and **not published to NuGet**. `RasterizerBackend.Native` resolves only in builds that reference the `KernSmith.Rasterizers.Native` project directly (a source build) or in the KernSmith CLI, which ships it. In an app built against the released NuGet packages, `RasterizerFactory.Create(RasterizerBackend.Native)` throws "not registered". Use [FreeType](freetype.md) or [StbTrueType](stbtruetype.md) for production work.

**Platform:** Cross-platform (net8.0, net10.0). No native binaries required. Verified on Windows, Linux (x64 and arm64), macOS, Android, iOS (simulator), and Mac Catalyst; Blazor WASM is expected to work but not yet verified. See the [platform support table](../../README.md#platform-support).

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
- PostScript (`CFF ` / `CFF2`) outlines -- `.otf` files, including CID-keyed fonts; a CFF2 variable font renders its default instance
- TrueType Collections (`.ttc`) via face index
- WOFF and WOFF2 files wrapping either outline format (KernSmith core decompresses them before the backend sees the bytes)
- `cmap` formats 4 and 12 for character lookup
- Anti-aliasing: `Grayscale` and `None` only
- Super sampling (applied by the core pipeline)
- Color fonts with `COLR` version 0 layers and `CPAL` palettes (`ColorFont`, `ColorPaletteIndex`); glyphs come out as straight-alpha RGBA
- Synthetic bold and italic (`Bold` / `Italic`), applied to the outline before rasterizing and matched to the FreeType backend: the same emboldening strength (growing the advance to match) and the same 12° slant. They apply to every `COLR` layer and to variable-font instances too. Caller-supplied `BoldPostProcessor` / `ItalicPostProcessor` are skipped when the matching flag is set, as on every other backend
- Signed distance fields (`Sdf`), computed from the outline itself. `SdfSpread` and `SdfScale` are honored, and the output uses FreeType's encoding and padding (inside bright, 128 on the edge, the glyph box padded by the spread)
- Multi-channel distance fields (`SdfMode.Msdf` / `SdfMode.Mtsdf`) -- the only backend that reports `SupportsMsdf`. RGB hold three distances whose median keeps corners sharp; MTSDF adds the single-channel field in alpha
- Font and glyph metrics that track FreeType to within a pixel

## Limitations

Each item below either raises a clear error or is ignored, as noted:

- **Hinting** -- none, so small sizes are softer than FreeType or GDI output. With `AntiAlias.None` there is no dropout control either, so hairline strokes thinner than half a pixel (a light weight at a small size) can drop out where FreeType keeps them
- **Outline stroke** -- the backend has no stroke of its own; outlines (`Outline` / `outlineThickness`) still work because KernSmith core draws them as a bitmap effect for every backend
- **CFF2 variable instances** -- a CFF2 font renders its default instance whatever axes are set. TrueType variable fonts work: `VariationAxes` (or `variationAxes` in a `.bmfc`) selects the instance, and outlines, advances and line metrics follow it. A font whose `avar` table is version 2 gets only its version 1 axis mappings
- **Color fonts beyond `COLR` version 0** -- `COLR` version 1 glyphs (gradients, transforms) render as plain grayscale coverage, the same as with `ColorFont` off. Bitmap-only color fonts (`CBDT`/`sbix`, such as Noto Color Emoji) are rejected at load time with a `RasterizationException`; use [FreeType](freetype.md) for those
- **System font loading by name** -- not supported; load font bytes instead
- **`Light` and `LCD` anti-aliasing** -- not supported; requesting them silently renders `Grayscale`

## When to Use

Use Native when you are building KernSmith from source and want plain TrueType or OpenType text rendered with no third-party dependencies whatsoever -- for example to audit the whole rasterization path, or to avoid pulling in a vendored C library. For shipping managed-only builds today, use [StbTrueType](stbtruetype.md): it is published and AOT-tested.
