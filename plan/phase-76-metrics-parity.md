# Phase 76 — Metrics Parity with BMFont

> **Status**: Planning
> **Created**: 2026-03-22
> **Goal**: Investigate and fix glyph metric differences between KernSmith and BMFont output.

---

## Problem

Side-by-side comparison of KernSmith vs BMFont output (same font, same sizes, rendered in GUM) shows visible differences:

1. **Letter spacing is wider in KernSmith** — xAdvance values appear larger than BMFont's, causing text to spread out more horizontally
2. **Bold/outline variants appear slightly thicker** at larger sizes
3. **Overall glyph shapes are close** but metrics (spacing, positioning) don't match BMFont exactly

Reference images saved in `plan/phase76/`.

## Investigation Areas

### xAdvance Calculation
- Compare how KernSmith computes `xAdvance` vs BMFont
- KernSmith uses FreeType's `advance.x` or `horiAdvance` — check which and whether it matches BMFont's approach
- Check for rounding differences (FreeType returns 26.6 fixed-point — rounding method matters)

### YOffset / XOffset
- Compare vertical and horizontal glyph positioning
- Check baseline alignment

### Line Height / Base
- Compare `lineHeight` and `base` values in the .fnt output
- BMFont may compute these differently from FreeType's `height` and `ascender`

### Padding and Spacing
- Check if padding/spacing settings affect xAdvance in KernSmith but not in BMFont (or vice versa)

## Approach

1. Generate identical fonts with both tools (same font, size, character set, no effects)
2. Diff the .fnt files to see exact metric differences per character
3. Trace KernSmith's metric calculation in `BmFontModelBuilder` and `FreeTypeRasterizer`
4. Adjust to match BMFont's behavior where appropriate

## Reference Files

- `plan/phase76/side-by-side.png` — side-by-side at 100%
- `plan/phase76/zoomed.png` — zoomed comparison

## Key Source Files

| What | Location |
|------|----------|
| xAdvance/metrics | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` |
| .fnt model building | `src/KernSmith/Output/BmFontModelBuilder.cs` |
| BMFont format ref | `reference/REF-05-bmfont-format-reference.md` |
