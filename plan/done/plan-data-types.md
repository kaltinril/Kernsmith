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
    public required IReadOnlyList<KerningPair> KerningPairs { get; init; }      // merged kern + GPOS
    public Os2Metrics? Os2 { get; init; }                // optional OS/2 table data
    public required HeadTable Head { get; init; }        // head table data
    public required HheaTable Hhea { get; init; }        // hhea table data
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
public sealed record HeadTable
{
    public required int UnitsPerEm { get; init; }
    public required int XMin { get; init; }
    public required int YMin { get; init; }
    public required int XMax { get; init; }
    public required int YMax { get; init; }
    public required int IndexToLocFormat { get; init; }  // 0 = short, 1 = long
    public required long Created { get; init; }          // timestamp
    public required long Modified { get; init; }         // timestamp
}
```

#### HheaTable

```csharp
public sealed record HheaTable
{
    public required int Ascender { get; init; }
    public required int Descender { get; init; }
    public required int LineGap { get; init; }
    public required int AdvanceWidthMax { get; init; }
    public required int NumberOfHMetrics { get; init; }
}
```

#### Os2Metrics

```csharp
public sealed record Os2Metrics
{
    public required int WeightClass { get; init; }
    public required int WidthClass { get; init; }
    public required int TypoAscender { get; init; }
    public required int TypoDescender { get; init; }
    public required int TypoLineGap { get; init; }
    public required int WinAscent { get; init; }
    public required int WinDescent { get; init; }
    public required int XHeight { get; init; }
    public required int CapHeight { get; init; }
    public required byte[] Panose { get; init; }         // 10 bytes
    public required int FirstCharIndex { get; init; }
    public required int LastCharIndex { get; init; }
}
```

#### NameInfo

```csharp
public sealed record NameInfo
{
    public string? FontFamily { get; init; }
    public string? FontSubfamily { get; init; }
    public string? FullName { get; init; }
    public string? PostScriptName { get; init; }
    public string? Copyright { get; init; }
    public string? Trademark { get; init; }
}
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
    public AntiAliasMode AntiAlias { get; init; } = AntiAliasMode.Normal;
    public bool Bold { get; init; }                      // synthetic bold
    public bool Italic { get; init; }                    // synthetic italic/oblique
    public bool Sdf { get; init; }                       // SDF rendering mode
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

    /// <summary>
    /// Convenience method. Delegates to the configured IAtlasEncoder.
    /// </summary>
    public byte[] ToPng() { /* delegates to IAtlasEncoder */ }
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
    public required BmFontModel Model { get; init; }                // the in-memory BMFont descriptor
    public required IReadOnlyList<AtlasPage> Pages { get; init; }   // the atlas page images

    public string ToString() { /* text format via TextFormatter */ }
    public string ToXml() { /* XML format via XmlFormatter */ }
    public byte[] ToBinary() { /* binary format via BmFontBinaryFormatter */ }
    public void ToFile(string outputPath, OutputFormat format = OutputFormat.Text) { /* writes .fnt + .png files */ }
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
    public required int FaceIndex { get; init; }         // for .ttc collections
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
    void LoadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0);
    RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options);
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);
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
    PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int pageWidth, int pageHeight);
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
public interface ISystemFontProvider
{
    IReadOnlyList<SystemFontInfo> GetInstalledFonts();
    byte[] LoadFont(string familyName, string? styleName = null);
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

### Custom Exception Hierarchy

```
KernSmithException (base)
├── FontParsingException
├── RasterizationException
└── AtlasPackingException
```

```csharp
public class KernSmithException : Exception
{
    public KernSmithException(string message) : base(message) { }
    public KernSmithException(string message, Exception innerException) : base(message, innerException) { }
}

public class FontParsingException : KernSmithException { /* ... */ }
public class RasterizationException : KernSmithException { /* ... */ }
public class AtlasPackingException : KernSmithException { /* ... */ }
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
