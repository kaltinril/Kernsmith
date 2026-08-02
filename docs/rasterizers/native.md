# Native (Experimental)

KernSmith's own rasterizer backend -- a font rasterizer written entirely in C# inside this repository, with no native libraries and no third-party font code. Everything from SFNT table parsing to the scanline fill is KernSmith's own.

> [!IMPORTANT]
> This backend is **experimental** and **not published to NuGet**. `RasterizerBackend.Native` resolves only in builds that reference the `KernSmith.Rasterizers.Native` project directly (a source build) or in the KernSmith CLI, which ships it. In an app built against the released NuGet packages, `RasterizerFactory.Create(RasterizerBackend.Native)` throws "not registered". Use [FreeType](freetype.md) or [StbTrueType](stbtruetype.md) for production work.

**Platform:** Cross-platform (net8.0, net10.0). No native binaries required.

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
- TrueType Collections (`.ttc`) via face index
- `cmap` formats 4 and 12 for character lookup
- Anti-aliasing: `Grayscale` and `None` only
- Super sampling (applied by the core pipeline)
- Font and glyph metrics that track FreeType to within a pixel

## Limitations

Everything below is either rejected with a clear error or simply unsupported:

- **CFF/OTF (PostScript) outlines** -- rejected at load time with a `RasterizationException`. This is the one that bites: a `.otf` file will not render at all.
- **WOFF / WOFF2** -- not supported; supply raw TTF/TTC bytes
- **Hinting** -- none, so small sizes are softer than FreeType or GDI output
- **SDF rendering** -- not supported
- **Outline stroke** -- not supported
- **Synthetic bold and italic** -- not supported. `Bold` and `Italic` are silently ignored rather than rejected, so requesting them produces regular-weight glyphs. For a bitmap-level approximation, add `BoldPostProcessor` / `ItalicPostProcessor` to `PostProcessors` and leave `Bold` / `Italic` unset (the pipeline skips those post-processors when the matching option is set, on the assumption the backend handled it).
- **Variable fonts** and **color fonts (COLR/CPAL)** -- not supported
- **System font loading by name** -- not supported; load font bytes instead
- **`Light` and `LCD` anti-aliasing** -- not supported

## When to Use

Use Native when you are building KernSmith from source and want plain TrueType text rendered with no third-party dependencies whatsoever -- for example to audit the whole rasterization path, or to avoid pulling in a vendored C library. For shipping managed-only builds today, use [StbTrueType](stbtruetype.md): it is published, AOT-tested, and adds SDF plus synthetic bold/italic.
