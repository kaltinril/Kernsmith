# bmfontier -- Master Plan

> **Status**: Phases 1-4 complete. Remaining items tracked in [plan-phase-future.md](plan-phase-future.md).
> **Date**: 2026-03-19

---

## Project Summary

**bmfontier** is a cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF files. It combines FreeTypeSharp for glyph rasterization with our own TTF table parsers (for GPOS kerning, OS/2 metadata, etc.), packs glyphs into texture atlases, and outputs industry-standard BMFont `.fnt` + `.png` pairs. The entire pipeline operates in-memory by default with zero disk I/O required.

---

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Font rasterization** | FreeTypeSharp (MIT, wraps FreeType 2.13.2 via P/Invoke) | Industry-standard rasterizer, small native footprint (~12 MB), MIT license, supports SDF, hinting, AA modes. Use everything it exposes -- metrics, kerning (kern table), glyph bitmaps. |
| **TTF table parsing** | Our own pure C# parser | FreeTypeSharp cannot expose GPOS kerning pairs, OS/2 metadata, name table strings, or variable font axes. We parse the tables FreeTypeSharp cannot reach. No additional dependencies. |
| **Texture packing** | MaxRects (BestShortSideFit) primary, Skyline as fast mode | MaxRects achieves 93-97% packing efficiency. Skyline is 2-5x faster with 2-5% less efficiency. Our own implementation based on public domain reference code. |
| **API design** | In-memory model first, output methods on top | Core pipeline produces a format-agnostic model. `.ToString()`, `.ToXml()`, `.ToBinary()`, `.ToFile()` render it. Zero disk I/O by default. |
| **Licensing** | Open source, no paid/restrictive dependencies | FreeTypeSharp: MIT. FreeType native: FreeType License (BSD-like). Our code: MIT. SixLabors: explicitly excluded (split license). |
| **Cross-platform** | Anywhere .NET + FreeType native binaries run | Windows, macOS, Linux, Android, iOS, tvOS via FreeTypeSharp's bundled natives. No Linux ARM64 or WASM (FreeTypeSharp gap). |

---

## High-Level Pipeline

```
Input (font file bytes or system font path)
  |
  v
Font Loading Layer
  +-- FreeTypeSharp: load font, get face handle
  +-- Our TTF Parser: read tables (cmap, kern, GPOS, name, OS/2, head, hhea, hmtx)
  |
  v
Font Metrics & Kerning
  +-- FreeTypeSharp: per-glyph metrics (advance, bearing, bbox), kern table kerning
  +-- Our Parser: GPOS kerning pairs, font metadata, Unicode ranges, OS/2 metrics
  +-- Merged into unified FontInfo model
  |
  v
Glyph Rasterization
  +-- FreeTypeSharp: render each requested glyph to bitmap buffer
      (configurable: size, DPI, AA mode, SDF)
  |
  v
Texture Atlas Packing
  +-- Our MaxRects packer: arrange glyph bitmaps into atlas pages
      (configurable: max texture size, padding, spacing, power-of-2)
  |
  v
BMFont Model (in-memory)
  +-- InfoBlock, CommonBlock, Pages[], Characters[], KerningPairs[]
  |
  v
Output Layer
  +-- .ToString()       -> BMFont text format (default)
  +-- .ToXml()          -> BMFont XML format
  +-- .ToBinary()       -> BMFont binary format
  +-- .ToFile(path)     -> write .fnt + .png files to disk
  +-- .GetAtlasBytes()  -> raw PNG bytes for each page
```

### Data Flow Responsibilities

| Component | Source | Responsibility |
|-----------|--------|----------------|
| **FreeTypeSharp** | Native FreeType | Load font face, rasterize glyph bitmaps, provide scaled glyph metrics (advance, bearing, bbox), kern table kerning via `FT_Get_Kerning`, SDF rendering, synthetic bold/italic |
| **Our TTF Parser** | Pure C# | Read GPOS kerning pairs, OS/2 table metadata (weight class, typo metrics, x-height, cap height, panose), name table strings, cmap (Unicode coverage), head/hhea/hmtx tables |
| **Packer** | Our C# | MaxRects or Skyline bin packing, multi-page overflow, glyph sorting, padding/spacing handling |
| **Atlas Builder** | Our C# | Compose rasterized glyph bitmaps into atlas page images, PNG encoding |
| **BMFont Writer** | Our C# | Populate in-memory BMFont model, serialize to text/XML/binary formats |

---

## Sub-Documents

| Document | Description |
|----------|-------------|
| [Data Types](plan-data-types.md) | **All shared types, interfaces, and error handling.** Single source of truth — other docs reference this. |
| [Project Structure](plan-project-structure.md) | Solution layout, namespace mapping, dependencies, target framework, and licensing |
| [API Design](plan-api-design.md) | Public API surface, builder pattern, configuration types (`FontGeneratorOptions`, `CharacterSet`), and code examples |
| [Font Parsing](plan-font-parsing.md) | FreeTypeSharp usage, our TTF parser scope, GPOS parsing, implementation details |
| [Rasterization](plan-rasterization.md) | Glyph rasterization pipeline, FreeTypeRasterizer implementation, memory management |
| [Texture Packing](plan-texture-packing.md) | MaxRects/Skyline algorithms, multi-page strategy, atlas building |
| [Output Formats](plan-output-formats.md) | BMFont model classes, text/XML/binary serialization, file output |
| [Testing](plan-testing.md) | xUnit test strategy, concrete test fonts, golden data, validation criteria, CI |
| [Implementation Order](plan-implementation-order.md) | **Phased task breakdown with dependencies and parallel groups.** Start here for building. |
| [Future Phase](plan-phase-future.md) | **Deferred items from Phases 3-4 plus planned Phases 5-7.** |
| [CLI Tool](plan-cli.md) | Full-featured CLI plan — BMFont.exe replacement with config files, inspect/convert commands |
| [BMFont Parity](plan-bmfont-parity.md) | 15 missing features from BMFont.exe — per-channel config, super sampling, TGA, etc. |
| [Extended Metadata](plan-extended-metadata.md) | How bmfontier stores SDF spread, gradient, shadow, and other metadata in .fnt files |

---

## Reference Documents

| Document | Description |
|----------|-------------|
| [Vision](bmfontier-vision.md) | Original project vision and goals |
| [Library Comparison](../reference/font-library-comparison.md) | Evaluation of .NET font libraries |
| [FreeTypeSharp Evaluation](../reference/freetypesharp-evaluation.md) | Detailed FreeTypeSharp capabilities and gaps |
| [Texture Packing Reference](../reference/texture-packing-reference.md) | Rectangle packing algorithm research |
| [BMFont Format Reference](../reference/bmfont-format-reference.md) | BMFont file format specification |
| [TTF Font Reference](../reference/ttf-font-reference.md) | TrueType font format reference |
| [Other Font Formats Reference](../reference/other-font-formats-reference.md) | WOFF, OTF, and other format details |

---

## Phased Implementation

### Phase 1 -- MVP (COMPLETE)

Core pipeline end-to-end: load a TTF, parse required tables, rasterize glyphs, pack into atlas, output BMFont text format + PNG. 28 tasks covering FreeTypeSharp integration, all table parsers (including GPOS), MaxRects packer, atlas builder, PNG encoding, text format output, file output, entry point wiring, CharacterSet, and basic tests.

### Phase 2 -- Complete (COMPLETE)

Additional output formats (XML, binary), Skyline packer, system font enumeration, configurable padding/spacing/outline, SDF mode, font collection (.ttc) support, variable font support, and `GenerateFromSystem()` API.

### Phase 3 -- Ecosystem (COMPLETE)

WOFF/WOFF2 decompression, channel packing, reference CLI tool, performance benchmarks. Completed 6 of 10 tasks. Incomplete items (color font support, font subsetting, NuGet publishing CI, CLI tests) moved to [plan-phase-future.md](plan-phase-future.md).

### Phase 4 -- Deferred / Future (COMPLETE)

fvar table parser, BMFont reader (text/XML/binary), BmFont.Load() entry point, gradient post-processor. Completed 8 of 10 tasks. Incomplete items (variable font axis application, variable font tests) moved to [plan-phase-future.md](plan-phase-future.md). Variable font axis application is blocked on FreeTypeSharp lacking `FT_Set_Var_Design_Coordinates`.

### Future Phases

Incomplete items from Phases 3-4 plus planned future work are collected in **[plan-phase-future.md](plan-phase-future.md)**. This includes Phases 5 (Full CLI Tool), 6 (BMFont Parity Features), and 7 (Extended Metadata).

---

## Resolved Decisions

| # | Question | Decision | Details |
|---|----------|----------|---------|
| 1 | **PNG encoding library** | **StbImageWriteSharp** (public domain) | Confirmed. See [plan-project-structure.md](plan-project-structure.md). |
| 2 | **Target framework** | **net8.0** (current LTS) | .NET Standard 2.1 multi-targeting deferred to Phase 2. See [plan-project-structure.md](plan-project-structure.md). |
| 3 | **Project license** | **Proprietary** | See LICENSE file. |
| 4 | **NuGet package name** | **Bmfontier** | Package ID `Bmfontier`, main API class `BmFont`. |
| 5 | **FreeTypeSharp usage boundary** | Use everything it can do | Our parser only covers what FreeTypeSharp cannot (GPOS, OS/2, name, cmap). No duplication. |
| 6 | **Unsafe code policy** | `AllowUnsafeBlocks` in main project | Isolated to FreeType interop (`FreeTypeRasterizer.cs`, `TtfFontReader.cs`). Rest is safe C#. |
| 7 | **FreeType memory** | Manual lifecycle via `IDisposable` | Pin font data with `GCHandle`. Do NOT use `FreeTypeFaceFacade`. See [plan-rasterization.md](plan-rasterization.md). |
| 8 | **Test framework** | **xUnit** + FluentAssertions | See [plan-testing.md](plan-testing.md). |
| 9 | **Error handling** | Custom exception hierarchy | `FontParsingException`, `RasterizationException`, `AtlasPackingException`. See [plan-data-types.md](plan-data-types.md). |

---

## Glossary

| Term | Definition |
|------|-----------|
| **BMFont** | Bitmap font format created by AngelCode. The `.fnt` descriptor + `.png` atlas pair. |
| **cmap** | Character-to-glyph mapping table in TTF/OTF fonts. |
| **GPOS** | Glyph Positioning table in OpenType fonts. Contains kerning (and other positioning) data that supersedes the legacy kern table. |
| **kern** | Legacy kerning table in TrueType fonts. Simpler than GPOS but increasingly rare in modern fonts. |
| **MaxRects** | Rectangle bin packing algorithm by Jukka Jylanki (2010). Maintains a list of free rectangles, splits on placement, prunes contained rects. |
| **BSSF** | BestShortSideFit -- a MaxRects heuristic that minimizes the leftover space on the shorter side of the fit. |
| **Skyline** | Rectangle packing algorithm that maintains a 1D height map. Simpler and faster than MaxRects. |
| **SDF** | Signed Distance Field. A technique for resolution-independent font rendering. Each texel stores the distance to the nearest glyph edge. |
| **P/Invoke** | Platform Invocation Services. .NET mechanism for calling native C functions from managed code. |
| **26.6 fixed point** | FreeType's internal number format. The value is a 32-bit integer where the lower 6 bits are the fractional part. Divide by 64 to get the pixel value. |
