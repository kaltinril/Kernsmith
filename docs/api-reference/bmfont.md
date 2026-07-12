# BmFont -- Entry Point

Namespace: `KernSmith`

`BmFont` is a `static` class. It is the single entry point for generating, querying, and
loading bitmap fonts.

## Generating a font

| Method | Description |
|--------|-------------|
| `Generate(byte[] fontData, FontGeneratorOptions? options = null)` | Generate from raw TTF/OTF/WOFF bytes. |
| `Generate(byte[] fontData, int size)` | Convenience overload using default options at the given size. |
| `Generate(string fontPath, FontGeneratorOptions? options = null)` | Generate from a font file on disk. |
| `Generate(string fontPath, int size)` | Convenience overload using default options at the given size. |
| `GenerateFromSystem(string fontFamily, FontGeneratorOptions? options = null)` | Generate from a system-installed font, looked up by family name (e.g. `"Arial"`). |
| `GenerateFromSystem(string fontFamily, int size)` | Convenience overload using default options. |

All `Generate*` methods return a [`BmFontResult`](result.md).

> WOFF and WOFF2 input is auto-detected and decompressed before parsing. (Note: WOFF2 is not
> supported at runtime and throws `NotSupportedException`; only WOFF is decompressed.)

```csharp
// From a file
var result = BmFont.Generate("font.ttf", new FontGeneratorOptions { Size = 48 });

// From bytes (e.g. an embedded resource or downloaded font)
byte[] bytes = File.ReadAllBytes("font.otf");
var result2 = BmFont.Generate(bytes, 32);

// From an installed system font
var result3 = BmFont.GenerateFromSystem("Segoe UI", 24);
```

## Generating from a config file

| Method | Description |
|--------|-------------|
| `FromConfig(string bmfcPath)` | Generate from a `.bmfc` or `.hiero` config file (format auto-detected by inspecting content; extension is only a fallback). |
| `FromConfig(BmfcConfig config)` | Generate from an already-parsed config. `BmfcConfig` is the shared model for both formats. |

```csharp
var result = BmFont.FromConfig("MyFont.bmfc");
```

## Loading existing BMFont output

| Method | Description |
|--------|-------------|
| `Load(string fntPath)` | Load an existing `.fnt` (text/XML/binary auto-detected) and its `.png` atlas pages from the same directory. |
| `LoadModel(byte[] fntData)` | Parse a `.fnt` descriptor from bytes. Does not load atlas images. |
| `LoadModel(string fntContent)` | Parse a text-format `.fnt` from a string. Does not load atlas images. |

`Load` returns a [`BmFontResult`](result.md); the `LoadModel` overloads return a
[`BmFontModel`](model.md).

## Reading font metadata

| Method | Description |
|--------|-------------|
| `ReadFontInfo(byte[] fontData, int faceIndex = 0)` | Read family name, metrics, available codepoints, and kerning without generating a font. |
| `ReadFontInfo(string fontPath, int faceIndex = 0)` | Same, from a file on disk. |

Returns a `KernSmith.Font.Models.FontInfo`.

## Querying atlas size

These estimate the atlas dimensions and page count without rasterizing glyphs.

| Method | Description |
|--------|-------------|
| `QueryAtlasSize(byte[] fontData, FontGeneratorOptions? options = null)` | Estimate from raw bytes. |
| `QueryAtlasSize(string fontPath, FontGeneratorOptions? options = null)` | Estimate from a file. |
| `QueryAtlasSizeFromSystem(string fontFamily, FontGeneratorOptions? options = null)` | Estimate from a system font. |

Each returns an `AtlasSizeInfo` (`Width`, `Height`, `PageCount`, `GlyphCount`,
`EstimatedEfficiency`).

## Fluent builder

| Method | Description |
|--------|-------------|
| `Builder()` | Returns a new [`BmFontBuilder`](builder.md) for chained configuration. |

## Batch generation

| Method | Description |
|--------|-------------|
| `GenerateBatch(IReadOnlyList<BatchJob> jobs, BatchOptions? options = null)` | Generate multiple fonts, optionally in parallel with font caching. Returns a `BatchResult` with per-job outcomes and timing. |

## Font registration

On platforms without system font access (e.g. Blazor WASM, containers), register raw font
data so `GenerateFromSystem` can resolve it by family name. Registered fonts take priority
over system fonts.

| Method | Description |
|--------|-------------|
| `RegisterFont(string familyName, byte[] fontData, string? style = null, int faceIndex = 0)` | Register raw font bytes under a family name (and optional style such as `"Bold"`). |
| `UnregisterFont(string familyName, string? style = null)` | Remove a previously registered font. Returns `true` if one was removed. |
| `ClearRegisteredFonts()` | Remove all registered fonts. |

```csharp
BmFont.RegisterFont("MyFont", fontBytes);
BmFont.RegisterFont("MyFont", boldBytes, style: "Bold");

var result = BmFont.GenerateFromSystem("MyFont", new FontGeneratorOptions
{
    Size = 32,
    Bold = true
});
```

## Font location hints

If you already know where a system font lives on disk, `HintFontLocation` is a lighter-weight
alternative to `RegisterFont` — it skips OS-specific resolution (Windows registry, filename
heuristics, full directory scan) for that family without loading the font's bytes into memory
up front. The hint is validated the same way as any other cached/seeded entry (the file must
exist and its parsed family name must match) before it's trusted, so a wrong hint just falls
through to normal resolution.

| Method | Description |
|--------|-------------|
| `HintFontLocation(string familyName, string path, int faceIndex = 0)` | Pre-populate the system font resolver's cache with a known file path for a family name. |

```csharp
BmFont.HintFontLocation("Arial", @"C:\Windows\Fonts\arial.ttf");

var result = BmFont.GenerateFromSystem("Arial", new FontGeneratorOptions { Size = 32 });
```

If a family name resolves through the expensive full directory scan (the last-resort tier),
`DefaultSystemFontProvider` logs a `Trace.TraceInformation` message naming that family — a
good signal to add a hint (or call `RegisterFont`) for it. See
[System Font Resolution](../core/system-font-resolution.md) for the full fallback chain, the
built-in seed table, and what each diagnostic message means.
