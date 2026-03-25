# KernSmith -- Data Types

> Defines all intermediate model types used across the pipeline. Each plan document references this file for type definitions.

## Pipeline Data Flow

```
byte[] → FontInfo → RasterizedGlyph[] → GlyphRect[] → PackResult → AtlasPage[] + BmFontModel → BmFontResult
```

## Font Layer Types

### FontInfo

`sealed class` with init-only properties.

```csharp
public sealed class FontInfo
{
    public required string FamilyName { get; init; }
    public required string StyleName { get; init; }
    public required int UnitsPerEm { get; init; }
    public required int Ascender { get; init; }          // in font units
    public required int Descender { get; init; }         // in font units, negative
    public required int LineGap { get; init; }           // in font units
    public int LineHeight => Ascender - Descender + LineGap; // computed
    public required bool IsBold { get; init; }
    public required bool IsItalic { get; init; }
    public required bool IsFixedPitch { get; init; }
    public required int NumGlyphs { get; init; }
    public required IReadOnlyList<int> AvailableCodepoints { get; init; }       // from cmap
    public IReadOnlyList<KerningPair> KerningPairs { get; init; } = Array.Empty<KerningPair>();
    public Os2Metrics? Os2 { get; init; }                // optional OS/2 table data
    public HeadTable? Head { get; init; }                // head table data, null if missing
    public HheaTable? Hhea { get; init; }                // hhea table data, null if missing
    public NameInfo? Names { get; init; }                // name table strings, null if missing
    public IReadOnlyList<VariationAxis>? VariationAxes { get; init; }   // variable font axes, null for non-variable
    public IReadOnlyList<NamedInstance>? NamedInstances { get; init; }  // preset variable font styles, null for non-variable
    public bool HasColorGlyphs { get; init; }            // true if font has color glyphs
}
```

### KerningPair

```csharp
public readonly record struct KerningPair(int LeftCodepoint, int RightCodepoint, int XAdvanceAdjustment);
```

> **Note:** `XAdvanceAdjustment` is in font units. Callers scale to pixels: `value * targetSize / unitsPerEm`.

### Table Model Types

#### HeadTable

```csharp
public sealed record HeadTable(
    int UnitsPerEm,
    int XMin,
    int YMin,
    int XMax,
    int YMax,
    int IndexToLocFormat,       // 0 = short, 1 = long
    long Created,               // timestamp
    long Modified,              // timestamp
    ushort MacStyle = 0,        // bit 0 = bold, bit 1 = italic
    ushort LowestRecPPEM = 0)   // smallest readable size in pixels per em
{
    public DateTime CreatedUtc => Epoch.AddSeconds(Created);
    public DateTime ModifiedUtc => Epoch.AddSeconds(Modified);
}
```

#### HheaTable

```csharp
public sealed record HheaTable(
    int Ascender,
    int Descender,
    int LineGap,
    int AdvanceWidthMax,
    int NumberOfHMetrics,
    short MinLeftSideBearing = 0,
    short MinRightSideBearing = 0,
    short XMaxExtent = 0);
```

#### Os2Metrics

```csharp
public sealed record Os2Metrics(
    int WeightClass,
    int WidthClass,
    int TypoAscender,
    int TypoDescender,
    int TypoLineGap,
    int WinAscent,
    int WinDescent,
    int XHeight,
    int CapHeight,
    byte[] Panose,              // 10 bytes
    int FirstCharIndex,
    int LastCharIndex,
    short XAvgCharWidth = 0,
    short SubscriptXSize = 0,
    short SubscriptYSize = 0,
    short SuperscriptXSize = 0,
    short SuperscriptYSize = 0,
    short StrikeoutSize = 0,
    short StrikeoutPosition = 0);
```

#### NameInfo

```csharp
public sealed record NameInfo(
    string? FontFamily,
    string? FontSubfamily,
    string? FullName,
    string? PostScriptName,
    string? Copyright,
    string? Trademark,
    string? UniqueId = null,
    string? Version = null,
    string? Manufacturer = null,
    string? Designer = null,
    string? Description = null,
    string? License = null,
    string? LicenseUrl = null);
```

## Rasterization Layer Types

### RasterizedGlyph

`sealed class` -- instances are immutable after creation.

```csharp
public sealed class RasterizedGlyph
{
    public required int Codepoint { get; init; }
    public required int GlyphIndex { get; init; }
    public required byte[] BitmapData { get; init; }     // row-major, top-to-bottom, 8-bit grayscale by default
    public required int Width { get; init; }             // bitmap width in pixels
    public required int Height { get; init; }            // bitmap height in pixels
    public required int Pitch { get; init; }             // bytes per row, may differ from Width for alignment
    public required GlyphMetrics Metrics { get; init; }
    public required PixelFormat Format { get; init; }
}
```

> **Note:** Post-processors create new `RasterizedGlyph` instances with modified data. Instances are immutable after creation. The bitmap data is a managed copy -- it does NOT reference native FreeType memory and can outlive the rasterizer.

### GlyphMetrics

```csharp
public readonly record struct GlyphMetrics(
    int BearingX,   // left side bearing in pixels -- maps to BMFont xoffset
    int BearingY,   // top bearing in pixels -- used to compute BMFont yoffset
    int Advance,    // horizontal advance in pixels -- maps to BMFont xadvance
    int Width,      // glyph bbox width in pixels
    int Height      // glyph bbox height in pixels
);
```

> **Note:** All values are in pixels, already converted from FreeType's 26.6 fixed-point format (divide by 64, round).

### RasterOptions

```csharp
public sealed record RasterOptions
{
    public required int Size { get; init; }              // font size in points
    public int Dpi { get; init; } = 72;
    public AntiAliasMode AntiAlias { get; init; } = AntiAliasMode.Grayscale;
    public bool Bold { get; init; }                      // synthetic bold
    public bool Italic { get; init; }                    // synthetic italic/oblique
    public bool Sdf { get; init; }                       // SDF rendering mode
    public bool ColorFont { get; init; }                 // render color glyphs
    public int ColorPaletteIndex { get; init; }          // CPAL palette index for color fonts
    public Dictionary<string, float>? VariationAxes { get; init; } // variable font axis overrides
    public bool EnableHinting { get; init; } = true;     // font hinting for sharper rendering
}
```

> **Note:** Extracted from `FontGeneratorOptions` for the rasterizer's use. The pipeline creates this from the user-facing options.

### PixelFormat

```csharp
public enum PixelFormat
{
    Grayscale8 = 0,  // 1 byte per pixel, used for normal rendering
    Rgba32 = 1       // 4 bytes per pixel, used for color fonts or composed atlas pages
}
```

## Texture Packing Layer Types

### GlyphRect

```csharp
public readonly record struct GlyphRect(
    int Id,      // matches codepoint or glyph index for later lookup
    int Width,   // effective width = bitmap width + padding*2 + spacing
    int Height   // effective height = bitmap height + padding*2 + spacing
);
```

> **Note:** The caller pre-computes effective sizes before calling `IAtlasPacker.Pack()`. The packer does not know about padding/spacing.

### GlyphPlacement

```csharp
public readonly record struct GlyphPlacement(
    int Id,
    int PageIndex,
    int X,   // top-left position on the atlas page
    int Y    // top-left position on the atlas page
);
```

### PackResult

```csharp
public sealed class PackResult
{
    public required IReadOnlyList<GlyphPlacement> Placements { get; init; }
    public required int PageCount { get; init; }
    public required int PageWidth { get; init; }         // actual page dimensions used
    public required int PageHeight { get; init; }        // actual page dimensions used
}
```

## Atlas Layer Types

### AtlasPage

```csharp
public sealed class AtlasPage
{
    public required int PageIndex { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] PixelData { get; init; }      // row-major, top-to-bottom
    public required PixelFormat Format { get; init; }

    public byte[] ToPng() { /* delegates to configured IAtlasEncoder */ }
    public byte[] ToTga() { /* encodes as TGA */ }
    public byte[] ToDds() { /* encodes as DDS */ }
}
```

> **Note:** `ToPng()` is a convenience method. The raw `PixelData` is always available for custom encoding.

## Output Layer Types

### OutputFormat

```csharp
public enum OutputFormat
{
    Text = 0,    // BMFont text format, the default
    Xml = 1,     // BMFont XML format
    Binary = 2   // BMFont binary format
}
```

### BmFontResult

```csharp
public sealed class BmFontResult
{
    public BmFontModel Model { get; }                               // the in-memory BMFont descriptor
    public IReadOnlyList<AtlasPage> Pages { get; }                  // the atlas page images
    public IReadOnlyList<int> FailedCodepoints { get; }             // codepoints that could not be rasterized
    public PipelineMetrics? Metrics { get; }                        // timing metrics (when CollectMetrics is enabled)

    public override string ToString() { /* text format via TextFormatter */ }
    public string ToXml() { /* XML format via XmlFormatter */ }
    public byte[] ToBinary() { /* binary format via BmFontBinaryFormatter */ }
    public void ToFile(string outputPath, OutputFormat format = OutputFormat.Text) { /* writes .fnt + .png files */ }
    public string ToBmfc() { /* .bmfc config content, requires source options */ }

    public string FntText { get; }                                  // text format (property)
    public string FntXml { get; }                                   // XML format (property)
    public byte[] FntBinary { get; }                                // binary format (property)
    public byte[][] GetPngData() { /* all pages as PNG */ }
    public byte[] GetPngData(int pageIndex) { /* single page as PNG */ }
    public byte[][] GetTgaData() { /* all pages as TGA */ }
    public byte[] GetTgaData(int pageIndex) { /* single page as TGA */ }
    public byte[][] GetDdsData() { /* all pages as DDS */ }
    public byte[] GetDdsData(int pageIndex) { /* single page as DDS */ }
}
```

> **Note:** The model types `BmFontModel`, `InfoBlock`, `CommonBlock`, `PageEntry`, `CharEntry`, `KerningEntry` are defined in [plan-output-formats.md](plan-output-formats.md).

## Configuration Types

### Padding

```csharp
public readonly record struct Padding(int Up, int Right, int Down, int Left)
{
    /// <summary>Creates uniform padding on all sides.</summary>
    public Padding(int all) : this(all, all, all, all) { }

    public static Padding Zero => new(0, 0, 0, 0);
}
```

### Spacing

```csharp
public readonly record struct Spacing(int Horizontal, int Vertical)
{
    /// <summary>Creates uniform spacing in both directions.</summary>
    public Spacing(int both) : this(both, both) { }

    public static Spacing Zero => new(0, 0);
}
```

### SystemFontInfo

```csharp
public sealed class SystemFontInfo
{
    public required string FamilyName { get; init; }
    public required string StyleName { get; init; }
    public required string FilePath { get; init; }
    public int FaceIndex { get; init; }                   // for .ttc collections
}
```

## Interfaces

### IFontReader

```csharp
public interface IFontReader
{
    FontInfo ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0);
}
```

### IRasterizer

```csharp
public interface IRasterizer : IDisposable
{
    void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0);
    RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options);
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);
    GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options) => null;
}
```

> **Note:** `RasterizeGlyph` returns `null` for codepoints with no glyph. `RasterizeAll` skips nulls.

### IGlyphPostProcessor

```csharp
public interface IGlyphPostProcessor
{
    RasterizedGlyph Process(RasterizedGlyph glyph);
}
```

### IAtlasPacker

```csharp
public interface IAtlasPacker
{
    PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight);
}
```

### IAtlasEncoder

```csharp
public interface IAtlasEncoder
{
    byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format);
    string FileExtension { get; }
}
```

### IBmFontFormatter

```csharp
public interface IBmFontFormatter
{
    string Format { get; }
}

public interface IBmFontTextFormatter : IBmFontFormatter
{
    string FormatText(BmFontModel model);
}

public interface IBmFontBinaryFormatter : IBmFontFormatter
{
    byte[] FormatBinary(BmFontModel model);
}
```

### ISystemFontProvider

```csharp
public sealed record FontLoadResult(byte[] Data, int FaceIndex);

public interface ISystemFontProvider
{
    IReadOnlyList<SystemFontInfo> GetInstalledFonts();
    FontLoadResult? LoadFont(string familyName, string? styleName = null);
}
```

> Each interface has a default implementation in the library. See the corresponding plan document for implementation details:
> - `IFontReader` → `TtfFontReader` — see [plan-font-parsing.md](plan-font-parsing.md)
> - `IRasterizer` → `FreeTypeRasterizer` — see [plan-rasterization.md](plan-rasterization.md)
> - `IGlyphPostProcessor` → various — see [plan-rasterization.md](plan-rasterization.md)
> - `IAtlasPacker` → `MaxRectsPacker`, `SkylinePacker` — see [plan-texture-packing.md](plan-texture-packing.md)
> - `IAtlasEncoder` → `StbPngEncoder` — see [plan-texture-packing.md](plan-texture-packing.md)
> - `IBmFontTextFormatter` → `TextFormatter`, `XmlFormatter` — see [plan-output-formats.md](plan-output-formats.md)
> - `IBmFontBinaryFormatter` → `BmFontBinaryFormatter` — see [plan-output-formats.md](plan-output-formats.md)

## Error Handling Strategy

Project-wide approach to errors:

- **Corrupt/invalid font data**: Throw `FontParsingException` (custom) with details about what was invalid.
- **Missing glyph**: `IRasterizer.RasterizeGlyph` returns `null` for codepoints with no glyph. The pipeline skips them silently and does not include them in the output.
- **Glyph doesn't fit page**: If a single glyph exceeds `MaxTextureSize`, throw `AtlasPackingException`.
- **FreeType errors**: Wrap in `RasterizationException` with the FreeType error code.
- **Invalid options**: Throw `ArgumentException` on `BmFont.Generate()` entry for `Size <= 0`, `MaxTextureSize <= 0`, etc.

All custom exceptions inherit from `BmFontException`, not `Exception` directly.

### Custom Exception Hierarchy

```
BmFontException (base)
├── FontParsingException
├── RasterizationException
└── AtlasPackingException
```

```csharp
public class BmFontException : Exception
{
    public BmFontException(string message) : base(message) { }
    public BmFontException(string message, Exception inner) : base(message, inner) { }
}

public class FontParsingException : BmFontException { /* ... */ }
public class RasterizationException : BmFontException { /* ... */ }
public class AtlasPackingException : BmFontException { /* ... */ }
```

## Cross-Reference Index

| Plan Document | Types Needed |
|---|---|
| [plan-font-parsing.md](plan-font-parsing.md) | FontInfo, KerningPair, HeadTable, HheaTable, Os2Metrics, NameInfo |
| [plan-rasterization.md](plan-rasterization.md) | RasterizedGlyph, GlyphMetrics, RasterOptions, PixelFormat |
| [plan-texture-packing.md](plan-texture-packing.md) | GlyphRect, GlyphPlacement, PackResult, AtlasPage, PixelFormat |
| [plan-output-formats.md](plan-output-formats.md) | OutputFormat, BmFontResult (and its own model types defined there) |
| [plan-api-design.md](plan-api-design.md) | All types (it defines the public API surface) |
| [plan-testing.md](plan-testing.md) | All types (tests need to construct and verify them) |
