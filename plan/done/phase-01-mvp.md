# Phase 01 — MVP

> **Status**: Complete
> **Original doc**: plan-implementation-order.md
> **Date**: 2026-03-19

---

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
| 2G | **cmap table parser**        | 2A         | Parse cmap: format 4 (BMP) and format 12 (full Unicode). Build codepoint->glyphIndex map.                            | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (cmap section)                  |
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
| 4A | **GPOS table parser**   | 2A        | Parse GPOS: navigate header -> ScriptList -> FeatureList -> LookupList, find 'kern' feature, extract PairPos (format 1 + format 2) with Coverage and ClassDef parsing. Handle Extension Lookup (Type 9) unwrapping. | plan-font-parsing.md, reference/REF-03-ttf-font-reference.md (GPOS section) |

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
| 6D | **BmFont.Generate() entry point** | 6A, 3A, 3B, 3D, 6B, 6C, 1B, 1C | Wire the full pipeline: parse font -> resolve charset -> rasterize -> pack -> build atlas -> build model -> return result                      | plan-api-design.md (Generate pipeline section)                   |

### Group G — Phase 1 Tests (depends on implementation being available)

| ID | Task                          | Depends On   | Description                                                                                                         | Docs to Read     |
|----|-------------------------------|-------------|---------------------------------------------------------------------------------------------------------------------|-----------------|
| 7A | **Unit tests: parsers**       | 2B-2H, 4A  | Test each table parser against known values from Roboto-Regular.ttf (generate golden data with ttx)                  | plan-testing.md |
| 7B | **Unit tests: packer**        | 3B          | Test MaxRectsPacker with known glyph sizes, verify no overlaps, verify multi-page overflow                           | plan-testing.md |
| 7C | **Unit tests: formatter**     | 5A          | Test TextFormatter output against known-good BMFont text format                                                      | plan-testing.md |
| 7D | **Unit tests: CharacterSet**  | 1C          | Test Ascii, FromRanges, FromChars, Union, Resolve                                                                    | plan-testing.md |
| 7E | **Integration: end-to-end**   | 6D          | Load Roboto-Regular.ttf -> Generate ASCII BMFont -> verify .fnt parses, .png has content, metrics reasonable           | plan-testing.md |

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

## Summary Statistics

| Phase                | Tasks    | Completed | Moved to Future | Max Parallel Width | Critical Path Length                  |
|----------------------|----------|-----------|-----------------|---------------------|---------------------------------------|
| Phase 1 — MVP        | 28 tasks | 28        | 0               | 6 (Group A)        | 5 groups deep (A->B->D->F->G)        |
| Phase 2 — Complete   | 14 tasks | 14        | 0               | 7 (Groups H+I+J)  | 3 groups deep (H/I/J->K->L)          |
| Phase 3 — Ecosystem  | 10 tasks | 6         | 4               | 6 (Groups M+N+O)  | 2 groups deep (M/N/O->P)             |
| Phase 4 — Deferred   | 10 tasks | 8         | 2               | 5 (Group Q)        | 2 groups deep (Q->R)                 |
| **Total**            | **62 tasks** | **56** | **6**           |                     |                                       |

## Critical Path (Phase 1)

The longest dependency chain determines the minimum time to MVP:

```
1E (font models) → 2A (table directory) → 2G (cmap) → 4A (GPOS) → 6A (TtfFontReader) → 6D (BmFont.Generate) → 7E (end-to-end test)
```

GPOS (4A) is the riskiest task on the critical path. It can start as soon as the table directory parser (2A) is done, and runs in parallel with the rasterizer and packer work (Group C). This means GPOS complexity does not block other work.
