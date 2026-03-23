# Phase 76 — Metrics Parity with BMFont

> **Status**: Investigation complete — ready for implementation
> **Created**: 2026-03-22
> **Goal**: Investigate and fix glyph metric differences between KernSmith and BMFont output.

---

## Problem

Side-by-side comparison of KernSmith vs BMFont output (same font, same sizes, rendered in GUM) shows visible differences:

1. **Letter spacing is wider in KernSmith** — xAdvance values appear larger than BMFont's, causing text to spread out more horizontally
2. **Bold/outline variants appear slightly thicker** at larger sizes
3. **Overall glyph shapes are close** but metrics (spacing, positioning) don't match BMFont exactly

Reference images saved in `plan/phase76/`.

## Root Cause (Confirmed)

**BMFont and KernSmith interpret `fontSize` differently.**

BMFont uses Windows GDI `CreateFont()` with a **positive lfHeight**, which means `fontSize` specifies the **cell height** (usWinAscent + usWinDescent scaled to pixels). KernSmith passes `fontSize` to FreeType's `FT_Set_Char_Size` as the **em square size** (ppem), which is equivalent to GDI's **negative lfHeight** (character height).

For Arial at fontSize=32:
- **BMFont**: ppem = 32 × 2048 / (1854 + 434) ≈ 28.67 → lineHeight=32, base=26
- **KernSmith**: ppem = 32.0 → lineHeight=36, base=29

KernSmith's ppem is ~12% larger, producing systematically wider metrics across all 95 test glyphs (avg xAdvance delta = +2.66, max = +5).

### Evidence

Generated identical .fnt files from both tools using Arial 32px, chars 32-126, padding 1,1,1,1, spacing 1,1. Full diff at `tests/phase76-comparison/`.

| Metric | BMFont | KernSmith | Delta |
|--------|--------|-----------|-------|
| lineHeight | 32 | 36 | +4 |
| base | 26 | 29 | +3 |
| 'A' xAdvance | 18 | 21 | +3 |
| 'H' xAdvance | 19 | 23 | +4 |
| '@' xAdvance | 27 | 32 | +5 |
| All 95 chars differ | — | — | +1 to +5 |

### Secondary Differences

- **Kerning**: 10 of 91 pairs differ by -1 (larger ppem → stronger kerning). 4 extra pairs in KernSmith (semicolon combos from GPOS data BMFont doesn't extract).
- **Rounding**: BMFont uses integer division after supersampling; KernSmith uses round-to-nearest `(v+32)>>6`. Will cause residual 1px diffs after ppem fix.
- **Hinting**: GDI native hinting vs FreeType v40 interpreter. Different grid-fitting at same ppem. Not fixable without switching to GDI.

## Implementation Plan

### Primary Fix: Font Size Scaling

In `FreeTypeRasterizer.cs`, compute effective ppem to match BMFont's cell-height sizing:

```
effectivePpem = fontSize × unitsPerEm / (usWinAscent + usWinDescent)
```

Then: `FT_Set_Char_Size(face, effectivePpem * 64, effectivePpem * 64, 72, 72)`

This should be the **default** behavior. Add a `matchCharHeight` / `charSizeMode` option that when enabled uses the current behavior (ppem = fontSize) and records a negative size in the .fnt info block (per BMFont convention).

### lineHeight and base

After fixing the ppem, also ensure `BmFontModelBuilder` computes lineHeight and base using the same formula as BMFont:
- `lineHeight` = the requested fontSize (since cell height = fontSize by design)
- `base` = ceil(usWinAscent × effectivePpem / unitsPerEm)

### Validation

Re-run the comparison diff script after changes. Target:
- lineHeight and base should match exactly
- xAdvance deltas should drop to 0-1 (residual rounding/hinting diffs)
- Kerning deltas should shrink

## Reference Files

- `plan/phase76/side-by-side.png` — side-by-side at 100%
- `plan/phase76/zoomed.png` — zoomed comparison
- `tests/phase76-comparison/` — comparison configs, generated .fnt files, diff script
- `reference/REF-09-font-metrics-and-sizing.md` — comprehensive font metrics reference

## Key Source Files

| What | Location |
|------|----------|
| xAdvance/metrics | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` |
| .fnt model building | `src/KernSmith/Output/BmFontModelBuilder.cs` |
| Font size config | `src/KernSmith/Config/FontGeneratorOptions.cs` |
| Raster options | `src/KernSmith/Rasterizer/RasterOptions.cs` |
| BMFont format ref | `reference/REF-05-bmfont-format-reference.md` |
| Font metrics ref | `reference/REF-09-font-metrics-and-sizing.md` |
