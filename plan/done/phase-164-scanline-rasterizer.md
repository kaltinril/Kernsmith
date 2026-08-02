# Phase 164 — Native Rasterizer: Scanline Rasterizer Core

> **Status**: **COMPLETE** (2026-08-01)
> **Created**: 2026-04-01
> **Depends on**: Phase 163 (outline extraction, edge generation)

> **Input contract from Phase 163** (complete — see `done/phase-163-outline-extraction.md`):
> - `OutlineFlattener.Flatten(outline, transform, tolerance)` returns `EdgeSegment[]` already in **pixel space** (scale and Y-flip applied via `OutlineTransform`), in original winding order, with horizontal edges already dropped. This phase does not re-scale or re-flip.
> - Contours arrive **already closed**: `OutlineCommandType.Close` carries the contour's start point, so the flattener emits the closing segment itself. There is no unclosed-contour case to handle here.
> - `GlyphOutline.XMin/YMin/XMax/YMax` is a **conservative control-hull box** (bounds control points too), not the tight `glyf` declared box. Sizing the bitmap from it is safe — it can only over-estimate — but it may leave a row/column of empty pixels beyond the 1-pixel padding, so trim from actual coverage if a tight box is required.

## What Shipped

| Item | Where |
|------|-------|
| `RasterResult` output record (bitmap + width/height + bearings) | `src/KernSmith.Rasterizers.Native/Internal/Raster/RasterResult.cs` |
| Signed-area trapezoid scanline rasterizer with active-edge table | `Internal/Raster/ScanlineRasterizer.cs` |
| `area[]` / `cover[]` from `ArrayPool<float>.Shared` (cleared after rent, returned in `finally`) | `Internal/Raster/ScanlineRasterizer.cs` |
| Output bitmap allocated with `new byte[]` (never pooled — the caller holds it for the glyph's lifetime) | `Internal/Raster/ScanlineRasterizer.cs` |
| 35 tests (19 rasterizer + 16 SSIM), bringing `KernSmith.Rasterizers.Native.Tests` to 137 per TFM | `tests/KernSmith.Rasterizers.Native.Tests/ScanlineRasterizerTests.cs`, `ScanlineRasterizerSsimTests.cs` |
| `ProjectReference` to `KernSmith.Rasterizers.StbTrueType` as the SSIM baseline (**test-only** — nothing shipped depends on it) | `tests/KernSmith.Rasterizers.Native.Tests/KernSmith.Rasterizers.Native.Tests.csproj` |

Entry point as shipped:

```csharp
internal static RasterResult Rasterize(
    EdgeSegment[] edges,
    int width,
    int height,
    float originX = 0f,
    float originY = 0f,
    AntiAliasMode antiAlias = AntiAliasMode.Grayscale,
    int bearingX = 0,
    int bearingY = 0);
```

All accumulation math is `float` throughout, matching the reference implementations. Nothing is wired into `NativeRasterizer` yet; that is Phase 165.

**Deviations from the plan as written**

- The **existing core `KernSmith.AntiAliasMode` enum is reused** rather than adding a parallel Native-only one. `AntiAliasMode.None` thresholds coverage at 128; every other value passes the grayscale coverage through unchanged. This keeps Phase 165's `RasterOptions.AntiAlias` a straight pass-through with no mapping layer.
- **`originX` / `originY` were added beyond the literal spec signature.** They define which pixel-space point maps to bitmap (0, 0), so Phase 165 can place the glyph box without allocating a translated `EdgeSegment[]` per glyph.
- **Per-glyph `RasterEdge[]` arrays are plain allocations, not pooled.** Only the `area` / `cover` buffers were required to be pooled, and pooling the edge array adds a lifetime/clearing concern for no measured win. Flagged as a follow-up if Phase 165 benchmarking shows GC pressure.
- **A real bug was caught by TDD**: non-finite edge coordinates (NaN/Infinity) propagated into the scanline bounds and threw `IndexOutOfRangeException`. Fixed with an `IsFinite` filter in `Prepare`, which drops such edges before they reach the active-edge table.
- **SSIM vs StbTrueType**: all 15 glyph/size cases clear the > 0.95 floor. Straight-edged glyphs ('I', 'X', 'W' at 12/32/96 px) score > 0.999. Curved cases: 'O' 12px **0.9509**, 16px 0.9863, 24px 0.9937, 32px 0.9718, 48px 0.9867, 96px 0.9958; 'e' 16px 0.9814; '8' 48px 0.9982.
  - ⚠️ **'O' @ 12px at 0.9509 is a thin margin against the 0.95 floor** — it is the first test that will trip if `OutlineFlattener` changes. Treat any flattener tolerance/subdivision edit as requiring a re-run of `ScanlineRasterizerSsimTests`.
  - The curved residual was traced to **curve tessellation, not coverage**: tightening the flattener tolerance moved scores *away* from stb, indicating stb's subdivision is the coarser approximation, not ours.
- **The stb prefilter idea was deliberately not implemented.** The `stbtt__h_prefilter` / `stbtt__v_prefilter` note below is carried forward as a possible later optimization for sub-20px quality — it is deferred, not dropped.
- **Performance was not benchmarked.** No benchmark was written for the "within 3x of StbTrueType for ASCII at 32px" criterion, so it is left unticked and explicitly unverified. Add it under `benchmarks/KernSmith.Benchmarks/` when the backend is wired up in Phase 165.

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

> **FontStashSharp insight:** stb_truetype includes lightweight horizontal/vertical box prefilter kernels (`stbtt__h_prefilter`, `stbtt__v_prefilter`) that blur along one axis as a post-rasterization step for subpixel positioning. This is much cheaper than full supersampling and improves small-size rendering quality. Consider implementing a similar 1D prefilter pass as an optional post-rasterization step, especially for sizes under ~20px where aliasing is most visible.

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

- [x] Rasterizer produces correct 8-bit grayscale output (row-major, pitch == width, coverage verified against hand-computed trapezoid areas for axis-aligned, diagonal and sub-pixel edges)
- [x] Coverage values match reference (SSIM > 0.95 vs StbTrueType) — all 15 glyph/size cases pass; straight-edged glyphs > 0.999, curved glyphs 0.9509–0.9982 (see the SSIM bullet above; 'O' @ 12px is the thin margin)
- [x] Both anti-alias modes work (Grayscale, None) — `None` asserted to emit only 0 or 255 via a 128 threshold; every other mode passes coverage through
- [x] No buffer overruns or out-of-bounds access (edges far outside the bitmap on all four sides, zero-size bitmaps, and non-finite coordinates all clip or drop instead of overrunning)
- [x] ArrayPool buffers properly rented and returned (`area`/`cover` rented from `ArrayPool<float>.Shared`, cleared after rent since the pool does not zero, returned in a `finally`)
- [ ] Performance within 3x of StbTrueType for ASCII set at 32px — **NOT VERIFIED / DEFERRED**: no benchmark was written in this phase. Add one under `benchmarks/KernSmith.Benchmarks/` once Phase 165 wires the backend into `IRasterizer` so the comparison can run end-to-end.
- [x] All tests pass — 137 in `KernSmith.Rasterizers.Native.Tests` (net8.0 + net10.0), 35 of them new here
