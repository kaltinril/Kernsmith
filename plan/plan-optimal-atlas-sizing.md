# bmfontier -- Optimal Atlas Sizing Plan

> Predict the minimum atlas texture size mathematically instead of brute-force trial-and-error.
> Uses shelf-packing estimation with binary search for non-square, single verification pack for correctness.
>
> **Date**: 2026-03-19

---

## Problem

The current autofit loop runs the packer 3-5 times, doubling dimensions until glyphs fit. The non-autofit path uses a hardcoded 1.2x area factor. Both are wasteful.

---

## Mathematical Foundation

### Lower Bounds (no packing needed)

```
total_area   = sum(glyph_width * glyph_height)  // includes padding/spacing
max_width    = max(glyph_width)
max_height   = max(glyph_height)
side_min     = max(ceil(sqrt(total_area / efficiency)), max_width, max_height)
```

### Packing Efficiency

MaxRects BSSF achieves 92-97% efficiency on font glyphs (nearly uniform height, moderate width variance). Conservative default: **0.90** (90%).

Source: Jylänki (2010), "A Thousand Ways to Pack the Bin"

### Steinberg's Theorem (provable upper bound)

Rectangles with total area A, max width w_max, max height h_max can ALWAYS be packed into W×H if:
- W >= 2 * w_max
- H >= 2 * h_max
- W * H >= 2 * A

This gives a guaranteed upper bound: `ceil(sqrt(2 * total_area))`.

### Shelf-Based Height Estimation

Simulate FFDH shelf packing in O(N) without any bitmap operations:

```
function EstimateShelfHeight(sortedGlyphs, width):
    totalHeight = 0
    shelfWidth = 0
    shelfHeight = 0
    for each glyph (sorted by height descending):
        if shelfWidth + glyph.Width > width:
            totalHeight += shelfHeight
            shelfWidth = 0
            shelfHeight = 0
        shelfWidth += glyph.Width
        shelfHeight = max(shelfHeight, glyph.Height)
    totalHeight += shelfHeight
    return totalHeight
```

### Non-Square Optimization

`EstimateShelfHeight(W)` is monotonically non-increasing. The product `W * H(W)` forms a roughly convex curve. Binary search (or ternary search) finds the width that minimizes total pixels in O(N log N).

For power-of-two: only ~7 candidate widths (64-4096), each evaluated in O(N). Trivially fast.

---

## Algorithm

### AtlasSizeEstimator.Estimate()

```
Input: list of glyph rects, sizing options
Output: (width, height)

1. Compute total_area, max_width, max_height
2. area_lower = ceil(sqrt(total_area / efficiency))
3. lower_bound = max(area_lower, max_width, max_height, min_size)

If square required:
    4. side = lower_bound
    5. If power-of-two: side = NextPowerOfTwo(side)
    6. Return (side, side)

If non-square allowed:
    4. Sort glyphs by height descending
    5. If power-of-two: evaluate W*H at each POT width, pick minimum
    6. If arbitrary: binary search on width between max_width and sum(widths)
    7. Return (bestW, bestH)
```

### Integration: Prediction + Single Verification

1. Estimate optimal size with AtlasSizeEstimator
2. Run packer ONCE with estimated size
3. If it fits on one page → done (typical case)
4. If overflow → bump one step (double smaller dimension or next POT)

Reduces packing runs from 3-5 to 1-2.

---

## Comparison with Current Approach

| Aspect | Current Autofit | Proposed |
|--------|----------------|----------|
| Packing runs | 3-5 typical | 1 (verification only) |
| NPOT support | No | Yes |
| Non-square optimization | Limited (doubles smaller dim) | Full (binary search on aspect ratio) |
| Accuracy | Exact (finds smallest POT) | Near-optimal (within one step) |
| Complexity | O(P × N log N), P=iterations | O(N log N) estimate + O(N log N) single pack |

---

## Task Breakdown

| # | Task | File(s) | Effort |
|---|------|---------|--------|
| 1 | Create `AtlasSizeEstimator` static class with `Estimate()` and `EstimateShelfHeight()` | `Atlas/AtlasSizeEstimator.cs` | Medium |
| 2 | Create `AtlasSizingOptions` record (efficiency, POT, non-square, max dims) | `Atlas/AtlasSizeEstimator.cs` (nested) | Small |
| 3 | Refactor autofit block in `BmFont.Generate()` to use estimator + single verification | `BmFont.cs` | Medium |
| 4 | Refactor non-autofit block to use estimator instead of hardcoded 1.2x | `BmFont.cs` | Small |
| 5 | Add `PackingEfficiencyHint` to `FontGeneratorOptions` (optional, default 0.90) | `FontGeneratorOptions.cs` | Small |
| 6 | Unit tests for estimator (empty, single, uniform, tall, many-small, non-square) | `tests/.../AtlasSizeEstimatorTests.cs` | Medium |
| 7 | Fluent builder method `WithPackingEfficiency(float)` | `BmFontBuilder.cs` | Trivial |

---

## Edge Cases

| Scenario | Handling |
|----------|----------|
| Empty glyph list | Return (MinSize, MinSize) |
| Single glyph | Return glyph dimensions + POT rounding |
| Very tall narrow glyphs | max_height dominates; shelves naturally handle |
| Many tiny glyphs | Area dominates; compact square layout |
| One huge + many small | max constraints kick in; huge glyph gets own shelf |
| Exceeds max texture | Clamp to max; packer overflows to multiple pages |

---

## References

- Jylänki, J. (2010). "A Thousand Ways to Pack the Bin" — MaxRects efficiency data
- Steinberg, A. (1997). "A Strip-Packing Algorithm with Absolute Performance Bound 3/2" — Feasibility conditions
- Coffman, Garey, Johnson, Tarjan (1980). "Performance Bounds for Level-Oriented Two-Dimensional Packing Algorithms" — FFDH bounds

---

## Estimated Effort

- **Total**: 1-2 days
- **Risk**: Low — additive feature, falls back to current behavior if estimate misses
