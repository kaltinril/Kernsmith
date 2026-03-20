# Phase 04 — Deferred / Future

> **Status**: Complete
> **Original doc**: plan-implementation-order.md
> **Date**: 2026-03-19

---

### Group Q — Variable Fonts + BMFont Reader (all parallel)

| ID  | Task | Depends On | Status | Description | Docs to Read |
|-----|------|-----------|--------|-------------|-------------|
| 17A | **fvar table parser** | Phase 3 complete | DONE | Parse fvar table: axis count, axis records (tag, min, default, max, nameID), named instance records. Add to TtfParser. | reference/REF-04-other-font-formats-reference.md (Variable Fonts section) |
| 17B | **Variable font axis API** | 17A | MOVED | Expose axes on FontInfo, add `Dictionary<string, float> VariationAxes` to FontGeneratorOptions, call `FT_Set_Var_Design_Coordinates` in FreeTypeRasterizer before rendering | Moved to [plan-phase-future.md](plan-phase-future.md). Blocked: FreeTypeSharp lacks `FT_Set_Var_Design_Coordinates`. |
| 17C | **BMFont reader (text)** | Phase 3 complete | DONE | Parse BMFont text format .fnt file into BmFontModel. Line-by-line parser matching tag + key=value pairs. | plan-output-formats.md (Text Format section) |
| 17D | **BMFont reader (XML)** | Phase 3 complete | DONE | Parse BMFont XML format .fnt file into BmFontModel using XmlReader. | plan-output-formats.md (XML Format section) |
| 17E | **BMFont reader (binary)** | Phase 3 complete | DONE | Parse BMFont binary format .fnt file into BmFontModel. Block-by-block reader. | plan-output-formats.md (Binary Format section), reference/REF-05-bmfont-format-reference.md |
| 17F | **BmFont.Load() entry point** | 17C, 17D, 17E | DONE | `BmFont.Load(string path)` and `BmFont.Load(byte[] fntData, byte[][] atlasPages)` -- auto-detect format, parse .fnt, load .png atlas pages, return BmFontResult | plan-api-design.md |
| 17G | **Gradient post-processor** | Phase 3 complete | DONE | IGlyphPostProcessor that applies a configurable color gradient to glyph bitmaps, producing RGBA output | plan-rasterization.md |

### Group R — Phase 4 Tests

| ID  | Task | Depends On | Status | Description |
|-----|------|-----------|--------|-------------|
| 18A | **Tests: variable fonts** | 17A, 17B | MOVED | Moved to [plan-phase-future.md](plan-phase-future.md). Blocked by 17B. |
| 18B | **Tests: BMFont reader** | 17F | DONE | Generate -> save -> load round-trip, verify model equality |
| 18C | **Tests: gradient** | 17G | DONE | Verify output is RGBA, pixel values differ top-to-bottom |
