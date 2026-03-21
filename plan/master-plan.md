# KernSmith -- Master Plan

> **Status**: Phases 1-11, 13-17 complete. Phase 12 (Pre-Ship Polish) and Phase 30 (WASM) are active.
> **Date**: 2026-03-20

---

## Project Summary

**KernSmith** is a cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF files. It combines FreeTypeSharp for glyph rasterization with our own TTF table parsers (for GPOS kerning, OS/2 metadata, etc.), packs glyphs into texture atlases, and outputs industry-standard BMFont `.fnt` + `.png` pairs. The entire pipeline operates in-memory by default with zero disk I/O required.

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

## Active Plans

| # | Document | Description | Status |
|---|----------|-------------|--------|
| 12 | [Pre-Ship Polish](phase-12-pre-ship-polish.md) | Security hardening, test coverage gaps, NuGet packaging, API polish, FT_Stroker fix | Planning |
| 30 | [WASM Rasterization](phase-30-wasm-rasterization.md) | Live investigation of WASM-compatible rasterizers (prior research was preliminary) | Planning |

---

## Completed Phases (archived in `done/`)

| # | Document | Description |
|---|----------|-------------|
| 01 | [MVP](done/phase-01-mvp.md) | End-to-end pipeline: TTF -> rasterize -> pack -> BMFont text + PNG |
| 02 | [Complete](done/phase-02-complete.md) | XML/binary output, Skyline packer, SDF, system fonts, variable fonts |
| 03 | [Ecosystem](done/phase-03-ecosystem.md) | WOFF/WOFF2, channel packing, CLI, benchmarks, color fonts, subsetting |
| 04 | [Deferred/Future](done/phase-04-deferred-future.md) | fvar parser, BMFont reader, gradient post-processor, variable font axis API |
| 05 | [Full CLI Tool](done/phase-05-cli-tool.md) | 5 commands, .bmfc config, full option coverage |
| 06 | [BMFont Parity](done/phase-06-bmfont-parity.md) | TGA, super sampling, shadow, autofit, fallback glyph, 10+ parity features |
| 07 | [Extended Metadata](done/phase-07-extended-metadata.md) | SDF spread, gradient, shadow, outline metadata in .fnt (text/XML/binary) |
| 08 | [Optimal Atlas Sizing](done/phase-08-optimal-atlas-sizing.md) | Mathematical atlas size prediction replacing brute-force trial-and-error |
| 09 | [Outline Overhaul](done/phase-09-outline-overhaul.md) | EDT-based anti-aliased outlines with outline color support |
| 10 | [Layered Rendering](done/phase-10-layered-rendering.md) | IGlyphEffect compositing replacing order-dependent post-processor chain |
| 11 | [Solution Restructure](done/phase-11-solution-restructure.md) | Multi-project foundation, net10.0 migration, CLI promotion, app scaffolding |
| 13 | [Batch CLI](done/phase-13-batch-cli.md) | Batch command, .bmfc multi-file processing, collision detection |
| 14 | [Benchmarking & Profiling](done/phase-14-benchmarking-profiling.md) | 50+ benchmarks, PipelineMetrics, CLI --time/--profile, benchmark command |
| 15 | [Library Performance](done/phase-15-library-performance.md) | FontCache, GenerateBatch API, static SystemFontProvider -- 18 fonts in 196ms |
| 16 | [BMFont .bmfc Compatibility](done/phase-16-bmfc-compatibility.md) | Standard BMFont key=value format, drop legacy INI, same files work in both tools |
| 17 | [Rebrand to KernSmith](phase-17-rebrand-kernsmith.md) | Full project rename from KernSmith to KernSmith |

### Topical Plan Docs (archived in `done/`)

These detailed docs were used during implementation and remain as reference material.

| Document | Description |
|----------|-------------|
| [Vision](done/KernSmith-vision.md) | Original project vision and goals |
| [Data Types](done/plan-data-types.md) | All shared types, interfaces, and error handling (source of truth) |
| [Project Structure](done/plan-project-structure.md) | Solution layout, namespace mapping, dependencies |
| [API Design](done/plan-api-design.md) | Public API surface, builder pattern, configuration types |
| [Font Parsing](done/plan-font-parsing.md) | FreeTypeSharp usage, TTF parser scope, GPOS parsing |
| [Rasterization](done/plan-rasterization.md) | Glyph rasterization pipeline, FreeTypeRasterizer |
| [Texture Packing](done/plan-texture-packing.md) | MaxRects/Skyline algorithms, multi-page strategy |
| [Output Formats](done/plan-output-formats.md) | BMFont model classes, text/XML/binary serialization |
| [Testing](done/plan-testing.md) | xUnit test strategy, test fonts, validation criteria |
| [Implementation Order](done/plan-implementation-order.md) | Original phased task breakdown with dependency graphs (Phases 1-4) |
| [Future Phases](done/plan-phase-future.md) | Deferred items + Phases 5-7 tracking |
| [CLI Tool](done/plan-cli.md) | Full CLI plan -- BMFont.exe replacement |
| [BMFont Parity](done/plan-bmfont-parity.md) | 15 missing features from BMFont.exe |
| [Color Fonts](done/plan-color-fonts.md) | COLRv0/CPAL, sbix, CBDT support |
| [Font Subsetting](done/plan-font-subsetting.md) | Logical subsetting -- filter cmap/kern/GPOS |
| [Extended Metadata](done/plan-extended-metadata.md) | SDF spread, gradient, shadow metadata in .fnt |
| [Bug Fixes](done/plan-bug-fixes.md) | All applied bug fixes |

---

## Phased Implementation

### Phase 1 -- MVP (COMPLETE)
Core pipeline end-to-end: load a TTF, parse required tables, rasterize glyphs, pack into atlas, output BMFont text format + PNG. 28 tasks covering FreeTypeSharp integration, all table parsers (including GPOS), MaxRects packer, atlas builder, PNG encoding, text format output, file output, entry point wiring, CharacterSet, and basic tests.

### Phase 2 -- Complete (COMPLETE)
Additional output formats (XML, binary), Skyline packer, system font enumeration, configurable padding/spacing/outline, SDF mode, font collection (.ttc) support, variable font support, and `GenerateFromSystem()` API.

### Phase 3 -- Ecosystem (COMPLETE)
WOFF/WOFF2 decompression, channel packing, reference CLI tool, performance benchmarks, color font support, font subsetting, NuGet publishing CI, CLI tests.

### Phase 4 -- Deferred / Future (COMPLETE)
fvar table parser, BMFont reader (text/XML/binary), KernSmith.Load() entry point, gradient post-processor, variable font axis API, variable font tests.

### Phase 5 -- Full CLI Tool (COMPLETE)
Production-ready CLI with 5 commands (generate, inspect, convert, list-fonts, info), .bmfc config file support, and full option coverage.

### Phase 6 -- BMFont Parity Features (COMPLETE)
Separate texture W/H, TGA output, super sampling, fallback glyph, hinting toggle, force offsets to zero, equalize cell heights, autofit texture size, failed character reporting, shadow post-processor.

### Phase 7 -- Extended Metadata (COMPLETE)
KernSmith-specific metadata (SDF spread, gradient, shadow, outline, variable axes) stored inline in .fnt output across all three formats (text, XML, binary).

### Phase 8 -- Optimal Atlas Sizing (COMPLETE)
Mathematical atlas size prediction using shelf-packing estimation. Reduces packing runs from 3-5 to 1-2. Handles channel packing, equalized cell heights, non-square optimization, and int64 overflow prevention for large CJK sets.

### Phase 9 -- Outline Rendering Overhaul (COMPLETE)
Replaced binary brute-force outline with EDT-based anti-aliased distance rendering. Added configurable outline color (RGB), alpha-over compositing, RGBA input support. FT_Stroker P/Invoke bindings implemented but disabled (tracked in Phase 12).

### Phase 10 -- Layered Rendering (COMPLETE)
Replaced order-dependent post-processor chain with layered compositing system. IGlyphEffect interface, GlyphCompositor, fixed Z-order (shadow -> outline -> body). Eliminates all post-processor ordering bugs.

### Phase 11 -- Solution Restructure (COMPLETE)
Multi-project foundation: Directory.Build.props, central package management, global.json, .editorconfig, net10.0 migration, CLI promotion from samples/ to tools/, future app scaffolding (UI/Web/Mobile), solution filters. Also fixed UTF-8 BOM bug in .fnt writer and channel packing + effects validation.

### Phase 12 -- Pre-Ship Polish (PLANNING)
Security hardening (10 items), test coverage gaps (~30 new tests), NuGet package readiness (LICENSE, URLs, SourceLink, XML docs, CHANGELOG), API documentation polish, optional FT_Stroker compositing fix.

### Phase 17 -- Rebrand to KernSmith (COMPLETE)
Full project rename from KernSmith to KernSmith -- namespaces, assemblies, directories, project files, docs, CLI commands, NuGet package, and all references.

### Phase 30 -- WASM Rasterization (PLANNING)
Live investigation of WASM-compatible font rasterizers. Prior preliminary research suggested server-side rasterization or SkiaSharp, but findings were not validated with actual testing. Requires checking current FreeTypeSharp WASM status, testing Emscripten builds, evaluating SkiaSharp.Views.Blazor, and verifying IRasterizer swappability.

### Phase 13 -- Batch CLI (COMPLETE)
Batch command for multi-file .bmfc processing with output collision detection, parallel execution, and font caching. 18 fonts in 1.5s vs 22s with separate invocations.

### Phase 14 -- Benchmarking & Profiling (COMPLETE)
50+ benchmarks across 7 classes, PipelineMetrics with stage-level timing, CLI --time/--profile flags, benchmark command, CI workflow. R2R tested and rejected (15% slower on .NET 10). AOT deferred.

### Phase 15 -- Library Performance & Batch API (COMPLETE)
Moved font caching and batch execution from CLI into NuGet library. Static SystemFontProvider singleton eliminates 800ms/call overhead. FontCache class, KernSmith.GenerateBatch() with parallelism. 18 fonts in 196ms via library API.

### Phase 16 -- BMFont .bmfc Compatibility (COMPLETE)
Replaced custom INI-style .bmfc format with standard AngelCode BMFont flat key=value format. Same .bmfc files work in both BMFont and KernSmith -- BMFont ignores extension keys. Legacy INI parser removed. All 32 test .bmfc files converted.

---

## Reference Documents

| # | Document | Description |
|---|----------|-------------|
| REF-01 | [Font Library Comparison](../reference/REF-01-font-library-comparison.md) | Evaluation of .NET font libraries |
| REF-02 | [FreeTypeSharp Evaluation](../reference/REF-02-freetypesharp-evaluation.md) | Detailed FreeTypeSharp capabilities and gaps |
| REF-03 | [TTF Font Reference](../reference/REF-03-ttf-font-reference.md) | TrueType font format reference |
| REF-04 | [Other Font Formats](../reference/REF-04-other-font-formats-reference.md) | WOFF, OTF, and other format details |
| REF-05 | [BMFont Format Reference](../reference/REF-05-bmfont-format-reference.md) | BMFont file format specification |
| REF-06 | [Texture Packing Reference](../reference/REF-06-texture-packing-reference.md) | Rectangle packing algorithm research |

---

## Resolved Decisions

| # | Question | Decision | Details |
|---|----------|----------|---------|
| 1 | **PNG encoding library** | **StbImageWriteSharp** (public domain) | Confirmed. See [done/plan-project-structure.md](done/plan-project-structure.md). |
| 2 | **Target framework** | **net10.0** | Migrated from net8.0 in Phase 11. All projects unified on net10.0 via Directory.Build.props. |
| 3 | **Project license** | **Proprietary** | See LICENSE file. |
| 4 | **NuGet package name** | **KernSmith** | Package ID `KernSmith`, main API class `KernSmith`. |
| 5 | **FreeTypeSharp usage boundary** | Use everything it can do | Our parser only covers what FreeTypeSharp cannot (GPOS, OS/2, name, cmap). No duplication. |
| 6 | **Unsafe code policy** | `AllowUnsafeBlocks` in main project | Isolated to FreeType interop (`FreeTypeRasterizer.cs`, `FreeTypeNative.cs`). Rest is safe C#. |
| 7 | **FreeType memory** | Manual lifecycle via `IDisposable` | Pin font data with `GCHandle`. Do NOT use `FreeTypeFaceFacade`. See [done/plan-rasterization.md](done/plan-rasterization.md). |
| 8 | **Test framework** | **xUnit** + FluentAssertions | See [done/plan-testing.md](done/plan-testing.md). |
| 9 | **Error handling** | Custom exception hierarchy | `FontParsingException`, `RasterizationException`, `AtlasPackingException`. See [done/plan-data-types.md](done/plan-data-types.md). |

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
