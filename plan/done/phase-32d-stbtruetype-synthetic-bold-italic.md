# Phase 32d — StbTrueType Synthetic Bold & Italic

> **Status**: Complete
> **Created**: 2026-03-31
> **Updated**: 2026-04-01
> **Depends on**: Phase 32 (StbTrueType rasterizer), Phase 32c (validation fixes)
> **Related**: Phase 36 (bitmap-level bold/italic post-processing), Phase 110 (post-processing enhancements)

## Goal

Implement outline-level synthetic bold and italic for the StbTrueType rasterizer, matching FreeType's approach of transforming glyph outlines before rasterization. Add bitmap-level `BoldPostProcessor` and `ItalicPostProcessor` as fallback post-processors in the existing `IGlyphPostProcessor` pipeline.

## Answers to Key Design Questions

### Q1: Is synthetic bold/italic possible without SDF in StbTrueTypeSharp?

**Yes.** StbTrueTypeSharp 1.26.12 exposes the full outline API:

- `stbtt_GetCodepointShape(fontinfo, codepoint, &vertices)` → array of `stbtt_vertex` (bezier control points)
- `stbtt_Rasterize(bitmap, flatness, vertices, num_verts, scale_x, scale_y, ...)` → renders modified vertices to bitmap

The workflow is: extract vertices → transform them (shear for italic, embolden for bold) → rasterize the modified vertices. This operates at the outline level, producing the same quality as FreeType's synthetic transforms.

**Italic**: Apply affine shear `x' = x + y * 0.2126` to all vertex fields (x, cx, cx1). ~20 lines of code. Mathematically exact — bezier curves are preserved under affine transforms.

**Bold**: Port FreeType's `FT_Outline_EmboldenXY` algorithm — bisector normals, angle clamping, segment collapse prevention. ~150 lines. Approximation (shifts control points by bisector vector) but well-proven for small displacements.

### Q2: Non-SDF path should use outline-level transforms

**Confirmed.** When SDF is NOT enabled, bold/italic uses the outline-level transform path:
1. `stbtt_GetCodepointShape()` → get vertices
2. Apply embolden (if bold) then shear (if italic) — matching FreeType ordering
3. Compute bounding box from modified vertices
4. `stbtt_Rasterize()` → render to bitmap

This is the **primary** path and produces the highest quality output.

### Q3: Post-processor plugin architecture

The `IGlyphPostProcessor` interface already exists with four implementations (Gradient, Outline, Shadow, HeightStretch). This phase adds two more:

- **`BoldPostProcessor`** — morphological dilation on the bitmap (pixel-level thickening)
- **`ItalicPostProcessor`** — pixel-level shear transform with bilinear interpolation

These serve as:
- Fallback for SDF + bold/italic if we don't vendor the SDF function (Option 2)
- Post-processing for existing atlas PNGs (Phase 110 use case)
- Backend-agnostic bitmap-level styling for any rasterizer

The existing `IGlyphPostProcessor` pattern is already the "plugin-like" structure. Each processor takes a `RasterizedGlyph` and returns a modified one. They chain naturally.

### Q4: Best bold/italic strategy for StbTrueType users

**Tiered approach:**

| Scenario | Strategy | Quality |
|----------|----------|---------|
| Non-SDF bold/italic | Outline-level transform (this phase) | Highest — matches FreeType |
| SDF + bold/italic (with vendored SDF) | Outline transform → vendored `GetGlyphSdfFromVertices` | Highest — full SDF on modified outlines |
| SDF + bold/italic (without vendored SDF) | Bitmap post-processor fallback | Lower — dilation on distance fields is approximate |
| Post-processing existing atlases | `BoldPostProcessor` / `ItalicPostProcessor` | Acceptable — pixel-level transforms |

Users always get the best available quality automatically. The rasterizer uses outline-level when possible, falls back to bitmap post-processing only when the outline path is unavailable.

### Q5: Contributing upstream to StbTrueTypeSharp

**Side quest, not blocking.** Two potential contributions:

1. **StbTrueTypeSharp**: Add a `stbtt_GetGlyphSdfFromVertices()` overload that accepts pre-modified vertices instead of re-reading from font data. This is literally a one-line signature change to the existing `stbtt_GetGlyphSDF` function (~250 lines, Public Domain C code).

2. **stb_truetype.h (upstream C library)**: Propose the same change to Sean Barrett's repo. Less likely to be accepted (he's selective about API changes) but would fix the root cause for all consumers.

**Recommendation**: Implement Phase 32d with vendored SDF first. If it works well, create a PR to StbTrueTypeSharp as a follow-up. Don't block on upstream acceptance.

---

## Background

### The Problem

StbTrueType currently throws `NotSupportedException` for bold/italic (lines 73-76 of `StbTrueTypeRasterizer.cs`). This means:
- Comparison images show red cells for StbTrueType bold/italic columns
- WASM/AOT users (where StbTrueType is the only rasterizer) cannot produce styled text
- The feature gap is the biggest practical limitation of the StbTrueType backend

### How Other Backends Do It

| Backend | Bold | Italic |
|---------|------|--------|
| **FreeType** | `FT_Outline_Embolden(ppem/24)` — expands outlines outward | `FT_GlyphSlot_Oblique` — horizontal shear (0.2126 = tan 12°) |
| **GDI** | `LOGFONTW.LfWeight = FW_BOLD` — font mapper synthesizes | `MAT2` shear matrix (0.364 = tan 20°) |
| **DirectWrite** | `DWRITE_FONT_SIMULATIONS_BOLD` flag | `DWRITE_FONT_SIMULATIONS_OBLIQUE` flag |

### StbTrueTypeSharp Outline API (confirmed available)

```csharp
// Vertex structure
public struct stbtt_vertex
{
    public short x, y;       // vertex position
    public short cx, cy;     // quadratic bezier control point
    public short cx1, cy1;   // cubic bezier control point
    public byte type;        // vmove=1, vline=2, vcurve=3, vcubic=4
    public byte padding;
}

// Extract outline vertices
stbtt_GetCodepointShape(fontinfo, codepoint, &vertices) → stbtt_vertex[]
stbtt_GetGlyphShape(fontinfo, glyphIndex, &vertices) → stbtt_vertex[]
stbtt_FreeShape(fontinfo, vertices)

// Low-level rasterize from vertices
stbtt_Rasterize(bitmap, flatness, vertices, num_verts, scale_x, scale_y,
                shift_x, shift_y, x_off, y_off, invert, userdata, useOldRasterizer)
```

### FontStashSharp Precedent

FontStashSharp (the most popular StbTrueTypeSharp consumer) has **no** synthetic bold/italic. This is novel work in the StbTrueTypeSharp ecosystem.

---

## Implementation Plan

### Step 1: Italic Outline Transform (~20 lines)

Apply affine shear to all vertex fields before rasterization:

```csharp
const float ItalicShear = 0.2126f; // tan(12°), matches FreeType

static void ApplyItalicShear(stbtt_vertex* vertices, int numVerts)
{
    for (int i = 0; i < numVerts; i++)
    {
        vertices[i].x  += (short)(vertices[i].y  * ItalicShear);
        vertices[i].cx += (short)(vertices[i].cy * ItalicShear);
        vertices[i].cx1 += (short)(vertices[i].cy1 * ItalicShear);
    }
}
```

Key properties:
- Bezier curves preserved under affine transforms — no approximation needed
- Scale-independent (applied in font units before scaling)
- Advance width: do NOT modify (matches FreeType behavior)
- BearingX: recomputed from transformed bounding box
- Composite glyphs: `stbtt_GetGlyphShape` decomposes them — no special handling
- Empty glyphs (space): 0 vertices, loop does nothing
- `short` overflow: shear on y=2000 adds ~425 units, well within range

### Step 2: Bold Outline Transform (~150 lines)

Port FreeType's `FT_Outline_EmboldenXY` algorithm:

1. **Half the strength**: `xstr = strength / 2` (expand equally on both sides)
2. **Detect contour orientation** via signed area (shoelace formula)
   - Outer contours: clockwise → expand outward
   - Inner contours (holes): counter-clockwise → shrink inward
3. **For each point**: compute incoming/outgoing edge direction vectors
4. **Compute bisector shift**: `shift = (in_normal + out_normal) * strength / (1 + cos(angle))`
5. **Clamp at sharp angles** (>160°) to prevent spikes
6. **Clamp to prevent segment collapse**: thin features don't collapse into themselves
7. **Apply shift** to all points in a segment (on-curve AND control points)

Bold strength calculation (matching FreeType):
```csharp
float boldStrength = effectiveSize / 24.0f; // ppem/24, same as FreeType
```

### Step 3: Modified Rasterization Path

Current fast path (non-bold/italic): `stbtt_GetCodepointBitmap` → copy → return

New styled path (when bold or italic requested):

```
stbtt_GetCodepointShape()          // Get outline vertices
  ↓
ApplyEmbolden() if bold            // Expand outlines (Step 2)
  ↓
ApplyItalicShear() if italic       // Shear transform (Step 1)
  ↓
ComputeBoundingBox(vertices)       // Recompute bbox from modified vertices
  ↓
Allocate bitmap, pin with GCHandle
  ↓
stbtt_Rasterize(bitmap, ...)       // Render modified outlines
  ↓
Copy to managed array
  ↓
stbtt_FreeShape() in finally       // Clean up vertex memory
```

Bitmap setup for `stbtt_Rasterize`:
- Allocate `byte[width * height]` pixel buffer
- Pin with `GCHandle.Alloc(..., GCHandleType.Pinned)`
- Construct `stbtt__bitmap { w, h, stride, pixels }` (confirmed public in StbTrueTypeSharp)
- Parameters: `invert = 1`, `flatness_in_pixels = 0.35f`, `useOldRasterizer = false`
- Always `stbtt_FreeShape` in finally block

### Step 4: Metrics Correction

`GetGlyphMetrics()` must return correct values when bold/italic is active. Currently (lines 189-214) it uses `stbtt_GetCodepointBitmapBox` which reads the stored font bbox, not transformed vertices.

When bold or italic is requested:
1. Get vertices via `stbtt_GetCodepointShape`
2. Apply same transforms (embolden, shear)
3. Compute bounding box by iterating transformed vertices
4. Scale to pixel space and compute metrics from the actual transformed bbox

### Step 5: SDF + Bold/Italic (Vendored SDF Function)

Vendor `stbtt_GetGlyphSDF` (~250 lines, Public Domain) into our project with a modified signature:

```csharp
// Original (reads vertices internally):
byte* stbtt_GetGlyphSDF(stbtt_fontinfo info, float scale, int glyph, ...)

// Vendored (accepts pre-modified vertices):
byte[] GetGlyphSdfFromVertices(stbtt_fontinfo info, float scale,
    stbtt_vertex* vertices, int numVerts, ...)
```

Also vendor helper functions if not public:
- `stbtt__compute_crossings_x` (~50 lines)
- `stbtt__solve_cubic` (~50 lines)

Place vendored code in: `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeSdfVendored.cs`

### Step 6: Bitmap Post-Processors (Fallback)

Add two new `IGlyphPostProcessor` implementations:

**`BoldPostProcessor`** (`src/KernSmith/Rasterizer/BoldPostProcessor.cs`):
- Morphological dilation — expand non-zero pixels outward
- Configurable strength (pixels to expand)
- Works with Grayscale8 and Rgba32 formats
- Updates metrics (Width, Height, BearingX, BearingY) for expanded bitmap
- Uses distance-based alpha for anti-aliased edges (similar to OutlinePostProcessor's EDT approach)

**`ItalicPostProcessor`** (`src/KernSmith/Rasterizer/ItalicPostProcessor.cs`):
- Pixel-level shear transform with bilinear interpolation
- Configurable shear factor (default 0.2126 to match FreeType)
- Works with Grayscale8 and Rgba32 formats
- Updates metrics for sheared bounding box
- Similar approach to HeightStretchPostProcessor's bilinear interpolation

These are lower quality than outline-level transforms but serve as:
- Fallback when outline path is unavailable
- Post-processing for existing atlas PNGs (Phase 110)
- Backend-agnostic styling for any rasterizer

### Step 7: Update Capabilities and Remove Exceptions

In `StbTrueTypeCapabilities`:
```csharp
public bool SupportsSyntheticBold => true;
public bool SupportsSyntheticItalic => true;
```

In `StbTrueTypeRasterizer.RasterizeGlyph()`:
- Remove the two `throw new NotSupportedException(...)` lines (lines 73-76)
- Route to the styled path when `options.Bold` or `options.Italic` is true

---

## Integration with Existing Pipeline

### BmFont.cs Guards (already implemented, lines 149-158)

```csharp
// Don't apply synthetic on already-styled fonts unless ForceSynthetic
if (fontInfo.IsBold && effectiveBold && !effectiveForceSyntheticBold)
    effectiveBold = false;
if (fontInfo.IsItalic && effectiveItalic && !effectiveForceSyntheticItalic)
    effectiveItalic = false;
```

No changes needed here — the StbTrueType rasterizer just needs to honor `options.Bold` / `options.Italic`.

### Bold + Italic Ordering

FreeType applies embolden first, then shear. We match this ordering for visual parity.

### GDI Rasterizer Bug (separate fix)

`src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:361` — XML comment says `0.2126` (12°) but code uses `0.364` (20°). The code is correct for GDI; the comment is wrong. Fix the comment separately.

---

## File Changes Summary

| File | Change |
|------|--------|
| `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs` | Remove NotSupportedException, add styled rasterization path, update GetGlyphMetrics |
| `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeCapabilities.cs` | Set SupportsSyntheticBold/Italic = true |
| `src/KernSmith.Rasterizers.StbTrueType/OutlineTransforms.cs` | NEW — italic shear + bold embolden algorithms |
| `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeSdfVendored.cs` | NEW — vendored SDF function accepting pre-modified vertices |
| `src/KernSmith/Rasterizer/BoldPostProcessor.cs` | NEW — bitmap-level bold via morphological dilation |
| `src/KernSmith/Rasterizer/ItalicPostProcessor.cs` | NEW — bitmap-level italic via pixel shear |
| `tests/KernSmith.Tests/` | Tests for italic shear, bold embolden, SDF+styled, post-processors, metrics accuracy |

---

## Implementation Order

| Order | Task | Effort | Risk |
|-------|------|--------|------|
| 1 | Italic outline transform + rasterization path | Small (1 step) | Low — affine math is exact |
| 2 | Bounding box recomputation from transformed vertices | Small (1 step) | Low |
| 3 | Metrics correction in GetGlyphMetrics | Small (1 step) | Medium — must match FreeType within ±2px |
| 4 | Bold outline transform (FreeType embolden port) | Large (multi-step) | Medium — bisector math needs careful testing |
| 5 | Remove NotSupportedException, update capabilities | Trivial | Low |
| 6 | Vendored SDF function for SDF+styled path | Medium | Medium — need to verify helpers are accessible |
| 7 | BoldPostProcessor (bitmap fallback) | Medium | Low — similar to existing OutlinePostProcessor |
| 8 | ItalicPostProcessor (bitmap fallback) | Small | Low — similar to HeightStretchPostProcessor |
| 9 | Integration tests, comparison images | Medium | Low |

**Recommended approach**: Ship italic first (Steps 1-3, 5) since it's low-risk and immediately useful. Then bold (Step 4). Then SDF vendoring (Step 6). Then post-processors (Steps 7-8).

---

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| SDF + bold/italic impossible through public API | High | Vendor `stbtt_GetGlyphSDF` (~250 lines, Public Domain) |
| `GetGlyphMetrics` returns wrong values for styled glyphs | High | Apply same transforms in metrics path, compare against FreeType |
| `stbtt_Rasterize` bitmap setup undocumented | High | Manual: allocate, pin with GCHandle, construct stbtt__bitmap |
| Bold algorithm complexity | Medium | Port FreeType's proven algorithm; test against many fonts |
| Modified vertices may self-intersect | Medium | stb_truetype's rasterizer handles this via fill rules |
| Vendored SDF code maintenance | Low | Upstream is stable; ~250 lines of Public Domain code |

## Success Criteria

- [ ] Synthetic italic works via outline shear transform
- [ ] Synthetic bold works via outline expansion
- [ ] SDF + bold/italic works via vendored SDF function
- [ ] Comparison images show StbTrueType bold/italic columns (not red)
- [ ] Metrics within ±2px of FreeType for same font/size
- [ ] `GetGlyphMetrics` returns correct values when bold/italic active
- [ ] No memory leaks from vertex array handling
- [ ] Bold + italic combined correct (embolden first, then shear)
- [ ] BoldPostProcessor and ItalicPostProcessor work as bitmap fallbacks
- [ ] Existing non-bold/italic tests still pass
- [ ] Non-styled glyphs still use fast path (no performance regression)

## Side Quest: Upstream Contribution

After Phase 32d ships and the vendored SDF approach is proven:

1. **Clone StbTrueTypeSharp** (`https://github.com/StbSharp/StbTrueTypeSharp`)
2. Add `stbtt_GetGlyphSdfFromVertices()` overload that accepts pre-modified vertices
3. Create PR with motivation: "Enable SDF rendering of synthetically modified glyphs"
4. If accepted, replace vendored code with upstream call in a future phase
5. Optionally propose the same to `nothings/stb` (the upstream C library)

This is a nice-to-have follow-up, not a blocker for Phase 32d.

## References

- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — outline API docs
- [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp) — C# port
- [FreeType ftoutln.c](https://gitlab.freedesktop.org/freetype/freetype/-/blob/master/src/base/ftoutln.c) — FT_Outline_EmboldenXY (~150 lines)
- [FreeType ftsynth.c](https://gitlab.freedesktop.org/freetype/freetype/-/blob/master/src/base/ftsynth.c) — FT_GlyphSlot_Oblique
