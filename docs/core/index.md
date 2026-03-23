# Core Library

The `KernSmith` NuGet package is the core library that powers bitmap font generation. It provides a fully in-memory pipeline from font file to BMFont output.

## Pipeline Flow

1. **Font reading** -- parse TTF/OTF/WOFF tables (cmap, head, hhea, OS/2, GPOS) for the requested codepoints
2. **Rasterization** -- render glyphs via FreeTypeSharp with optional effects (outline, gradient, shadow)
3. **Atlas packing** -- arrange glyphs into texture pages using MaxRects or Skyline algorithms
4. **Output formatting** -- produce BMFont `.fnt` descriptors (text, XML, or binary) and encoded atlas images

## Namespaces

| Namespace | Purpose |
|-----------|---------|
| `KernSmith` | Entry point (<xref:KernSmith.BmFont>), configuration types, exceptions, enums |
| `KernSmith.Font` | Font reading and TTF table parsing |
| `KernSmith.Font.Models` | Data models: <xref:KernSmith.Font.Models.FontInfo>, <xref:KernSmith.Font.Models.KerningPair>, <xref:KernSmith.Font.Models.GlyphMetrics> |
| `KernSmith.Font.Tables` | Parsed table structures: <xref:KernSmith.Font.Tables.HeadTable>, <xref:KernSmith.Font.Tables.HheaTable>, <xref:KernSmith.Font.Tables.Os2Metrics>, <xref:KernSmith.Font.Tables.NameInfo> |
| `KernSmith.Rasterizer` | <xref:KernSmith.Rasterizer.IRasterizer>, <xref:KernSmith.Rasterizer.FreeTypeRasterizer>, glyph effects (<xref:KernSmith.Rasterizer.IGlyphEffect>), <xref:KernSmith.Rasterizer.GlyphCompositor> |
| `KernSmith.Atlas` | <xref:KernSmith.Atlas.IAtlasPacker>, packing algorithms, texture encoders (PNG/TGA/DDS), <xref:KernSmith.Atlas.AtlasBuilder> |
| `KernSmith.Output` | BMFont formatters, <xref:KernSmith.Output.FileWriter>, <xref:KernSmith.Output.BmFontResult>, <xref:KernSmith.Output.BmFontReader> |
| `KernSmith.Output.Model` | BMFont data model: <xref:KernSmith.Output.Model.BmFontModel>, <xref:KernSmith.Output.Model.InfoBlock>, <xref:KernSmith.Output.Model.CommonBlock> |

## Key Classes

### BmFont

The main entry point. Provides static methods for font generation:

- <xref:KernSmith.BmFont>.Generate() -- generate from a font file path or byte array
- <xref:KernSmith.BmFont>.GenerateFromSystem() -- generate from a system-installed font
- <xref:KernSmith.BmFont>.FromConfig() -- generate from a `.bmfc` configuration file
- <xref:KernSmith.BmFont>.Builder() -- start a fluent builder chain
- <xref:KernSmith.BmFont>.Load() -- load an existing `.fnt` file with atlas pages
- <xref:KernSmith.BmFont>.GenerateBatch() -- parallel batch generation

### BmFontResult

The output of font generation. Provides access to:

- `.FntText`, `.FntXml`, `.FntBinary` -- formatted `.fnt` content
- `.Pages` -- atlas page pixel data and dimensions
- `.GetPngData()`, `.GetTgaData()`, `.GetDdsData()` -- encoded atlas images
- `.ToFile()` -- write all output files to disk
- `.Model` -- the underlying <xref:KernSmith.Output.Model.BmFontModel> data

### FontGeneratorOptions

Configuration for the generation pipeline: font size, character set, effects (outline, gradient, shadow), atlas settings, SDF, super sampling, variable font axes, and more.

### CharacterSet

Defines which Unicode codepoints to include. Provides presets (`Ascii`, `ExtendedAscii`, `Latin`) and factory methods (`FromChars`, `FromRanges`, `Union`).
