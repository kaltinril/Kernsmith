# Core Library

The `KernSmith` NuGet package is the core library that powers bitmap font generation. It provides a fully in-memory pipeline from font file to BMFont output.

## Pipeline Flow

1. **Font reading** -- parse TTF/OTF/WOFF tables (cmap, head, hhea, OS/2, GPOS) for the requested codepoints
2. **Rasterization** -- render glyphs via a [pluggable rasterizer backend](../rasterizers/index.md) (FreeType by default, with GDI and DirectWrite alternatives) with optional effects (outline, gradient, shadow)
3. **Atlas packing** -- arrange glyphs into texture pages using MaxRects or Skyline algorithms
4. **Output formatting** -- produce BMFont `.fnt` descriptors (text, XML, or binary) and encoded atlas images

## Namespaces

| Namespace | Purpose |
|-----------|---------|
| `KernSmith` | Entry point (<xref:KernSmith.BmFont>), configuration types (<xref:KernSmith.FontGeneratorOptions>, <xref:KernSmith.CharacterSet>, <xref:KernSmith.Padding>, <xref:KernSmith.Spacing>, <xref:KernSmith.ChannelConfig>, <xref:KernSmith.BmfcConfig>, <xref:KernSmith.AtlasSizeConstraints>, <xref:KernSmith.AntiAliasMode>, <xref:KernSmith.OutputFormat>, <xref:KernSmith.TextureFormat>), exceptions, enums |
| `KernSmith.Font` | Font reading and TTF table parsing |
| `KernSmith.Font.Models` | Data models: <xref:KernSmith.Font.Models.FontInfo>, <xref:KernSmith.Font.Models.KerningPair>, <xref:KernSmith.Font.Models.GlyphMetrics> |
| `KernSmith.Font.Tables` | Parsed table structures: <xref:KernSmith.Font.Tables.HeadTable>, <xref:KernSmith.Font.Tables.HheaTable>, <xref:KernSmith.Font.Tables.Os2Metrics>, <xref:KernSmith.Font.Tables.NameInfo> |
| `KernSmith.Rasterizer` | <xref:KernSmith.Rasterizer.IRasterizer>, glyph effects (<xref:KernSmith.Rasterizer.IGlyphEffect>), <xref:KernSmith.Rasterizer.GlyphCompositor> |
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
- <xref:KernSmith.BmFont>.RegisterFont() -- register raw font data for use with `GenerateFromSystem()` on platforms without system font access
- <xref:KernSmith.BmFont>.UnregisterFont() -- remove a previously registered font
- <xref:KernSmith.BmFont>.ClearRegisteredFonts() -- remove all registered fonts

### BmFontResult

The output of font generation. Provides access to:

- `.FntText`, `.FntXml`, `.FntBinary` -- formatted `.fnt` content
- `.Pages` -- atlas page pixel data and dimensions
- `.GetPngData()`, `.GetTgaData()`, `.GetDdsData()` -- encoded atlas images
- `.ToFile()` -- write all output files to disk
- `.Model` -- the underlying <xref:KernSmith.Output.Model.BmFontModel> data

### FontGeneratorOptions

Configuration for the generation pipeline: font size, character set, effects (outline, gradient, shadow), atlas settings, SDF, super sampling, variable font axes, and more.

#### Bold / Italic Properties

| Property | Description |
|----------|-------------|
| `Bold` | Request bold -- uses native bold face when available (system fonts), falls back to synthetic |
| `Italic` | Request italic -- uses native italic face when available (system fonts), falls back to synthetic |
| `ForceSyntheticBold` | Force synthetic bold, skip native bold face lookup |
| `ForceSyntheticItalic` | Force synthetic italic, skip native italic face lookup |

When loading from a file path, bold/italic is always synthetic -- `Bold` and `ForceSyntheticBold` produce identical results. Use `GenerateFromSystem()` or `WithSystemFont()` for native face resolution. GDI backend limitation: cannot apply synthetic bold when a native bold face exists -- use FreeType or DirectWrite.

### CharacterSet

Defines which Unicode codepoints to include. Provides presets (`Ascii`, `ExtendedAscii`, `Latin`) and factory methods (`FromChars`, `FromRanges`, `Union`).

## Platform Notes

### Blazor WASM

The core library works in Blazor WebAssembly when paired with the StbTrueType rasterizer backend. Key constraints:

- Use `KernSmith.Rasterizers.StbTrueType` — FreeType requires native binaries unavailable in WASM
- Use in-memory APIs (`FntText`, `GetPngData()`) instead of `ToFile()`
- System font loading returns empty results — use `BmFont.RegisterFont()` to provide font data
- Enable `<RunAOTCompilation>true</RunAOTCompilation>` for production performance

See the [Blazor WASM sample](https://github.com/kaltinril/KernSmith/tree/main/samples/KernSmith.Samples.BlazorWasm) for a complete working example.
