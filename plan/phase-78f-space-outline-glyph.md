# Phase 78F -- Space Character Outline Rendering

> **Status**: Planning
> **Size**: Small
> **Created**: 2026-03-27
> **Dependencies**: None
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Render outline pixels for space (and other zero-width glyphs) so they have real bitmap entries in the atlas, matching BMFont behavior.

---

## Problem

BMFont renders outline pixels even for space (char 32), producing a small bitmap (e.g., `width=11 height=9` at size 56 with outline=4). KernSmith outputs `width=0 height=0` for space, meaning no pixels exist in the atlas for that character.

This matters because bitmap font consumers copy character rectangles from the atlas PNG using the x/y/width/height from the .fnt file. A width=0/height=0 entry gives them nothing to copy. While space is visually empty, the outline effect should still produce a rectangular region -- the outline is applied around the glyph shape, and even an empty glyph with advance width occupies space that outline/effects may fill.

Each font defines its own space width via xadvance, so this cannot be faked -- the outline bitmap must be generated from the actual glyph metrics.

## Expected Behavior

When outline > 0, space and other zero-bitmap glyphs should:
1. Still go through the outline/effects pipeline
2. Produce a bitmap with outline pixels (even if the interior is empty)
3. Have correct width/height in the .fnt output
4. Occupy real space in the atlas

When outline = 0, space should remain width=0 height=0 (no pixels needed).

## Tasks

- [ ] Identify where zero-size glyphs are skipped in the rasterization/effects pipeline
- [ ] When outline > 0, generate an outline bitmap for glyphs that have advance width but no visible pixels
- [ ] Verify all rasterizer backends (FreeType, GDI, DirectWrite) produce consistent results
- [ ] Test with various outline thicknesses and font sizes
- [ ] Compare output against BMFont64 for space character

## Files to Investigate

| File | Why |
|------|-----|
| `src/KernSmith/BmFont.cs` | Main pipeline -- likely where zero-size glyphs are filtered out |
| `src/KernSmith/Rasterizer/GlyphCompositor.cs` | Effects pipeline -- outline applied here |
| `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` | May return null/empty for space |
