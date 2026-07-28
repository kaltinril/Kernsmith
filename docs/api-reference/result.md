# BmFontResult -- Generation Output

Namespace: `KernSmith.Output`

Returned by every `BmFont.Generate*`, `BmFont.FromConfig`, `BmFont.Load`, and
`BmFontBuilder.Build()` call. It is immutable; the `.fnt` text/XML/binary representations are
computed lazily on first access.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Model` | [`BmFontModel`](model.md) | The parsed BMFont descriptor. |
| `Pages` | `IReadOnlyList<AtlasPage>` | The rendered atlas pages (raw pixel data). |
| `FailedCodepoints` | `IReadOnlyList<int>` | Requested codepoints that could not be rasterized (missing from the font). |
| `Metrics` | `PipelineMetrics?` | Per-stage timing. Only populated when `CollectMetrics` was enabled. |
| `VariantModels` | `IReadOnlyDictionary<string, BmFontModel>` | Additional character-set renderings requested via `FontGeneratorOptions.Variants` (e.g. a dropshadow silhouette), keyed by variant name. Empty when no variants were requested. See [Atlas Variants](../core/atlas-variants.md). |
| `VariantPages` | `IReadOnlyDictionary<string, IReadOnlyList<AtlasPage>>` | Atlas pages for each entry in `VariantModels`, keyed the same way. |

## Descriptor (.fnt) content

| Member | Type | Description |
|--------|------|-------------|
| `FntText` | `string` | The descriptor in BMFont text format. |
| `FntXml` | `string` | The descriptor in BMFont XML format. |
| `FntBinary` | `byte[]` | The descriptor in BMFont binary format (a fresh copy). |
| `ToString()` | `string` | Same as `FntText`. |
| `ToXml()` | `string` | Same as `FntXml`. |
| `ToBinary()` | `byte[]` | Same as `FntBinary`. |
| `GetVariantFntText(string variantName)` | `string` | The `.fnt` text for a variant produced via `FontGeneratorOptions.Variants`, keyed by name as in `VariantModels`. Throws `KeyNotFoundException` if no variant with that name exists. |

## Atlas image bytes

| Member | Returns | Description |
|--------|---------|-------------|
| `GetPngData()` | `byte[][]` | PNG bytes for every page. |
| `GetPngData(int pageIndex)` | `byte[]` | PNG bytes for one page. |
| `GetTgaData()` / `GetTgaData(int)` | `byte[][]` / `byte[]` | TGA bytes. |
| `GetDdsData()` / `GetDdsData(int)` | `byte[][]` / `byte[]` | DDS bytes. |

For direct GPU upload of raw pixels (without re-encoding), use the helper methods on each
`AtlasPage` in `Pages`: `GetRgbaPixelData()`, `GetAlpha8PixelData()`, and
`GetPremultipliedRgbaPixelData()`.

## Config export

| Member | Returns | Description |
|--------|---------|-------------|
| `ToBmfc()` | `string` | The equivalent BMFont/AngelCode `.bmfc` config. Throws `InvalidOperationException` if this result was loaded from disk rather than generated. |
| `ToHiero()` | `string` | The equivalent libGDX Hiero `.hiero` config. Same `InvalidOperationException` condition. |

## Writing to disk

| Member | Description |
|--------|-------------|
| `ToFile(string outputPath, OutputFormat format = OutputFormat.Text)` | Writes the `.fnt` descriptor, the atlas page images, and (when source options are available) a matching `.bmfc` config. `outputPath` is a base path without extension, e.g. `"output/myfont"`. |

```csharp
var result = BmFont.Generate("font.ttf", new FontGeneratorOptions { Size = 32 });

// Write font.fnt, font_0.png, font.bmfc
result.ToFile("output/font");

// Or work entirely in memory
string fnt = result.FntText;
byte[] page0 = result.GetPngData(0);
```
