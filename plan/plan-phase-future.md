# bmfontier -- Future Phase Plan

> Incomplete items collected from Phase 3 and Phase 4, plus planned future phases.
> These features are deferred until dependencies are resolved or prioritized.
>
> **Date**: 2026-03-19

---

## Deferred from Phase 3

Items that were not completed during Phase 3 (Ecosystem).

| Original ID | Task | Description | Notes |
|-------------|------|-------------|-------|
| 13B | **Color font support** | COLRv0/CPAL layer rendering, sbix bitmap extraction, CBDT/CBLC bitmap extraction | Three separate implementations. High effort, low priority. |
| 13C | **Font subsetting** | Strip unused glyphs from font data before processing (reduces memory for large CJK fonts) | Medium effort. Important for CJK workflows. |
| 15C | **NuGet publishing CI** | Configure CI for NuGet pack + push, README, package icon | A `publish.yml` workflow exists but may need verification and finalization. |
| 16C | **Tests: CLI** | End-to-end CLI invocation tests | Depends on the reference CLI tool (15A, complete). |

---

## Deferred from Phase 4

Items that were not completed during Phase 4 (Deferred / Future).

| Original ID | Task | Description | Notes |
|-------------|------|-------------|-------|
| 17B | **Variable font axis API** | Expose axes on FontInfo, add `Dictionary<string, float> VariationAxes` to FontGeneratorOptions, call `FT_Set_Var_Design_Coordinates` in FreeTypeRasterizer before rendering | **Blocked**: FreeTypeSharp lacks `FT_Set_Var_Design_Coordinates` binding. fvar table parsing (17A) is complete. |
| 18A | **Tests: variable fonts** | Load a variable font, set weight axis, verify different rasterization output | Depends on 17B. Blocked until axis application is unblocked. |

---

## Phase 5 -- Full CLI Tool

Production-ready CLI that replaces BMFont.exe. Config file support (.bmfc), inspect/convert/info commands, .NET global tool packaging.

See **[plan-cli.md](plan-cli.md)** for full specification and task breakdown (Phases A-D).

---

## Phase 6 -- BMFont Parity Features

15 features from BMFont.exe not yet implemented: separate texture W/H, per-channel configuration, TGA/DDS output, super sampling, fallback glyph, hinting toggle, force offsets to zero, equalize cell heights, height stretch, autofit texture size, custom glyph images, failed character reporting.

See **[plan-bmfont-parity.md](plan-bmfont-parity.md)** for prioritized feature list with implementation details.

---

## Phase 7 -- Extended Metadata

Store bmfontier-specific metadata (SDF spread, gradient, shadow settings) inline in .fnt output using custom fields that existing BMFont readers safely ignore. Follows Hiero's precedent.

See **[plan-extended-metadata.md](plan-extended-metadata.md)** for full specification, compatibility analysis, and implementation plan.
