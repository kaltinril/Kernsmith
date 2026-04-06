# Phase 98: Outline Advance Bug Report -- INVALID

> **Status**: Rejected
> **Date**: 2026-04-05
> **Origin**: External user bug report
> **Verdict**: Current behavior is correct; no changes needed

---

## Original Claim

A user reported that when generating bitmap fonts with `OutlineThickness > 0`, characters overlap each other because `Advance` (xadvance) is not adjusted by `+ 2 * outlineWidth` after outline expansion. The report identified three code locations as buggy:

1. `OutlinePostProcessor.cs` line 148 -- `Advance: metrics.Advance` (unchanged)
2. `BmFont.cs` lines 356-377 -- empty glyph path passes `Metrics` unchanged
3. `GlyphCompositor.cs` lines 96/203 -- effects path passes `Advance` unchanged

The suggested fix was to add `2 * outlineWidth` to `Advance` in all three locations.

## Why This Is Wrong

### 1. BMFont spec says outline does NOT modify xadvance

`reference/REF-08-bmfont-internals.md` (lines 235-260) is explicit:

> - Padding does NOT modify xadvance
> - **Outline does NOT modify xadvance**
> - The outline thickness from the info block is available for dynamic adjustment at render time

BMFont's design: outline is a purely visual expansion. The `outline` value is written into the `.fnt` info block so **renderers** can optionally adjust spacing at runtime if desired. The font generation tool does not bake this into xadvance.

### 2. Three existing tests guard the current behavior

In `tests/KernSmith.Tests/Integration/EndToEndTests.cs`:

- `Generate_WithOutlineProperty_AdvanceIsUnchanged()` (line 414)
- `Generate_WithOutlinePostProcessor_AdvanceIsUnchanged()` (line 444)
- `Generate_WithOutlineAndCustomChannels_ExpandsMetrics()` (line 473)

All assert `charWith.XAdvance.ShouldBe(charWithout.XAdvance)` with the message: *"outline should not change the advance -- outlines overlap into adjacent glyph space"*.

### 3. All outline paths are intentionally consistent

The report flags `OutlinePostProcessor`, `BmFont.cs` (empty glyphs), and `GlyphCompositor` as all having the "same bug." They are all consistently implementing correct BMFont behavior -- outlines overlap into adjacent glyph space by design.

### 4. The proposed test would fail against current (correct) behavior

The suggested unit test asserts `XAdvance + 6 == XAdvanceWithOutline`, which contradicts the three existing tests that assert the opposite.

## Actual Issue

The overlap the user observed in their FontPlayground app is a **renderer-side issue**. The fix belongs in the text rendering code, not in KernSmith:

- **Option A**: Read the `outline` value from the `.fnt` info block and add `2 * outlineThickness` to the cursor advance during text layout
- **Option B**: Increase `Padding` in generation options to add visual spacing without changing advance metrics

## Code Locations Reviewed (all confirmed correct)

| File | Line | Behavior | Verdict |
|------|------|----------|---------|
| `src/KernSmith/Rasterizer/OutlinePostProcessor.cs` | 148 | `Advance: metrics.Advance` | Correct |
| `src/KernSmith/BmFont.cs` | 371 | `Metrics = g.Metrics` (empty glyphs) | Correct |
| `src/KernSmith/Rasterizer/GlyphCompositor.cs` | 96, 203 | `Advance: metrics.Advance` | Correct |
| `src/KernSmith/Output/BmFontModelBuilder.cs` | 156 | `XAdvance: glyph.Metrics.Advance` | Correct (pass-through) |
