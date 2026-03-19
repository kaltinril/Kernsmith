# bmfontier -- Project Structure

> Part of the [Master Plan](master-plan.md).
> Related: [API Design](plan-api-design.md)

---

## Solution Layout

```
bmfontier/
+-- src/
|   +-- Bmfontier/                        # Main NuGet package
|       +-- Bmfontier.csproj
|       +-- BmFont.cs                      # Main entry point / builder API
|       +-- Font/                          # Font loading and parsing
|       |   +-- FontLoader.cs              # FreeTypeSharp font loading
|       |   +-- TtfParser.cs               # Our TTF table parser (entry point)
|       |   +-- Tables/                    # Individual table parsers
|       |   |   +-- CmapTable.cs
|       |   |   +-- GposTable.cs
|       |   |   +-- KernTable.cs
|       |   |   +-- NameTable.cs
|       |   |   +-- Os2Table.cs
|       |   |   +-- HeadTable.cs
|       |   |   +-- HheaTable.cs
|       |   |   +-- HmtxTable.cs
|       |   |   +-- MaxpTable.cs
|       |   +-- Models/                    # Font data models
|       |       +-- FontInfo.cs            # Merged font metadata
|       |       +-- GlyphMetrics.cs        # Per-glyph measurements
|       |       +-- KerningPair.cs         # Kerning pair data
|       +-- Rasterizer/                    # Glyph rasterization
|       |   +-- GlyphRasterizer.cs         # FreeTypeSharp rasterization
|       |   +-- RasterOptions.cs           # Size, DPI, AA mode, SDF toggle
|       +-- Packing/                       # Texture atlas packing
|       |   +-- IAtlasPacker.cs            # Interface for swappable algorithms
|       |   +-- MaxRectsPacker.cs          # Primary algorithm (BSSF + batch)
|       |   +-- SkylinePacker.cs           # Fast alternative (Bottom-Left)
|       |   +-- PackResult.cs              # Packing output (glyph placements)
|       +-- Atlas/                         # Atlas image generation
|       |   +-- AtlasBuilder.cs            # Composes glyph bitmaps into atlas
|       |   +-- AtlasPage.cs              # Single texture page (bitmap data)
|       +-- Output/                        # BMFont format output
|       |   +-- BmFontModel.cs             # In-memory BMFont data model
|       |   +-- TextFormatter.cs           # .fnt text format serializer
|       |   +-- XmlFormatter.cs            # .fnt XML format serializer
|       |   +-- BinaryFormatter.cs         # .fnt binary format serializer
|       |   +-- FileWriter.cs              # Write .fnt + .png to disk
|       +-- Config/                        # Configuration
|           +-- FontGeneratorOptions.cs    # All configurable options
|           +-- CharacterSet.cs            # Predefined and custom char sets
+-- tests/
|   +-- Bmfontier.Tests/
|       +-- Bmfontier.Tests.csproj
|       +-- Font/                          # Parser tests (each table)
|       +-- Packing/                       # Packing algorithm tests
|       +-- Output/                        # Format output tests
|       +-- Integration/                   # End-to-end tests
+-- samples/
|   +-- Bmfontier.Cli/                    # Reference CLI tool
|       +-- Program.cs
+-- reference/                             # Research & design docs (not shipped)
+-- bmfontier.sln
+-- README.md
+-- LICENSE
+-- CLAUDE.md
```

---

## Namespace Mapping

| Namespace | Folder | Contains |
|-----------|--------|----------|
| `Bmfontier` | root | `BmFont` (entry point), `FontGeneratorOptions`, `CharacterSet` |
| `Bmfontier.Font` | Font/ | `FontLoader`, `TtfParser` |
| `Bmfontier.Font.Tables` | Font/Tables/ | Individual table parsers |
| `Bmfontier.Font.Models` | Font/Models/ | `FontInfo`, `GlyphMetrics`, `KerningPair` |
| `Bmfontier.Rasterizer` | Rasterizer/ | `GlyphRasterizer`, `RasterOptions` |
| `Bmfontier.Packing` | Packing/ | `IAtlasPacker`, `MaxRectsPacker`, `SkylinePacker`, `PackResult` |
| `Bmfontier.Atlas` | Atlas/ | `AtlasBuilder`, `AtlasPage` |
| `Bmfontier.Output` | Output/ | `BmFontModel`, `TextFormatter`, `XmlFormatter`, `BinaryFormatter`, `FileWriter` |

---

## Dependencies

| Package | Purpose | License | Required? | Size |
|---------|---------|---------|-----------|------|
| **FreeTypeSharp** 3.1.0 | Glyph rasterization, glyph metrics, kern table kerning | MIT | Yes | ~12 MB (includes native FreeType for all platforms) |
| **PNG encoder** (TBD) | Encode atlas bitmaps as PNG | Must be open source | Yes | See analysis below |

---

## PNG Encoding -- Decision Needed

We need to encode raw bitmap data as PNG for atlas output. Options:

| Option | License | Type | Pros | Cons |
|--------|---------|------|------|------|
| **StbImageWriteSharp** | Public domain | Managed port of stb_image_write | Zero-dep, tiny, battle-tested C origin | Write-only, no read (fine for us) |
| **BigGustave** | MIT | Pure C# | MIT, pure managed, read+write | Less popular, fewer downloads |
| **Our own minimal PNG writer** | N/A | Our code | Zero dependencies, full control | Dev effort, but PNG for grayscale/RGBA is not complex |
| **System.Drawing** | .NET | System library | Built-in | Windows only -- violates cross-platform requirement |
| **SkiaSharp** | MIT | Native wrapper | Full imaging capability | ~130 MB native deps -- massive overkill |

**Recommended**: Start with **StbImageWriteSharp** (public domain, ~200 KB, proven). Fall back to our own minimal PNG writer if the dependency causes issues. PNG encoding for 8-bit grayscale or 32-bit RGBA is well-specified and implementable in ~300-400 lines.

The PNG encoder is abstracted behind `IAtlasEncoder` (see [Texture Packing](plan-texture-packing.md)) so the choice can be changed later without affecting the rest of the codebase.

---

## Target Framework

FreeTypeSharp targets .NET Standard 2.0 + .NET 9.0. We should target **NET 8+** (current LTS) with possible .NET Standard 2.1 for broader reach. Key considerations:

- .NET 8 is the current Long-Term Support release.
- `ReadOnlySpan<byte>` and `BinaryPrimitives` (used extensively in our parser) require .NET Standard 2.1 minimum.
- .NET Standard 2.1 would enable Unity and older framework compatibility.

---

## License

Both MIT and Apache 2.0 are permissive open-source licenses. **MIT** is simpler and matches FreeTypeSharp's license. SixLabors is explicitly excluded due to its split license model.

FreeType native library uses the FreeType License (BSD-like), which is compatible with both MIT and Apache 2.0.
