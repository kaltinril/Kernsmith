# bmfontier -- Outline Rendering Overhaul

> Replace the binary brute-force outline with anti-aliased distance-based rendering,
> add outline color support, and optionally use FreeType's native FT_Stroker for best quality.
>
> **Date**: 2026-03-19

---

## Problem

The current `OutlinePostProcessor` produces jagged, ugly outlines:
1. Binary 0/255 output — no anti-aliasing on the outer edge
2. No outline color — gradient colors outline identically to glyph body, making it invisible
3. Pipeline ordering broken — outline after gradient skips RGBA; outline before gradient gets overwritten
4. Source alpha treated as binary (`> 0` threshold) — ignores glyph anti-aliasing
5. O(W×H×outlineWidth²) brute-force performance
6. Pass 2 compositing uses simple overwrite instead of alpha-over blending

---

## Three-Tier Solution

### Tier 1: Fix OutlinePostProcessor (immediate, managed C#)

Replace binary output with distance-based anti-aliased rendering:

**1a. Euclidean Distance Transform (EDT)**
Replace the brute-force neighbor scan with Felzenszwalb-Huttenlocher EDT:
- O(N) linear time regardless of outline width (vs current O(N×OW²))
- Separable: two passes of 1D transforms (columns then rows)
- Produces exact float distances at every pixel
- Reference implementation: Mapbox's `tiny-sdf`

**1b. Anti-aliased alpha from distance**
Instead of binary 0/255, compute smooth alpha:
```
alpha = clamp(255 * (outlineWidth - distance + 0.5) / 1.0, 0, 255)
```
The +0.5 creates a 1-pixel smooth transition zone at the outline's outer edge.

**1c. Add outline color (R, G, B)**
`OutlinePostProcessor` gets configurable color parameters (default black: 0,0,0).
Output becomes RGBA:
- Outline pixels: (outlineR, outlineG, outlineB, computed_alpha)
- Glyph body: composite on top using alpha-over blending (like ShadowPostProcessor)

**1d. Fix pipeline ordering**
Insert outline at the BEGINNING of the post-processor list (`Insert(0, ...)` not `Add()`).
Since it now outputs RGBA with baked colors, subsequent processors (gradient) need adjustment.

**1e. Gradient interaction**
Two options:
- **Option A**: Gradient only colors the glyph body region, not the outline. Requires tracking which pixels are "body" vs "outline" — could use a mask or the distance field itself.
- **Option B**: Outline runs AFTER gradient. Outline processor accepts RGBA input, extracts alpha for distance computation, then composites outline (with its own color) underneath the colored glyph. This is simpler and matches Hiero's approach.

**Recommended: Option B** — outline extracts alpha from the input (grayscale or RGBA), computes the distance-based outline, renders it with the outline color, then composites the original glyph on top. Works regardless of pipeline ordering.

### Tier 2: FT_Stroker integration (medium-term, best quality)

FreeType's `FT_Stroker` operates on vector outlines before rasterization — geometrically precise with perfect anti-aliasing.

**API workflow:**
1. `FT_Stroker_New(library, &stroker)`
2. `FT_Stroker_Set(stroker, radius_in_26_6, FT_STROKER_LINECAP_ROUND, FT_STROKER_LINEJOIN_ROUND, 0)`
3. `FT_Load_Glyph(face, index, FT_LOAD_NO_BITMAP)` — load vector outline
4. `FT_Get_Glyph(face->glyph, &glyph)` — copy
5. `FT_Glyph_StrokeBorder(&glyph, stroker, false, true)` — get outer border
6. `FT_Glyph_To_Bitmap(&glyph, FT_RENDER_MODE_NORMAL, null, true)` — rasterize

FreeTypeSharp exposes the full Stroker API (confirmed in `reference/freetypesharp-evaluation.md`).

**Integration**: Add `RasterizeOutline(int codepoint, int outlineWidth)` to `FreeTypeRasterizer` that returns a separate outline bitmap per glyph. `BmFont.Generate()` uses this when FreeTypeRasterizer is detected, falling back to the EDT post-processor for custom rasterizers.

### Tier 3: GlyphAndOutline channel encoding fix

The `ChannelCompositor.ResolveChannel` for `GlyphAndOutline` currently uses simple addition (`Math.Min(255, glyph + outline)`). Per BMFont spec, it should use threshold encoding:
- Outline maps to 0-127 range
- Glyph maps to 128-255 range
- Shader decodes: `glyph = val > 0.5 ? 2*val-1 : 0`, `outline = val > 0.5 ? 1 : 2*val`

---

## Task Breakdown

| # | Task | Tier | Effort | Files |
|---|------|------|--------|-------|
| 1 | Implement Felzenszwalb-Huttenlocher EDT | 1 | Medium | New: `Atlas/EuclideanDistanceTransform.cs` |
| 2 | Rewrite OutlinePostProcessor with EDT + distance-based alpha | 1 | Medium | `Rasterizer/OutlinePostProcessor.cs` |
| 3 | Add outline color (R,G,B) parameters + RGBA output | 1 | Small | `Rasterizer/OutlinePostProcessor.cs` |
| 4 | Alpha-over compositing for glyph body on outline | 1 | Small | `Rasterizer/OutlinePostProcessor.cs` |
| 5 | Accept RGBA input (extract alpha channel) | 1 | Small | `Rasterizer/OutlinePostProcessor.cs` |
| 6 | Fix pipeline ordering: outline runs after gradient | 1 | Small | `BmFont.cs` |
| 7 | Update CLI `--outline` to accept optional color | 1 | Small | `GenerateCommand.cs` |
| 8 | Update BmFontBuilder fluent API for outline color | 1 | Small | `BmFontBuilder.cs` |
| 9 | Add FT_Stroker P/Invoke bindings | 2 | Medium | `Rasterizer/FreeTypeNative.cs` |
| 10 | Implement FreeTypeRasterizer.RasterizeOutline() | 2 | Medium | `Rasterizer/FreeTypeRasterizer.cs` |
| 11 | Wire FT_Stroker path into BmFont.Generate() | 2 | Small | `BmFont.cs` |
| 12 | Fix GlyphAndOutline threshold encoding | 3 | Small | `Atlas/ChannelCompositor.cs` |
| 13 | Tests for EDT, outline quality, color, RGBA input | All | Medium | `tests/` |

---

## EDT Algorithm (Felzenszwalb-Huttenlocher)

For each row/column, computes the lower envelope of parabolas:

```
function EDT_1D(f, n):
    d = new float[n]     // output distances
    v = new int[n]       // locations of parabolas
    z = new float[n+1]   // boundaries between parabolas
    k = 0
    v[0] = 0
    z[0] = -INF
    z[1] = +INF

    for q = 1 to n-1:
        while true:
            s = ((f[q] + q*q) - (f[v[k]] + v[k]*v[k])) / (2*q - 2*v[k])
            if s > z[k]: break
            k--
        k++
        v[k] = q
        z[k] = s
        z[k+1] = +INF

    k = 0
    for q = 0 to n-1:
        while z[k+1] < q: k++
        d[q] = (q - v[k])^2 + f[v[k]]

    return d
```

Run on columns first, then rows. Total: O(W×H).

---

## Comparison

| Aspect | Current | Tier 1 (EDT) | Tier 2 (FT_Stroker) |
|--------|---------|-------------|---------------------|
| Quality | Binary, jagged | Smooth, anti-aliased | Perfect, vector-precise |
| Performance | O(N×OW²) | O(N) | O(outline_points) |
| Outline color | None | Configurable RGB | Configurable RGB |
| RGBA input | Skipped | Supported | N/A (pre-rasterization) |
| Corner joins | None | Distance-based | Round/miter/bevel |
| Dependencies | Pure C# | Pure C# | FreeTypeSharp |

---

## Estimated Effort

- **Tier 1**: 2-3 days — EDT + rewritten post-processor + color + pipeline fix
- **Tier 2**: 1-2 days — FT_Stroker P/Invoke + integration
- **Tier 3**: 0.5 days — channel encoding fix
- **Risk**: Low for Tier 1 (well-understood algorithms), Medium for Tier 2 (unsafe interop)

---

## References

- Felzenszwalb & Huttenlocher (2012). "Distance Transforms of Sampled Functions"
- Mapbox tiny-sdf — reference EDT implementation (github.com/mapbox/tiny-sdf)
- FreeType Stroker API — freetype.org/freetype2/docs/reference/ft2-glyph_stroker.html
- BMFont pixel shader reference — angelcode.com/products/bmfont/doc/pixel_shader.html
- libGDX FreeTypeFontGenerator — Hiero's FT_Stroker implementation
