# Phase 164 — Native Rasterizer: Scanline Rasterizer Core

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 163 (outline extraction, edge generation)

## Goal

Implement the core scanline rasterizer using the signed-area trapezoid coverage method. This is the heart of the rasterizer — it converts directed edge segments into an 8-bit grayscale bitmap.

## Scope

### Algorithm: Signed-Area Trapezoid Coverage

This is the method used by stb_truetype v2 and font-rs. For each pixel, compute exact fractional coverage.

#### Step 1: Edge Sorting
- Sort all edges by minimum Y coordinate (top of edge)
- Secondary sort by X at minimum Y

#### Step 2: Scanline Processing
For each pixel row (scanline):
1. Activate edges whose top Y ≤ current scanline
2. Deactivate edges whose bottom Y ≤ current scanline  
3. For each active edge crossing this scanline band:
   - Clip edge to the 1-pixel-tall scanline band [y, y+1]
   - For each pixel column the clipped edge crosses:
     - Compute the signed trapezoid area contribution
     - Add to accumulation buffer `area[x]` and carry buffer `cover[x]`

#### Step 3: Coverage Accumulation
For each scanline, left-to-right:
```
float runningCover = 0;
for (int x = 0; x < width; x++)
{
    runningCover += cover[x];
    float coverage = Math.Abs(area[x] + runningCover);
    bitmap[y * width + x] = (byte)Math.Min(coverage * 255f, 255f);
    area[x] = 0;   // reset for next scanline
    cover[x] = 0;
}
```

### Core Math: Edge-Pixel Intersection

For a directed edge from (x0, y0) to (x1, y1) crossing pixel column `px`, row `py`:

1. Clip edge to pixel bounds [py, py+1] vertically
2. Compute X at clipped top and bottom: `x_top`, `x_bottom`
3. Clip horizontally to [px, px+1]
4. Compute area and cover contributions:
   - `area[px] += signed_trapezoid_area`
   - `cover[px] += signed_height`

The signed area accounts for winding direction — edges going up contribute positive, edges going down contribute negative (or vice versa, consistently).

### Buffer Management

**Internal buffers** (rented from pool, returned after each glyph):
- `float[] area` — per-pixel area accumulation (width of bitmap)
- `float[] cover` — per-pixel cover accumulation (width of bitmap)
- Both rented from `ArrayPool<float>.Shared`, returned after rasterization completes
- Reused across scanlines (reset after each row)

**Output bitmap** (freshly allocated, caller owns):
- `byte[] output` — final bitmap (width × height), allocated with `new byte[]`
- NOT rented from ArrayPool — callers (`RasterizedGlyph.BitmapData`) hold references indefinitely
- The existing `RasterizedGlyph` has no `IDisposable` pattern, so pooled output would leak or corrupt

### Anti-Alias Modes

- **Grayscale** (default): Output the coverage value directly (0–255)
- **None**: Threshold at 128 — output 0 or 255

### Output

```csharp
internal sealed class RasterResult
{
    public byte[] Bitmap { get; }   // 8-bit grayscale, width × height
    public int Width { get; }
    public int Height { get; }
    public int BearingX { get; }    // pixel offset from origin
    public int BearingY { get; }    // pixel offset from baseline
}
```

### Conversion to RasterizedGlyph

Phase 165 converts `RasterResult` to the public `RasterizedGlyph` type:
- `Bitmap` → `BitmapData`
- `Width` → `Width`
- `Height` → `Height`
- `Width` → `Pitch` (no row padding, so pitch = width for Grayscale8, width*4 for Rgba32)
- `BearingX/BearingY` → `Metrics` (wrapped in `GlyphMetrics` with advance from hmtx)
- Codepoint and GlyphIndex added by Phase 165's caller context
- `Format` = `PixelFormat.Grayscale8` for standard rasterization

## Key Implementation Details

- **Subpixel positioning**: Edge coordinates are floating-point. The rasterizer naturally handles fractional positions.
- **Clipping**: Edges extending outside the bitmap bounds must be clipped. Missing this causes buffer overruns.
- **Empty scanlines**: Skip scanlines with no active edges for performance.
- **Numeric stability**: Use `float` (not `double`) — precision is sufficient and matches reference implementations.
- **Bitmap padding**: Add 1-pixel border to avoid edge artifacts (trim in final output if needed).

## Reference

- stb_truetype.h: `stbtt__rasterize_sorted_edges` (~line 3400)
- stb_truetype.h: `stbtt__fill_active_edges_new` (~line 3250)
- stb_truetype.h: `stbtt__handle_clipped_edge` (~line 3200)
- font-rs: `accumulate` function
- Sean Barrett's explanation: https://nothings.org/gamedev/rasterize/

## Testing

- Render 'I' (simple rectangle): verify sharp vertical edges, correct coverage at boundaries
- Render 'O' (curves): verify smooth anti-aliased edges
- Render 'X' (diagonal lines): verify correct diagonal coverage
- Render at multiple sizes (12, 16, 24, 32, 48, 96 px)
- Compare against StbTrueType output: SSIM > 0.95 at all sizes
- Anti-alias None mode: verify binary output (0 or 255 only)
- Edge cases: very small glyphs (8px), very large (200px), glyphs with overlapping contours
- Performance: benchmark ASCII set at 32px, compare to StbTrueType

## Success Criteria

- [ ] Rasterizer produces correct 8-bit grayscale output
- [ ] Coverage values match reference (SSIM > 0.95 vs StbTrueType)
- [ ] Both anti-alias modes work (Grayscale, None)
- [ ] No buffer overruns or out-of-bounds access
- [ ] ArrayPool buffers properly rented and returned
- [ ] Performance within 3x of StbTrueType for ASCII set at 32px
- [ ] All tests pass
