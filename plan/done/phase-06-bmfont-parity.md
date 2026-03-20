# Phase 06 — BMFont Parity Features

> **Status**: Complete
> **Original doc**: plan-phase-future.md
> **Date**: 2026-03-19

---

Implemented: separate texture W/H, TGA output, super sampling, fallback glyph, hinting toggle, force offsets to zero, equalize cell heights, autofit texture size, failed character reporting, shadow post-processor (beyond parity).

See **[plan-bmfont-parity.md](plan-bmfont-parity.md)** for full prioritized feature list with implementation details.

## Completed Deferred Items from Phase 4

Items originally deferred from Phase 4 (Deferred / Future) that were completed during this phase:

| Original ID | Task | Description | Notes |
|-------------|------|-------------|-------|
| 17B | **Variable font axis API** | Call `FT_Set_Var_Design_Coordinates` in FreeTypeRasterizer before rendering | **DONE** -- Custom P/Invoke in `FreeTypeNative.cs`, axis application in `FreeTypeRasterizer.SetVariationAxes()`. |
| 18A | **Tests: variable fonts** | Load a variable font, set weight axis, verify different rasterization output | **DONE** -- 7 passing + 10 ready for variable font fixture in `VariableFontTests.cs`. |
