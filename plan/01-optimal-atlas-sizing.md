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

### Channel Packing

When `ChannelPacking = true`, 4 glyphs share one pixel position via RGBA channels. The effective total area is divided by 4 (equivalently, glyph count is divided by 4) before estimation. Without this adjustment, the estimated atlas is 4x too large.

### Equalized Cell Heights Fast Path

When `EqualizeCellHeights = true`, all glyphs are padded to the same height, making packing a 1D strip problem. The estimator detects this and uses a simpler formula:

```
cells_per_row = floor(width / cell_width)
rows = ceil(N / cells_per_row)
height = rows * cell_height
```

### Non-Square Optimization

`EstimateShelfHeight(W)` is monotonically non-increasing. The shelf height function is a step function, so `W * H(W)` has discontinuities and can have local minima at step boundaries. Binary/ternary search is unsound for this function.

For power-of-two sizes: only ~7 candidate widths (64-4096), each evaluated in O(N). Use exhaustive evaluation of all POT width candidates to find the minimum.

For arbitrary (non-POT) sizes: use exhaustive sweep over step-function breakpoints (the widths where a glyph moves shelves) rather than binary search.

---

## Algorithm

### AtlasSizeEstimator.Estimate()

```
Input: list of glyph rects (already including padding/spacing), sizing options
Output: (width, height)

0. Filter out glyphs with zero width or height
1. Compute total_area (using long to avoid int32 overflow), max_width, max_height
2. If channel packing enabled: divide effective total_area by 4
3. If equalized cell heights: use fast-path formula (cells_per_row / rows)
4. area_lower = ceil(sqrt(total_area / efficiency))
5. lower_bound = max(area_lower, max_width, max_height, min_size)
6. Apply safety margin: multiply shelf estimate by 1.05 to reduce verification failures

If square required:
    7. side = lower_bound
    8. If power-of-two: side = NextPowerOfTwo(side)
    9. Return (side, side)

If non-square allowed:
    7. Sort glyphs by height descending
    8. If power-of-two: exhaustively evaluate W*H at each POT width, pick minimum
    9. If arbitrary: sweep over step-function breakpoints between max_width and sum(widths)
   10. Return (bestW, bestH)

Post-estimation:
   11. If estimated size exceeds MaxTextureWidth/MaxTextureHeight, clamp to max dimensions
       and let the packer produce multiple pages (preserve graceful multi-page fallback)
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
| 3 | Handle channel packing in estimator (divide effective area by 4) | `Atlas/AtlasSizeEstimator.cs` | Small |
| 4 | Handle equalized cell heights fast path | `Atlas/AtlasSizeEstimator.cs` | Small |
| 5 | Use `long` for all area calculations to avoid int32 overflow | `Atlas/AtlasSizeEstimator.cs` | Trivial |
| 6 | Filter zero-area glyph rects before estimation | `Atlas/AtlasSizeEstimator.cs` | Trivial |
| 7 | Refactor autofit block in `BmFont.Generate()` to use estimator + single verification | `BmFont.cs` | Medium |
| 8 | Refactor non-autofit block to use estimator instead of hardcoded 1.2x | `BmFont.cs` | Small |
| 9 | Add `PackingEfficiencyHint` (`internal`, clamped to [0.50, 0.99]) to `FontGeneratorOptions` | `FontGeneratorOptions.cs` | Small |
| 10 | Unit tests for estimator (empty, single, uniform, tall, many-small, non-square) | `tests/.../AtlasSizeEstimatorTests.cs` | Medium |
| 11 | Review-identified test cases: channel packing, equalized heights, large CJK set (int overflow), zero-area glyphs, max-texture clamping, pathological distributions needing multiple bumps | `tests/.../AtlasSizeEstimatorTests.cs` | Medium |
| 12 | Fluent builder method `WithPackingEfficiency(float)` | `BmFontBuilder.cs` | Trivial |

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

## Review Findings

Issues identified during QA review that the implementation must address:

**HIGH — Shelf estimate is FFDH-specific, not MaxRects**
The shelf estimate is a valid upper bound for FFDH packing but could underestimate what MaxRects/Skyline needs for certain glyph distributions. The verification pass handles this, but pathological cases could need multiple bumps. Implementation should apply a small safety margin (e.g., multiply shelf estimate by 1.05) to reduce verification failures.

**HIGH — Channel packing mode unaddressed**
When `ChannelPacking = true`, 4 glyphs share one pixel position via RGBA channels. The estimator must divide effective total area by 4 (or equivalently, divide glyph count by 4) when channel packing is enabled. Without this, the estimated atlas is 4x too large.

**HIGH — EqualizeCellHeights changes packing characteristics**
When all glyphs are padded to the same height, packing becomes a 1D strip problem. The estimator should detect this and use a simpler formula:
```
cells_per_row = floor(width / cell_width)
rows = ceil(N / cells_per_row)
height = rows * cell_height
```

**MEDIUM — W×H(W) is not convex for step functions**
The shelf height function is a step function, so `W * H(W)` has discontinuities and can have local minima at step boundaries. Binary/ternary search is unsound. For arbitrary (non-POT) sizes, use exhaustive sweep over step-function breakpoints (the widths where a glyph moves shelves). For POT sizes, exhaustive evaluation of ~7 candidates is trivially fast.

**MEDIUM — MaxTexture clamping and multi-page fallback**
When the estimated size exceeds MaxTextureWidth/MaxTextureHeight, clamp to max dimensions and let the packer produce multiple pages. Explicitly preserve the current graceful degradation to multi-page output.

**MEDIUM — PackingEfficiencyHint API**
Make this property `internal` rather than public. Clamp to [0.50, 0.99] in the estimator. Document that the default 0.90 is tuned for MaxRects BSSF with font glyphs.

**MEDIUM — Integer overflow for large character sets**
Use `long` for total area calculations. Full CJK at 64px with padding approaches int32 limits.

**MEDIUM — Estimator must receive padded GlyphRects**
Document explicitly that the estimator operates on GlyphRects that already include padding and spacing (as built in BmFont.cs). Do not add padding inside the estimator.

**LOW — Zero-area glyph rects**
Filter out glyphs with zero width or height before estimation to avoid NaN/division-by-zero.

**LOW — Thread safety**
`AtlasSizeEstimator` must be stateless (no static mutable fields). All state flows through parameters.

---

## References

- Jylänki, J. (2010). "A Thousand Ways to Pack the Bin" — MaxRects efficiency data
- Steinberg, A. (1997). "A Strip-Packing Algorithm with Absolute Performance Bound 3/2" — Feasibility conditions
- Coffman, Garey, Johnson, Tarjan (1980). "Performance Bounds for Level-Oriented Two-Dimensional Packing Algorithms" — FFDH bounds

---

## Estimated Effort

- **Total**: 1-2 days
- **Risk**: Low — additive feature, falls back to current behavior if estimate misses
