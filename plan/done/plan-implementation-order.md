# KernSmith -- Implementation Order

> Detailed task breakdown for all phases. Tasks are ordered by dependencies to maximize parallel execution.
> Each task lists the plan doc(s) a coder agent needs and which tasks must complete first.

---

## How to Read This Plan

- **Tasks with the same dependency set can run in parallel**
- **Docs to read**: the plan files the coder agent should read before starting
- **Depends on**: task IDs that must be complete before this task can start
- **Parallel group**: tasks sharing a group letter can run concurrently

---

## Phase 1 — MVP

Goal: End-to-end pipeline that loads a TTF, parses required tables, rasterizes glyphs, packs into atlas, outputs BMFont text format + PNG.

### Group A — Foundation (no dependencies, all parallel)

| ID | Task                         | Description                                                                                                                                      | Docs to Read                                                                                         |
|----|------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------|
| 1A | **Exception types**          | Implement `KernSmithException`, `FontParsingException`, `RasterizationException`, `AtlasPackingException` with constructors and message formatting | plan-data-types.md (Error Handling section only)                                                     |
| 1B | **Config types**             | Implement `Padding`, `Spacing` (with convenience constructors), `PixelFormat`, `AntiAliasMode`, `PackingAlgorithm`, `OutputFormat` enums, `FontGeneratorOptions`, `RasterOptions` | plan-data-types.md (Configuration Types section), plan-api-design.md (FontGeneratorOptions, CharacterSet sections) |
| 1C | **CharacterSet**             | Implement `CharacterSet` with `Ascii`, `ExtendedAscii`, `Latin` statics, `FromRanges`, `FromChars`, `Union`, `GetCodepoints`, `Resolve`          | plan-api-design.md (CharacterSet section)                                                            |
| 1D | **BMFont model classes**     | Implement `BmFontModel`, `InfoBlock`, `CommonBlock`, `PageEntry`, `CharEntry`, `KerningEntry` as immutable records                                | plan-output-formats.md (BmFontModel section only)                                                    |
| 1E | **Font model types**         | Implement `FontInfo`, `KerningPair`, `GlyphMetrics`, `HeadTable`, `HheaTable`, `Os2Metrics`, `NameInfo`                                          | plan-data-types.md (Font Layer Types section)                                                        |
| 1F | **Packing model types**      | Implement `GlyphRect`, `GlyphPlacement`, `PackResult`, `AtlasPage`                                                                               | plan-data-types.md (Texture Packing + Atlas sections)                                                |

### Group B — Core Parsers (depends on Group A)

| ID | Task                         | Depends On | Description                                                                                                         | Docs to Read                                                                          |
|----|------------------------------|-----------|---------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|
| 2A | **TTF table directory parser** | 1E        | Parse sfnt header, locate table records by tag. This is the foundation for all other table parsers.                  | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (File Structure section)        |
| 2B | **head table parser**        | 2A         | Parse head table: unitsPerEm, bounding box, index format, timestamps                                                | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (head section)                  |
| 2C | **hhea table parser**        | 2A         | Parse hhea table: ascender, descender, lineGap, numberOfHMetrics                                                     | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (hhea section)                  |
| 2D | **hmtx table parser**        | 2A, 2C    | Parse hmtx: per-glyph advance widths and left side bearings. Needs numberOfHMetrics from hhea.                       | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (hmtx section)                  |
| 2E | **OS/2 table parser**        | 2A         | Parse OS/2: weight class, typo metrics, panose, x-height, cap height, char range                                     | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (OS/2 section)                  |
| 2F | **name table parser**        | 2A         | Parse name: font family, subfamily, full name, PostScript name, copyright                                            | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (name section)                  |
| 2G | **cmap table parser**        | 2A         | Parse cmap: format 4 (BMP) and format 12 (full Unicode). Build codepoint→glyphIndex map.                            | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (cmap section)                  |
| 2H | **kern table parser**        | 2A         | Parse legacy kern table: format 0 subtables, extract horizontal kerning pairs                                        | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (kern section)                  |

### Group C — Rasterizer + Packer (depends on Group A, parallel with Group B)

| ID | Task                    | Depends On | Description                                                                                                                                                                                                                         | Docs to Read                                                       |
|----|-------------------------|-----------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------|
| 3A | **FreeTypeRasterizer**  | 1E, 1A    | Implement IRasterizer using FreeTypeSharp: LoadFont (pin data, create library+face), RasterizeGlyph (load glyph, render, copy bitmap, build GlyphMetrics), RasterizeAll, Dispose. Handle null for missing glyphs, normalize mono to grayscale. | plan-rasterization.md, reference/REF-02-freetypesharp-evaluation.md       |
| 3B | **MaxRectsPacker**      | 1F, 1A    | Implement IAtlasPacker using MaxRects BSSF: free rect list, placement scoring, 4-way splitting, containment pruning, multi-page overflow, height-descending pre-sort                                                                | plan-texture-packing.md, reference/REF-06-texture-packing-reference.md (MaxRects section) |
| 3C | **StbPngEncoder**       | 1F        | Implement IAtlasEncoder using StbImageWriteSharp: encode byte[] pixel data to PNG bytes                                                                                                                                              | plan-texture-packing.md (IAtlasEncoder section)                    |
| 3D | **AtlasBuilder**        | 1F, 3C    | Implement atlas page composition: allocate page buffers, copy glyph bitmaps at packed positions with padding offsets, create AtlasPage instances                                                                                     | plan-texture-packing.md (AtlasBuilder section)                     |

### Group D — GPOS Parser (depends on Group B foundation, can run parallel with Group C)

| ID | Task                    | Depends On | Description                                                                                                                                                                                              | Docs to Read                                                             |
|----|-------------------------|-----------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| 4A | **GPOS table parser**   | 2A        | Parse GPOS: navigate header → ScriptList → FeatureList → LookupList, find 'kern' feature, extract PairPos (format 1 + format 2) with Coverage and ClassDef parsing. Handle Extension Lookup (Type 9) unwrapping. | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (GPOS section) |

### Group E — Output Formatters (depends on Group A model classes)

| ID | Task                    | Depends On | Description                                                                                                                            | Docs to Read                                         |
|----|-------------------------|-----------|----------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------|
| 5A | **TextFormatter**       | 1D        | Implement IBmFontTextFormatter: serialize BmFontModel to BMFont text format following serialization rules (quoting, booleans as 0/1, padding/spacing format) | plan-output-formats.md (Text Format section)         |
| 5B | **FileWriter**          | 1D, 1F   | Implement FileWriter: write .fnt file + .png atlas pages to disk, handle directory creation, page naming pattern                        | plan-output-formats.md (FileWriter section)          |

### Group F — Integration (depends on most of the above)

| ID | Task                              | Depends On                        | Description                                                                                                                              | Docs to Read                                                     |
|----|-----------------------------------|----------------------------------|------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------|
| 6A | **TtfFontReader**                 | 2B, 2C, 2D, 2E, 2F, 2G, 2H, 4A | Implement IFontReader: combine FreeTypeSharp loading with TtfParser, merge kern+GPOS kerning (GPOS takes precedence), build FontInfo      | plan-font-parsing.md                                             |
| 6B | **BmFontModelBuilder**            | 1D, 1E, 1F                      | Internal helper that assembles BmFontModel from FontInfo + RasterizedGlyph[] + PackResult + options. Maps glyph metrics to CharEntry fields, scales kerning pairs. | plan-output-formats.md, plan-data-types.md                       |
| 6C | **BmFontResult**                  | 1D, 5A, 5B                      | Implement BmFontResult: holds Model + Pages, delegates to formatters for ToString/ToXml/ToBinary/ToFile                                   | plan-data-types.md (BmFontResult section), plan-output-formats.md |
| 6D | **BmFont.Generate() entry point** | 6A, 3A, 3B, 3D, 6B, 6C, 1B, 1C | Wire the full pipeline: parse font → resolve charset → rasterize → pack → build atlas → build model → return result                      | plan-api-design.md (Generate pipeline section)                   |

### Group G — Phase 1 Tests (depends on implementation being available)

| ID | Task                          | Depends On   | Description                                                                                                         | Docs to Read     |
|----|-------------------------------|-------------|---------------------------------------------------------------------------------------------------------------------|-----------------|
| 7A | **Unit tests: parsers**       | 2B-2H, 4A  | Test each table parser against known values from Roboto-Regular.ttf (generate golden data with ttx)                  | plan-testing.md |
| 7B | **Unit tests: packer**        | 3B          | Test MaxRectsPacker with known glyph sizes, verify no overlaps, verify multi-page overflow                           | plan-testing.md |
| 7C | **Unit tests: formatter**     | 5A          | Test TextFormatter output against known-good BMFont text format                                                      | plan-testing.md |
| 7D | **Unit tests: CharacterSet**  | 1C          | Test Ascii, FromRanges, FromChars, Union, Resolve                                                                    | plan-testing.md |
| 7E | **Integration: end-to-end**   | 6D          | Load Roboto-Regular.ttf → Generate ASCII BMFont → verify .fnt parses, .png has content, metrics reasonable           | plan-testing.md |

### Phase 1 Dependency Graph (visual summary)

```
Group A (all parallel, no deps)
  1A  1B  1C  1D  1E  1F
   \   |   |   |   |   /
    v  v   v   v   v  v
Group B (parsers)          Group C (raster+pack)     Group E (output)
  2A ──────────────┐        3A  3B  3C                5A  5B
  / | | | | | \    │         \   |  /                  |
 2B 2C 2D 2E 2F 2G 2H       3D─┘                     │
  \  \ |  /  /  /  /         |                         │
   v  v v  v  v v v          v                         v
Group D          Group F (integration)
  4A ──────────> 6A  6B  6C
                  \   |  /
                   v  v v
                    6D ──> Group G (tests)
                            7A 7B 7C 7D 7E
```

---

## Phase 2 — Complete

Goal: Additional output formats, Skyline packer, system fonts, SDF, .ttc support, variable fonts.

### Group H — Output Formats (no Phase 2 dependencies between these)

| ID | Task                        | Depends On       | Description                                                                        | Docs to Read                                                                                    |
|----|-----------------------------|-----------------|-------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------|
| 8A | **XmlFormatter**            | Phase 1 complete | Implement IBmFontTextFormatter for XML output                                       | plan-output-formats.md (XML Format section)                                                     |
| 8B | **BmFontBinaryFormatter**   | Phase 1 complete | Implement IBmFontBinaryFormatter: block-based binary format with bitfield packing    | plan-output-formats.md (Binary Format section), reference/REF-05-bmfont-format-reference.md (Block 1-5 sections) |

### Group I — Additional Packers + Features (parallel with Group H)

| ID | Task                            | Depends On       | Description                                                                              | Docs to Read                                                                         |
|----|--------------------------------|-----------------|------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|
| 9A | **SkylinePacker**              | Phase 1 complete | Implement IAtlasPacker using Skyline Bottom-Left algorithm                                | plan-texture-packing.md, reference/REF-06-texture-packing-reference.md (Skyline section)    |
| 9B | **Outline post-processor**     | Phase 1 complete | Implement IGlyphPostProcessor that adds configurable outline/border to glyphs             | plan-rasterization.md (post-processors section)                                      |
| 9C | **SDF rendering mode**         | Phase 1 complete | Enable FreeType's FT_RENDER_MODE_SDF in FreeTypeRasterizer, handle SDF-specific metrics   | plan-rasterization.md, reference/REF-02-freetypesharp-evaluation.md                         |
| 9D | **Configurable padding/spacing** | Phase 1 complete | Ensure padding and spacing options fully propagate through the pipeline (may already work from Phase 1) | plan-texture-packing.md, plan-api-design.md                                          |

### Group J — System Fonts + Collections

| ID  | Task                            | Depends On       | Description                                                                                              | Docs to Read                                                                                    |
|-----|--------------------------------|-----------------|----------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------|
| 10A | **ISystemFontProvider**        | Phase 1 complete | Implement system font enumeration for Windows, macOS, Linux (registry, /Library/Fonts, /usr/share/fonts)  | plan-data-types.md (ISystemFontProvider), reference/REF-04-other-font-formats-reference.md (System Font Locations section) |
| 10B | **BmFont.GenerateFromSystem()** | 10A             | Add system font entry point to BmFont class                                                               | plan-api-design.md                                                                              |
| 10C | **Font collection (.ttc) support** | Phase 1 complete | Verify/fix TtfParser TTC header handling, test with NotoSansCJK.ttc, ensure faceIndex works              | plan-font-parsing.md, reference/REF-04-other-font-formats-reference.md (.ttc section)                  |
| 10D | **Variable font support**      | Phase 1 complete | Parse fvar table for axis enumeration and named instances                                                 | reference/REF-04-other-font-formats-reference.md (Variable Fonts section)                              |

### Group K — BmFontBuilder (depends on output formats)

| ID  | Task              | Depends On | Description                                                                        | Docs to Read                         |
|-----|-------------------|-----------|------------------------------------------------------------------------------------|--------------------------------------|
| 11A | **BmFontBuilder** | 8A, 8B    | Implement fluent builder pattern as syntactic sugar over FontGeneratorOptions        | plan-api-design.md (Builder section) |

### Group L — Phase 2 Tests

| ID  | Task                              | Depends On   | Description                                                                       | Docs to Read     |
|-----|-----------------------------------|-------------|-----------------------------------------------------------------------------------|-----------------|
| 12A | **Tests: XML + Binary formatters** | 8A, 8B      | Verify XML and binary output against reference data                                | plan-testing.md |
| 12B | **Tests: Skyline packer**         | 9A          | Compare packing results and verify no overlaps                                     | plan-testing.md |
| 12C | **Tests: system fonts**           | 10A         | Platform-conditional tests for font enumeration                                    | plan-testing.md |
| 12D | **Tests: .ttc loading**           | 10C         | Load NotoSansCJK.ttc with different face indices                                   | plan-testing.md |
| 12E | **Tests: end-to-end multi-format** | 8A, 8B, 9A | Generate same font in text/XML/binary, verify all three parse correctly             | plan-testing.md |

### Phase 2 Parallelism

```
Phase 1 complete
       |
       +──────────────────────────────────────+
       |              |              |         |
    Group H       Group I        Group J    Group K (after H)
    8A  8B      9A 9B 9C 9D    10A 10C 10D    11A
       |           |              |              |
       v           v              v              v
                Group L (tests)
           12A 12B 12C 12D 12E
```

---

## Phase 3 — Ecosystem (COMPLETE)

> **Status**: Complete. Remaining incomplete items (13B, 13C, 15C, 16C) moved to **[plan-phase-future.md](plan-phase-future.md)**.

Goal: WOFF support, channel packing, color fonts, CLI tool, benchmarks, NuGet publishing, font subsetting.

### Group M — Format Support

| ID  | Task                    | Depends On       | Status | Description                                                                                        | Docs to Read                                                                  |
|-----|-------------------------|-----------------|--------|----------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------|
| 13A | **WOFF decompression**  | Phase 2 complete | DONE | Implement WOFF/WOFF2 header parsing and zlib/Brotli decompression to extract inner TTF/OTF          | reference/REF-04-other-font-formats-reference.md (WOFF/WOFF2 sections)    |
| 13B | **Color font support**  | Phase 2 complete | MOVED | COLRv0/CPAL layer rendering, sbix bitmap extraction, CBDT/CBLC bitmap extraction                    | Moved to [plan-phase-future.md](plan-phase-future.md)    |
| 13C | **Font subsetting**     | Phase 2 complete | MOVED | Strip unused glyphs from font data before processing (reduces memory for large CJK fonts)           | Moved to [plan-phase-future.md](plan-phase-future.md)                |

### Group N — Advanced Atlas Features

| ID  | Task                    | Depends On       | Status | Description                                                                       | Docs to Read                                                                                                         |
|-----|-------------------------|-----------------|--------|-----------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------|
| 14A | **Channel packing**     | Phase 2 complete | DONE | Pack monochrome glyphs into individual RGBA channels for 4x density                | reference/REF-06-texture-packing-reference.md (Channel Packing section), reference/REF-05-bmfont-format-reference.md (Channel section) |

### Group O — Tooling + Publishing

| ID  | Task                        | Depends On                       | Status | Description                                                                                          | Docs to Read                 |
|-----|-----------------------------|----------------------------------|--------|------------------------------------------------------------------------------------------------------|------------------------------|
| 15A | **Reference CLI tool**      | Phase 2 complete                 | DONE | Simple CLI wrapper: `KernSmith generate -f font.ttf -s 32 -o output/`                                | New plan doc needed          |
| 15B | **Performance benchmarks**  | Phase 2 complete                 | DONE | Benchmark suite: measure time/memory for ASCII set at various sizes, compare MaxRects vs Skyline      | New plan doc needed          |
| 15C | **NuGet publishing**        | Phase 2 complete, license decided | MOVED | Configure CI for NuGet pack + push, README, package icon                                             | Moved to [plan-phase-future.md](plan-phase-future.md)    |

### Group P — Phase 3 Tests

| ID  | Task                        | Depends On | Status | Description                                                              | Docs to Read     |
|-----|-----------------------------|-----------|--------|--------------------------------------------------------------------------|-----------------|
| 16A | **Tests: WOFF**             | 13A       | DONE | Load WOFF/WOFF2 font, verify decompression and generation                 | plan-testing.md |
| 16B | **Tests: channel packing**  | 14A       | DONE | Verify channel-packed atlas has glyphs in correct channels                 | plan-testing.md |
| 16C | **Tests: CLI**              | 15A       | MOVED | End-to-end CLI invocation tests                                           | Moved to [plan-phase-future.md](plan-phase-future.md) |

### Phase 3 Parallelism

```
Phase 2 complete
       |
       +───────────────────────────+
       |           |           |    |
    Group M     Group N     Group O
   13A 13B 13C    14A      15A 15B 15C
       |           |           |
       v           v           v
            Group P (tests)
           16A  16B  16C
```

---

## Phase 4 — Deferred / Future (COMPLETE)

> **Status**: Complete. Remaining incomplete items (17B, 18A) moved to **[plan-phase-future.md](plan-phase-future.md)**.

### Group Q — Variable Fonts + BMFont Reader (all parallel)

| ID  | Task | Depends On | Status | Description | Docs to Read |
|-----|------|-----------|--------|-------------|-------------|
| 17A | **fvar table parser** | Phase 3 complete | DONE | Parse fvar table: axis count, axis records (tag, min, default, max, nameID), named instance records. Add to TtfParser. | reference/REF-04-other-font-formats-reference.md (Variable Fonts section) |
| 17B | **Variable font axis API** | 17A | MOVED | Expose axes on FontInfo, add `Dictionary<string, float> VariationAxes` to FontGeneratorOptions, call `FT_Set_Var_Design_Coordinates` in FreeTypeRasterizer before rendering | Moved to [plan-phase-future.md](plan-phase-future.md). Blocked: FreeTypeSharp lacks `FT_Set_Var_Design_Coordinates`. |
| 17C | **BMFont reader (text)** | Phase 3 complete | DONE | Parse BMFont text format .fnt file into BmFontModel. Line-by-line parser matching tag + key=value pairs. | plan-output-formats.md (Text Format section) |
| 17D | **BMFont reader (XML)** | Phase 3 complete | DONE | Parse BMFont XML format .fnt file into BmFontModel using XmlReader. | plan-output-formats.md (XML Format section) |
| 17E | **BMFont reader (binary)** | Phase 3 complete | DONE | Parse BMFont binary format .fnt file into BmFontModel. Block-by-block reader. | plan-output-formats.md (Binary Format section), reference/REF-05-bmfont-format-reference.md |
| 17F | **BmFont.Load() entry point** | 17C, 17D, 17E | DONE | `BmFont.Load(string path)` and `BmFont.Load(byte[] fntData, byte[][] atlasPages)` — auto-detect format, parse .fnt, load .png atlas pages, return BmFontResult | plan-api-design.md |
| 17G | **Gradient post-processor** | Phase 3 complete | DONE | IGlyphPostProcessor that applies a configurable color gradient to glyph bitmaps, producing RGBA output | plan-rasterization.md |

### Group R — Phase 4 Tests

| ID  | Task | Depends On | Status | Description |
|-----|------|-----------|--------|-------------|
| 18A | **Tests: variable fonts** | 17A, 17B | MOVED | Moved to [plan-phase-future.md](plan-phase-future.md). Blocked by 17B. |
| 18B | **Tests: BMFont reader** | 17F | DONE | Generate -> save -> load round-trip, verify model equality |
| 18C | **Tests: gradient** | 17G | DONE | Verify output is RGBA, pixel values differ top-to-bottom |

---

## Summary Statistics

| Phase                | Tasks    | Completed | Moved to Future | Max Parallel Width | Critical Path Length                  |
|----------------------|----------|-----------|-----------------|---------------------|---------------------------------------|
| Phase 1 — MVP        | 28 tasks | 28        | 0               | 6 (Group A)        | 5 groups deep (A->B->D->F->G)        |
| Phase 2 — Complete   | 14 tasks | 14        | 0               | 7 (Groups H+I+J)  | 3 groups deep (H/I/J->K->L)          |
| Phase 3 — Ecosystem  | 10 tasks | 6         | 4               | 6 (Groups M+N+O)  | 2 groups deep (M/N/O->P)             |
| Phase 4 — Deferred   | 10 tasks | 8         | 2               | 5 (Group Q)        | 2 groups deep (Q->R)                 |
| **Total**            | **62 tasks** | **56** | **6**           |                     |                                       |

## Future Phases

Incomplete items from Phases 3 and 4 plus planned future phases are collected in **[plan-phase-future.md](plan-phase-future.md)**.

### Phase 5 — Full CLI Tool

Production-ready CLI that replaces BMFont.exe. Config file support (.bmfc), inspect/convert/info commands, .NET global tool packaging.

See **[plan-cli.md](plan-cli.md)** for full specification and task breakdown (Phases A-D).

### Phase 6 — BMFont Parity Features

15 features from BMFont.exe not yet implemented: separate texture W/H, per-channel configuration, TGA/DDS output, super sampling, fallback glyph, hinting toggle, force offsets to zero, equalize cell heights, height stretch, autofit texture size, custom glyph images, failed character reporting.

See **[plan-bmfont-parity.md](plan-bmfont-parity.md)** for prioritized feature list with implementation details.

### Phase 7 — Extended Metadata

Store KernSmith-specific metadata (SDF spread, gradient, shadow settings) inline in .fnt output using custom fields that existing BMFont readers safely ignore. Follows Hiero's precedent.

See **[plan-extended-metadata.md](plan-extended-metadata.md)** for full specification, compatibility analysis, and implementation plan.

---

## Critical Path (Phase 1)

The longest dependency chain determines the minimum time to MVP:

```
1E (font models) → 2A (table directory) → 2G (cmap) → 4A (GPOS) → 6A (TtfFontReader) → 6D (BmFont.Generate) → 7E (end-to-end test)
```

GPOS (4A) is the riskiest task on the critical path. It can start as soon as the table directory parser (2A) is done, and runs in parallel with the rasterizer and packer work (Group C). This means GPOS complexity does not block other work.
