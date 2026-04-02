# Phase 174 — Native Rasterizer: Auto-Hinting

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration)

## Goal

Implement automatic hinting that analyzes glyph outlines and snaps key features to the pixel grid, improving rendering quality at small sizes without requiring TrueType bytecode interpretation.

## Background

Hinting adjusts glyph outlines to align with the pixel grid, making stems consistent width and baselines sharp. Full TrueType bytecode is enormously complex (~200 opcodes, ~15,000 lines in FreeType). Auto-hinting analyzes the outline shape to achieve similar results without bytecode.

This is modeled after FreeType's auto-hinter, which detects blue zones and stems algorithmically.

## Scope

### Blue Zone Detection

Identify key vertical positions that should snap to pixel boundaries:
1. **Baseline** (y=0 for most glyphs)
2. **x-height** (top of 'x', from OS/2 sxHeight or measured)
3. **Cap height** (top of 'H', from OS/2 sCapHeight or measured)
4. **Ascender** (top of 'd', 'l', from hhea.ascender)
5. **Descender** (bottom of 'p', 'g', from hhea.descender)

For each zone, scan reference glyphs to find the exact Y coordinate range. Snap zone boundaries to pixel edges.

### Stem Detection

Find vertical and horizontal stems (consistent-width strokes):
1. Scan outline for nearly-vertical and nearly-horizontal segments
2. Group parallel segments that form stem pairs (left/right edges of vertical strokes, top/bottom of horizontal strokes)
3. Compute stem width in font units
4. Quantize stem width to integer pixels (round to nearest, with preference for consistent widths across glyphs)

### Grid Fitting

Apply hinting to outline points:
1. Snap blue zone points to pixel boundaries
2. Scale stems to quantized widths, centering on original position
3. Interpolate non-stem, non-zone points proportionally between their neighbors
4. Maintain curve smoothness (off-curve points adjust with on-curve points)

### Algorithm Outline

```
For each glyph:
  1. Identify horizontal stems (pairs of horizontal edges)
  2. Identify vertical stems (pairs of vertical edges)
  3. Snap bottom/top of each horizontal stem to blue zone pixel edges
  4. Round vertical stem widths to integer pixels
  5. For remaining points, interpolate position between hinted neighbors
  6. Rasterize the modified outline
```

### Integration

**Pipeline position**: Auto-hinting operates in pixel space (it snaps features to the pixel grid). This means it runs AFTER scaling to pixels but BEFORE the scanline rasterizer:

```
GlyphOutline (font units)
  → Synthetic transforms (bold, italic, stroke, etc. — in font units)
    → Scale to pixels (font units → pixel coordinates)
      → AUTO-HINTING (snap stems, blue zones to pixel grid — in pixel space)
        → Flatten to edges
          → Scanline rasterizer
```

This is a deviation from Phase 160's simplified pipeline diagram, which shows scaling as the last step before rasterization. Phase 160's pipeline should be read as: "outline transforms happen in font units; hinting + rasterization happen in pixel space."

- New option: `RasterOptions.EnableHinting` (already exists, default true)
- When hinting enabled and backend is Native AND Phase 174 is implemented, apply auto-hinting
- When hinting disabled, render unhinted (current behavior, skip this step)
- Auto-hinting data (blue zones, stem widths) computed once per font load, cached per size
- Add `AntiAliasMode.Light` to `SupportedAntiAliasModes` (light hinting = reduced grid-fitting)

### Limitations

- This is NOT full TrueType bytecode — it's a heuristic-based approach
- Works well for Latin, Cyrillic, Greek scripts
- Less effective for CJK (different stem structure), Arabic (cursive)
- Some fonts look better unhinted (display/decorative fonts)
- The auto-hinter modifies outline points in-place before rasterization

## Testing

- Hint 'H': verify cap height snaps to pixel boundary, stems are integer-width
- Hint 'x': verify x-height snaps to pixel boundary
- Hint 'p': verify descender snaps to pixel boundary
- Stem consistency: 'l', 'i', 't' should have same stem width at same size
- Compare hinted vs unhinted: hinted should have sharper horizontal features
- Multiple sizes (12, 14, 16, 18, 24 px): verify quality at each
- Blue zone detection: verify zones match OS/2 metrics

## Success Criteria

- [ ] Blue zones correctly detected from font metrics and reference glyphs
- [ ] Horizontal and vertical stems detected
- [ ] Stems quantized to consistent integer pixel widths
- [ ] Grid fitting produces visually sharper output at small sizes
- [ ] Auto-hinting runs once per font load (cached)
- [ ] `EnableHinting` option respected
- [ ] All tests pass
