# Phase 32d — StbTrueType Synthetic Bold & Italic

> **Status**: Planning
> **Created**: 2026-03-31
> **Depends on**: Phase 32 (StbTrueType rasterizer), Phase 32c (validation fixes)
> **Related**: Phase 36 (bitmap-level bold/italic post-processing)

## Goal

Implement outline-level synthetic bold and italic for the StbTrueType rasterizer, matching FreeType's approach of transforming glyph outlines before rasterization.

## Background

### The Problem

StbTrueType currently throws `NotSupportedException` for bold/italic because stb_truetype has no built-in synthesis like FreeType's `FT_GlyphSlot_Embolden` / `FT_GlyphSlot_Oblique`. This means:
- Comparison images show red cells for StbTrueType bold/italic columns
- Users on WASM/AOT platforms have no way to get bold/italic output
- The feature gap is the biggest practical limitation of the StbTrueType backend

### How Other Backends Do It

Each KernSmith rasterizer handles synthetic bold/italic at the **outline/face level before rasterization** — no bitmap post-processing exists:

| Backend | Bold | Italic |
|---------|------|--------|
| **FreeType** | `FT_Outline_Embolden(ppem/24)` — expands glyph outlines outward | `FT_GlyphSlot_Oblique` — applies horizontal shear to outline points |
| **GDI** | `LOGFONTW.LfWeight = FW_BOLD` — font mapper selects/synthesizes bold | `MAT2` shear matrix (0.364 ≈ tan(20°)) applied to `GetGlyphOutlineW` |
| **DirectWrite** | `DWRITE_FONT_SIMULATIONS_BOLD` flag on font face | `DWRITE_FONT_SIMULATIONS_OBLIQUE` flag on font face |

### Research: StbTrueTypeSharp Outline API

StbTrueTypeSharp (1.26.12) exposes the full outline API from stb_truetype:

**Outline extraction:**
- `stbtt_GetCodepointShape(fontinfo, codepoint, &vertices)` → returns array of `stbtt_vertex` (bezier control points)
- `stbtt_GetGlyphShape(fontinfo, glyphIndex, &vertices)` → same, by glyph index
- `stbtt_FreeShape(fontinfo, vertices)` → frees the vertex array

**Vertex structure:**
```csharp
public struct stbtt_vertex
{
    public short x, y;       // vertex position
    public short cx, cy;     // quadratic bezier control point
    public short cx1, cy1;   // cubic bezier control point
    public byte type;        // STBTT_vmove=1, vline=2, vcurve=3, vcubic=4
    public byte padding;
}
```

**Low-level rasterization:**
- `stbtt_Rasterize(bitmap, flatness, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, x_off, y_off, invert, userdata, useOldRasterizer)` — rasterizes raw vertices to a bitmap

**Vertex type constants:**
- `STBTT_vmove = 1` — contour start (move to)
- `STBTT_vline = 2` — straight line segment
- `STBTT_vcurve = 3` — quadratic bezier curve
- `STBTT_vcubic = 4` — cubic bezier curve

### FontStashSharp Precedent

FontStashSharp (the most popular StbTrueTypeSharp consumer) has **no synthetic bold/italic** support. This would be novel work in the StbTrueTypeSharp ecosystem.

## Implementation Plan

### Synthetic Italic (Shear Transform)

Apply horizontal shear to all vertex positions before rasterization:

```
x' = x + y * shear_factor
```

**Shear factor**: FreeType uses `FT_GlyphSlot_Oblique` which applies a 12° slant. `tan(12°) ≈ 0.2126`. GDI uses `tan(20°) ≈ 0.364`.

For FreeType parity, use **0.2126**. The shear is applied in font units (before scaling), so it works at any size.

**Steps:**
1. `stbtt_GetCodepointShape` → get vertex array
2. For each vertex: `v.x += (short)(v.y * 0.2126f)`, also shear control points `v.cx`, `v.cx1`
3. Recalculate bounding box (sheared glyph is wider)
4. `stbtt_Rasterize` with the modified vertices
5. `stbtt_FreeShape` to clean up
6. Update advance width and bearing to account for shear

### Synthetic Bold (Outline Expansion)

FreeType's `FT_Outline_Embolden` moves each outline point outward along its normal by a fixed amount. This is conceptually simple but computing per-vertex normals from bezier contours is non-trivial.

**Simpler approach**: Render the glyph at a slightly larger scale (or with a small offset), then composite. However, this doesn't match FreeType's behavior well.

**Recommended approach**: Use the same technique as FreeType — compute the direction perpendicular to the contour at each vertex and offset by `strength` pixels:

1. `stbtt_GetCodepointShape` → get vertex array
2. For each contour, compute outward normals at each point
3. Offset each point along its normal by `strength` (FreeType uses `ppem/24` in 26.6 fixed-point, so roughly `fontSize / 24` pixels)
4. Also offset control points for bezier curves
5. `stbtt_Rasterize` with modified vertices
6. Update metrics: advance += strength, bearingX -= strength/2, width += strength

**Alternative simpler approach**: Render the glyph normally, then apply a small dilation (max filter) on the bitmap. This is Phase 36's approach and is much simpler but lower quality. Could be used as a fallback if outline expansion proves too complex.

### Integration with Existing Guards

The core `BmFont.cs` (lines 149-158) already handles the bold/italic guard logic:
- Clears `options.Bold` when font is natively bold (unless `ForceSyntheticBold`)
- Clears `options.Italic` when font is natively italic (unless `ForceSyntheticItalic`)

The StbTrueType rasterizer just needs to:
1. Remove the `NotSupportedException` throws for Bold/Italic
2. When `options.Bold` is true: apply outline expansion before rasterizing
3. When `options.Italic` is true: apply shear transform before rasterizing
4. Both can be combined (shear + expand)

### Workflow Change

Current: `GetCodepointBitmap` → copy → return
New: `GetCodepointShape` → transform vertices → `Rasterize` → copy → `FreeShape` → return

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Outline normal computation is complex for bezier curves | High | Start with italic (trivial shear), bold can use simplified approach initially |
| Modified vertices may produce self-intersecting contours | Medium | Test with many fonts; stb_truetype's rasterizer handles some self-intersection |
| Metrics after bold expansion need careful adjustment | Medium | Compare against FreeType baseline metrics |
| Memory management with raw vertex pointers | Medium | Use try/finally with stbtt_FreeShape |
| Shear factor differs between FreeType and GDI | Low | Match FreeType (0.2126) since that's our primary comparison target |

## Success Criteria

- [ ] Synthetic italic works via outline shear transform
- [ ] Synthetic bold works via outline expansion (or fallback to bitmap dilation)
- [ ] Comparison images show StbTrueType bold/italic columns (not red)
- [ ] Metrics are within +/-2px of FreeType for the same font/size
- [ ] No memory leaks from vertex array handling
- [ ] SDF + bold/italic combination works
- [ ] Existing non-bold/italic tests still pass

## References

- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — outline API docs in header comments
- [StbTrueTypeSharp source](https://github.com/StbSharp/StbTrueTypeSharp) — C# port with full outline API
- [FreeType FT_Outline_Embolden](https://freetype.org/freetype2/docs/reference/ft2-outline_processing.html#ft_outline_embolden) — reference for bold strength calculation
- [FreeType FT_GlyphSlot_Oblique](https://freetype.org/freetype2/docs/reference/ft2-glyph_management.html#ft_glyphslot_oblique) — reference for italic shear (12° / 0.2126)
