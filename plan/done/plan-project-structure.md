# KernSmith -- Project Structure

> Part of the [Master Plan](master-plan.md).
> Related: [API Design](plan-api-design.md)
>
> All data types referenced in this document are defined in [plan-data-types.md](plan-data-types.md). Interface definitions are in [plan-api-design.md](plan-api-design.md).

---

## Solution Layout

```
KernSmith/
+-- src/
|   +-- KernSmith/                        # Main NuGet package
|       +-- KernSmith.csproj
|       +-- BmFont.cs                      # Main entry point / builder API
|       +-- Font/                          # Font loading and parsing
|       |   +-- IFontReader.cs             # Interface for font reading
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
|       |       +-- HeadTable.cs           # Head table data model
|       |       +-- HheaTable.cs           # Hhea table data model
|       |       +-- Os2Metrics.cs          # OS/2 metrics data model
|       |       +-- NameInfo.cs            # Name table data model
|       +-- Rasterizer/                    # Glyph rasterization
|       |   +-- IRasterizer.cs             # Interface for glyph rasterization
|       |   +-- IGlyphPostProcessor.cs     # Interface for post-processing
|       |   +-- GlyphRasterizer.cs         # FreeTypeSharp rasterization
|       |   +-- RasterizedGlyph.cs         # Rasterized glyph output data
|       |   +-- RasterOptions.cs           # Size, DPI, AA mode, SDF toggle
|       +-- Atlas/                         # Texture atlas packing and image generation
|       |   +-- IAtlasPacker.cs            # Interface for swappable packing algorithms
|       |   +-- IAtlasEncoder.cs           # Interface for atlas encoding
|       |   +-- AtlasBuilder.cs            # Composes glyph bitmaps into atlas
|       |   +-- AtlasPage.cs              # Single texture page (bitmap data)
|       |   +-- GlyphRect.cs              # Glyph rectangle in atlas
|       |   +-- GlyphPlacement.cs          # Glyph placement data
|       |   +-- PackResult.cs             # Atlas packing result
|       +-- Output/                        # BMFont format output
|       |   +-- IBmFontFormatter.cs        # Interface for BMFont formatting
|       |   +-- BmFontModel.cs             # In-memory BMFont data model
|       |   +-- TextFormatter.cs           # .fnt text format serializer
|       |   +-- XmlFormatter.cs            # .fnt XML format serializer
|       |   +-- BmFontBinaryFormatter.cs   # .fnt binary format serializer
|       |   +-- FileWriter.cs              # Write .fnt + .png to disk
|       +-- Config/                        # Configuration
|       |   +-- FontGeneratorOptions.cs    # All configurable options
|       |   +-- CharacterSet.cs            # Predefined and custom char sets
|       |   +-- PixelFormat.cs             # Pixel format enumeration
|       +-- Exceptions/                    # Custom exception types
|           +-- BmFontException.cs         # Base exception
|           +-- FontParsingException.cs    # Font parsing errors
|           +-- RasterizationException.cs  # Rasterization errors
|           +-- AtlasPackingException.cs   # Atlas packing errors
+-- tests/
|   +-- KernSmith.Tests/
|       +-- KernSmith.Tests.csproj
|       +-- Font/                          # Parser tests (each table)
|       +-- Packing/                       # Packing algorithm tests
|       +-- Output/                        # Format output tests
|       +-- Integration/                   # End-to-end tests
+-- samples/
|   +-- KernSmith.Cli/                    # Reference CLI tool
|       +-- Program.cs
+-- plan/                                  # Project plans & design docs (not shipped)
+-- reference/                             # Research & reference material (not shipped)
+-- KernSmith.sln
+-- README.md
+-- LICENSE
+-- CLAUDE.md
```

---

## Namespace Mapping

| Namespace | Folder | Contains |
|-----------|--------|----------|
| `KernSmith` | root | `BmFont` (entry point) |
| `KernSmith` | Config/ | `FontGeneratorOptions`, `CharacterSet`, `PixelFormat` (see note below) |
| `KernSmith.Font` | Font/ | `IFontReader`, `FontLoader`, `TtfParser` |
| `KernSmith.Font.Tables` | Font/Tables/ | Individual table parsers |
| `KernSmith.Font.Models` | Font/Models/ | `FontInfo`, `GlyphMetrics`, `KerningPair`, `HeadTable`, `HheaTable`, `Os2Metrics`, `NameInfo` |
| `KernSmith.Rasterizer` | Rasterizer/ | `IRasterizer`, `IGlyphPostProcessor`, `GlyphRasterizer`, `RasterizedGlyph`, `RasterOptions` |
| `KernSmith.Atlas` | Atlas/ | `IAtlasPacker`, `MaxRectsPacker`, `SkylinePacker`, `PackResult`, `IAtlasEncoder`, `AtlasBuilder`, `AtlasPage`, `GlyphRect`, `GlyphPlacement` |
| `KernSmith.Output` | Output/ | `IBmFontFormatter`, `BmFontModel`, `TextFormatter`, `XmlFormatter`, `BmFontBinaryFormatter`, `FileWriter` |
| `KernSmith` | Exceptions/ | `BmFontException` (base), `FontParsingException`, `RasterizationException`, `AtlasPackingException` — all use root `KernSmith` namespace |

> **Config/ namespace note:** Files in `Config/` use the root `KernSmith` namespace (not `KernSmith.Config`) because these are core configuration types that users reference frequently. Use `<RootNamespace>` in the folder's files or explicit namespace declarations.

---

## Dependencies

| Package | Purpose | License | Required? | Size |
|---------|---------|---------|-----------|------|
| **FreeTypeSharp** 3.1.0 | Glyph rasterization, glyph metrics, kern table kerning | MIT | Yes | ~12 MB (includes native FreeType for all platforms) |
| **StbImageWriteSharp** 1.16.7 | Encode atlas bitmaps as PNG | Public domain | Yes | ~200 KB managed |

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

**Decision: StbImageWriteSharp** (NuGet: `StbImageWriteSharp`, public domain). This is confirmed for Phase 1. Minimal dependency, no native code, good enough quality.

The PNG encoder is abstracted behind `IAtlasEncoder` (see [Texture Packing](plan-texture-packing.md)) so the choice can be changed later without affecting the rest of the codebase.

---

## Target Framework

**Target framework: `net8.0;net10.0`** (multi-target). The `Directory.Build.props` uses `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`. Originally net8.0 only; net10.0 added in Phase 11.

Key considerations:

- .NET 8 is the current Long-Term Support release.
- `ReadOnlySpan<byte>` and `BinaryPrimitives` (used extensively in our parser) require .NET Standard 2.1 minimum.
- .NET Standard 2.1 would enable Unity and older framework compatibility (Phase 2).
- FreeTypeSharp targets .NET Standard 2.0 + .NET 9.0, compatible with our target.

---

## Test Framework

**Test framework: xUnit** with `xunit`, `xunit.runner.visualstudio`, and `Microsoft.NET.Test.Sdk`. Use **Shouldly** for readable assertions (FluentAssertions replaced in Phase 79 due to paid licensing).

---

## Project File (.csproj)

Minimal `.csproj` skeleton for `src/KernSmith/KernSmith.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PackageId>KernSmith</PackageId>
    <Version>0.1.0</Version>
    <Authors>KernSmith contributors</Authors>
    <Description>Generate BMFont bitmap font atlases from TTF/OTF files</Description>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FreeTypeSharp" Version="3.1.0" />
    <PackageReference Include="StbImageWriteSharp" Version="1.16.7" />
  </ItemGroup>
</Project>
```

---

## License

Both MIT and Apache 2.0 are permissive open-source licenses. **MIT** is simpler and matches FreeTypeSharp's license. SixLabors is explicitly excluded due to its split license model.

FreeType native library uses the FreeType License (BSD-like), which is compatible with both MIT and Apache 2.0.
