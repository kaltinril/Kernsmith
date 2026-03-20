# Phase 02 — Complete

> **Status**: Complete
> **Original doc**: plan-implementation-order.md
> **Date**: 2026-03-19

---

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
