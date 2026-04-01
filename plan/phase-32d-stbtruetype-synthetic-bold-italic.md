# Phase 32d — StbTrueType Synthetic Bold & Italic

> **Status**: Planning (research complete, decision needed on SDF + bold/italic)
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
| **GDI** | `LOGFONTW.LfWeight = FW_BOLD` — font mapper selects/synthesizes bold | `MAT2` shear matrix (0.364 = tan(20deg)) applied to `GetGlyphOutlineW` |
| **DirectWrite** | `DWRITE_FONT_SIMULATIONS_BOLD` flag on font face | `DWRITE_FONT_SIMULATIONS_OBLIQUE` flag on font face |

### Research: StbTrueTypeSharp Outline API

StbTrueTypeSharp (1.26.12) exposes the full outline API from stb_truetype:

**Outline extraction:**
- `stbtt_GetCodepointShape(fontinfo, codepoint, &vertices)` returns array of `stbtt_vertex` (bezier control points)
- `stbtt_GetGlyphShape(fontinfo, glyphIndex, &vertices)` same, by glyph index
- `stbtt_FreeShape(fontinfo, vertices)` frees the vertex array

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
- `stbtt_Rasterize(bitmap, flatness, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, x_off, y_off, invert, userdata, useOldRasterizer)` rasterizes raw vertices to a bitmap

**Vertex type constants:**
- `STBTT_vmove = 1` contour start (move to)
- `STBTT_vline = 2` straight line segment
- `STBTT_vcurve = 3` quadratic bezier curve
- `STBTT_vcubic = 4` cubic bezier curve

### FontStashSharp Precedent

FontStashSharp (the most popular StbTrueTypeSharp consumer) has **no synthetic bold/italic** support. This would be novel work in the StbTrueTypeSharp ecosystem.

## Research Findings

Three validation agents investigated the italic transform, bold algorithm, and integration issues in detail. Their combined findings follow.

### Italic: Affine Shear Transform

The italic transform is a simple affine shear applied to all vertex fields:

```
x'   = x   + y   * 0.2126
cx'  = cx  + cy  * 0.2126
cx1' = cx1 + cy1 * 0.2126
```

Key properties:
- **Mathematically exact**: Bezier curves are preserved under affine transforms. No approximation, no curve subdivision needed.
- **Shear factor**: FreeType uses `tan(12deg) = 0.2126`. GDI uses `tan(20deg) = 0.364`. We match FreeType.
- **Coordinate system**: stb_truetype outlines use y-up with baseline at y=0. Shearing from y=0 is correct: ascenders shift right, descenders shift left.
- **Scale-independent**: The same shear factor works at all sizes because it is applied in font units before scaling.
- **Advance width**: Do NOT modify. FreeType does not modify advance width for italic either.
- **BearingX**: Recomputed naturally from the new bitmap bounding box.
- **Bounding box**: MUST be recomputed from modified vertices. Cannot use `stbtt_GetGlyphBitmapBoxSubpixel` because it reads the stored font bbox, not the actual transformed vertices.
- **Composite glyphs**: `stbtt_GetGlyphShape` fully decomposes composite glyphs into simple vertices. No special handling needed.
- **Empty glyphs** (e.g. space): 0 vertices returned, the loop does nothing. No special handling needed.
- **`short` precision**: Shear on a y=2000 vertex adds ~425 units, well within `short` range. Integer truncation is acceptable.

**Estimated effort**: ~20 lines of transform code plus bounding box recomputation.

### Bold: FreeType's FT_Outline_EmboldenXY Algorithm

The FreeType bold algorithm is ~150 lines of carefully tuned code. The key steps are:

1. **Half the strength**: `xstr = strength / 2` (expand equally on both sides)
2. **Detect contour orientation** via signed area (shoelace formula). In TrueType fonts, outer contours are clockwise, inner contours (holes) are counter-clockwise.
3. **For each point**, compute incoming and outgoing edge direction vectors.
4. **Compute bisector shift**: `shift = (in_normal + out_normal) * strength / (1 + cos(angle))` where the normals are perpendicular to the incoming/outgoing edges.
5. **Clamp at sharp angles** (>160deg) to prevent spikes where edges meet at extreme angles.
6. **Clamp to prevent segment collapse**: `if (strength * q > l * d)` limits the shift so that thin features (like the crossbar of an 'e') don't collapse into themselves.
7. **Apply shift to ALL points** in a segment, including on-curve points AND control points. The same bisector vector is applied to every point in the segment.

**Key insight**: FreeType does NOT compute true curve offsets (offset curves of bezier curves are not themselves bezier curves). Instead it shifts all control points by the same bisector vector. This is an approximation that works well for the small displacements typical of emboldening.

**Contour direction**: The signed area check automatically causes outer contours to expand outward and inner contours (holes) to shrink inward. No manual winding-direction annotation needed.

**Self-intersection**: FreeType does not handle self-intersection explicitly. It relies on the rasterizer's fill rules to produce correct output even if expanded contours cross.

**Estimated effort**: Multi-day. The bisector normal computation, angle clamping, and segment collapse prevention require careful porting and testing.

### Bold + Italic Ordering

FreeType applies embolden first, then shear. We should match this ordering to ensure visual parity.

### GDI Rasterizer Bug Found

The GDI rasterizer has a misleading doc comment at `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:361`. The XML comment on `GetMat2` says:

> Uses a 0.2126 horizontal shear (~12deg) to match FreeType's FT_GlyphSlot_Oblique.

But the actual code on line 370 uses `0.364 * 65536 = 23855`, which is GDI's native 20-degree shear. The comment is wrong; the code is correct for GDI. This should be fixed separately (the GDI backend should use GDI's shear, not FreeType's).

## SDF Limitation

### The Problem

`stbtt_GetCodepointSDF` and `stbtt_GetGlyphSDF` internally re-read the glyph outline from font data. There is NO `stbtt_RasterizeSDF(vertices, ...)` entry point that accepts pre-modified vertices. This is a limitation in the **original C stb_truetype.h library**, not just the C# port.

This means SDF + bold or SDF + italic cannot be achieved through the public StbTrueTypeSharp API. The SDF function is approximately 250 lines inline in StbTrueTypeSharp, calling two helpers (`stbtt__compute_crossings_x` and `stbtt__solve_cubic`).

### Options

**Option 1 — Vendor one function (recommended)**

Copy `stbtt_GetGlyphSDF` (~250 lines, Public Domain license) into our project and change the entry point to accept vertices as a parameter instead of re-reading them from font data. Literally a one-line signature change:

```csharp
// Original: gets its own vertices internally
byte* stbtt_GetGlyphSDF(fontinfo, scale, glyph, ...)

// Ours: accepts pre-modified vertices
byte* GetGlyphSdfFromVertices(fontinfo, scale, vertices, num_verts, ...)
```

The two helper functions may already be public in StbTrueTypeSharp; if not, copy those too (~50 lines each).

Pros: Clean, self-contained, no external dependency changes, Public Domain allows it.
Cons: ~250-350 lines of vendored code to maintain if StbTrueTypeSharp updates their SDF implementation.

**Option 2 — Descope SDF + bold/italic**

Do not support the combination on the StbTrueType backend. Users who need SDF + bold/italic use FreeType.

Pros: Zero extra code, ship faster.
Cons: Feature gap. SDF is one of StbTrueType's advertised capabilities; not being able to combine it with bold/italic is surprising.

**Option 3 — Bitmap-level fallback for SDF path**

Use outline modification for normal rendering but fall back to bitmap-level post-processing (Phase 36) when SDF is requested.

Pros: No vendored code, leverages Phase 36 work.
Cons: Bitmap dilation on distance field values is semantically wrong. Distances do not average or dilate like pixel intensities. Distance-field-aware dilation is a separate research problem.

**Option 4 — Contribute upstream**

Propose a vertex-accepting SDF function to stb_truetype (Sean Barrett's project).

Pros: Fixes the root cause for everyone.
Cons: Uncertain timeline. Sean Barrett is selective about API changes. Does not solve our problem now.

**Option 5 — Fork StbTrueTypeSharp**

Maintain our own fork with the added function.

Pros: Full control.
Cons: Maintenance burden, NuGet dependency changes, overkill for one function.

### Recommendation

Option 1 (vendor one function) for the best quality with minimal risk. Fall back to Option 2 (descope) if the vendored code is not worth maintaining. Option 3 sounds appealing but bitmap dilation on SDF values is fundamentally the wrong math.

## Alternative: Skip Synthetic Bold/Italic Entirely

An alternative to all of this work: only support bold/italic when the font file itself has native bold/italic faces. The StbTrueType comparison image columns stay red for synthetic — that is the documented feature gap. Users who need synthetic bold/italic use FreeType.

This is a valid architectural choice. stb_truetype is a lightweight fallback for WASM/AOT platforms, not a full-featured replacement for FreeType. FontStashSharp (the most popular StbTrueTypeSharp consumer) also has no synthetic bold/italic.

**Pros:**
- Zero implementation effort
- No vendored SDF code
- No complex bold algorithm to port and maintain
- Clearly documented capability gap between backends

**Cons:**
- WASM/AOT users have no path to bold/italic at all
- The feature gap remains the biggest practical limitation of the StbTrueType backend

**Decision needed**: This option should be discussed before committing to the full implementation.

## Implementation Plan

### Workflow Change

Current (non-bold/italic): `stbtt_GetCodepointBitmap` fast path, copy, return.

New (bold/italic requested): `stbtt_GetCodepointShape` -> transform vertices (embolden then shear) -> compute bounding box from vertices -> allocate bitmap manually -> `stbtt_Rasterize` -> copy -> `stbtt_FreeShape` -> return.

### Bitmap Setup for stbtt_Rasterize

The `stbtt_Rasterize` path requires manual bitmap setup:
- Allocate pixel buffer
- Pin it with `GCHandle`
- Construct `stbtt__bitmap { w, h, stride, pixels }` struct (confirmed public in StbTrueTypeSharp)
- Parameters: `invert` likely 1, `flatness_in_pixels` 0.35, `useOldRasterizer` false

### Performance

Keep the `stbtt_GetCodepointBitmap` fast path for non-bold/italic glyphs. Only switch to the shape-modify-rasterize path when bold or italic is actually requested.

### Integration with Existing Guards

The core `BmFont.cs` (lines 149-158) already handles the bold/italic guard logic:
- Clears `options.Bold` when font is natively bold (unless `ForceSyntheticBold`)
- Clears `options.Italic` when font is natively italic (unless `ForceSyntheticItalic`)

The StbTrueType rasterizer needs to:
1. Remove the `NotSupportedException` throws for Bold/Italic
2. When `options.Bold` is true: apply outline expansion before rasterizing
3. When `options.Italic` is true: apply shear transform before rasterizing
4. When both: embolden first, then shear (matching FreeType ordering)

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| SDF + bold/italic impossible through public API | High | Vendor `stbtt_GetGlyphSDF` (~250 lines, Public Domain) or descope the combination |
| `GetGlyphMetrics` returns unmodified metrics when bold/italic is active | High | Apply the same transforms in `GetGlyphMetrics`: recompute bbox for italic, adjust advance/bearing for bold |
| `stbtt_Rasterize` bitmap setup is undocumented | High | Manual: allocate buffer, pin with GCHandle, construct `stbtt__bitmap`, use `invert=1`, `flatness=0.35`, `useOldRasterizer=false` |
| Bold algorithm complexity underestimated | Medium | FreeType's algorithm is ~150 lines with bisector normals, angle clamping, and collapse prevention. Multi-day effort. Consider Phase 36 bitmap fallback initially. |
| Bold + italic ordering affects output | Medium | Match FreeType: embolden first, shear second |
| Modified vertices may produce self-intersecting contours | Medium | Test with many fonts; stb_truetype's rasterizer handles some self-intersection via fill rules |
| Metrics after bold expansion need careful adjustment | Medium | Compare against FreeType baseline metrics |
| Memory management with raw vertex pointers | Medium | Use try/finally with `stbtt_FreeShape` |
| Shear factor differs between FreeType and GDI | Low | Match FreeType (0.2126) since that is our primary comparison target |

## Success Criteria

- [ ] Synthetic italic works via outline shear transform
- [ ] Synthetic bold works via outline expansion (or fallback to bitmap dilation as interim)
- [ ] Comparison images show StbTrueType bold/italic columns (not red)
- [ ] Metrics are within +/-2px of FreeType for the same font/size
- [ ] `GetGlyphMetrics` returns correct values when bold/italic is active
- [ ] No memory leaks from vertex array handling
- [ ] SDF + bold/italic works if Option 1 (vendored SDF) is chosen; otherwise documented as unsupported
- [ ] Existing non-bold/italic tests still pass
- [ ] Bold + italic combined produces correct output (embolden first, then shear)

## References

- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — outline API docs in header comments
- [StbTrueTypeSharp source](https://github.com/StbSharp/StbTrueTypeSharp) — C# port with full outline API
- [FreeType FT_Outline_Embolden](https://freetype.org/freetype2/docs/reference/ft2-outline_processing.html#ft_outline_embolden) — reference for bold strength calculation
- [FreeType FT_GlyphSlot_Oblique](https://freetype.org/freetype2/docs/reference/ft2-glyph_management.html#ft_glyphslot_oblique) — reference for italic shear (12deg / 0.2126)
- [FreeType ftsynth.c](https://gitlab.freedesktop.org/freetype/freetype/-/blob/master/src/base/ftsynth.c) — FT_GlyphSlot_Oblique implementation
- [FreeType ftoutln.c](https://gitlab.freedesktop.org/freetype/freetype/-/blob/master/src/base/ftoutln.c) — FT_Outline_EmboldenXY implementation (~150 lines)
- GDI rasterizer bug: `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:361` — doc comment claims 12deg but code uses 20deg
