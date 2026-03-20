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
| ~~13B~~ | ~~**Color font support**~~ | ~~COLRv0/CPAL, sbix, CBDT via FT_LOAD_COLOR + RGBA atlas~~ | **DONE** — Phases A-C implemented. 20 tests + 4 skip-ready. Plan: [plan-color-fonts.md](plan-color-fonts.md). |
| ~~13C~~ | ~~**Font subsetting**~~ | ~~Logical subsetting — filter cmap/kern/GPOS during parsing~~ | **DONE** — 22 tests. Plan: [plan-font-subsetting.md](plan-font-subsetting.md). |
| ~~15C~~ | ~~**NuGet publishing CI**~~ | ~~Configure CI for NuGet pack + push, README, package icon~~ | **DONE** — publish.yml updated, .csproj metadata added, README created. |
| ~~16C~~ | ~~**Tests: CLI**~~ | ~~End-to-end CLI invocation tests~~ | **DONE** — 20 tests in `tests/Bmfontier.Tests/Cli/CliTests.cs`. |

---

## Deferred from Phase 4

Items that were not completed during Phase 4 (Deferred / Future).

| Original ID | Task | Description | Notes |
|-------------|------|-------------|-------|
| ~~17B~~ | ~~**Variable font axis API**~~ | ~~Call `FT_Set_Var_Design_Coordinates` in FreeTypeRasterizer before rendering~~ | **DONE** — Custom P/Invoke in `FreeTypeNative.cs`, axis application in `FreeTypeRasterizer.SetVariationAxes()`. |
| ~~18A~~ | ~~**Tests: variable fonts**~~ | ~~Load a variable font, set weight axis, verify different rasterization output~~ | **DONE** — 7 passing + 10 ready for variable font fixture in `VariableFontTests.cs`. |

---

## Phase 5 -- Full CLI Tool (COMPLETE)

Production-ready CLI with 5 commands (generate, inspect, convert, list-fonts, info), .bmfc config file support, and full option coverage.

See **[plan-cli.md](plan-cli.md)** for specification.

---

## Phase 6 -- BMFont Parity Features (COMPLETE)

Implemented: separate texture W/H, TGA output, super sampling, fallback glyph, hinting toggle, force offsets to zero, equalize cell heights, autofit texture size, failed character reporting, shadow post-processor (beyond parity).

See **[plan-bmfont-parity.md](plan-bmfont-parity.md)** for full feature list.

---

## Phase 7 -- Extended Metadata (COMPLETE)

bmfontier-specific metadata (SDF spread, gradient, shadow, outline, variable axes) stored inline in .fnt output across all three formats (text, XML, binary). Existing BMFont readers safely ignore the additions.

See **[plan-extended-metadata.md](plan-extended-metadata.md)** for specification.
