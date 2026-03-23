# Phase 76B — Outline Rendering and Italic Tilt Fixes

> **Status**: Done
> **Created**: 2026-03-22
> **Goal**: Fix EDT outline counter-fill bug and italic tilt differences vs BMFont.

---

## Problem 1: Outline Fills Letter Counters

At larger sizes with thick outlines (e.g., size 48+ with outline thickness 4, especially with bold), the inner holes of letters like 'e', 'o', 'a' fill in. The outline expands inward into counters instead of only outward.

### Root Cause

The current outline renderer (`OutlineEffect.cs`) uses Euclidean Distance Transform (EDT). The EDT treats counter (hole) regions as "outside the glyph" and expands the outline bidirectionally — both outward AND inward along counter boundaries.

Key code in `OutlineEffect.cs` lines 53-70:
```csharp
var dist = MathF.Sqrt(squaredDist[y * dstW + x]);
var outlineAlpha = Math.Clamp(255f * (ow - dist + 0.5f), 0f, 255f);
```

In `EuclideanDistanceTransform.cs` lines 19-26:
```csharp
// Initialize: inside pixels = 0, outside pixels = infinity.
for (var i = 0; i < size; i++)
    grid[i] = alphaData[i] > 0 ? 0f : Infinity;
```

Zero-alpha counter pixels are treated identically to zero-alpha exterior pixels. The EDT computes distance to the nearest non-zero pixel, which for counter pixels means distance to the counter's inner edge — and outline alpha spreads `outlineWidth` pixels from that edge inward.

### Existing FT_Stroker Path (Disabled)

A correct FT_Stroker-based outline path already exists but is disabled:

- **`FreeTypeRasterizer.RasterizeOutline()`** (lines 317-448) — uses `FT_Glyph_StrokeBorder(ref glyph, stroker, false, true)` with `inside=false`, which strokes ONLY the outer contour. This produces correct outlines that never fill counters.
- **`BmFont.CompositeWithFtStroker()`** (lines 1054-1153) — composites the FT_Stroker outline with the body glyph. Currently disabled.
- **Disabled at**: the effects pipeline always uses `OutlineEffect` (EDT-based) instead.

### Why FT_Stroker Was Disabled (Phase 12 Track D)

| Issue | Details |
|-------|---------|
| D1 Silent failure | Bare catch returns unoutlined glyph with no indication. Mixed outlined/non-outlined glyphs cause atlas overlap. |
| D2 Offset mismatch | offsetX/offsetY calculation assumes matching coordinate systems between FT_Stroker and EDT metrics. |
| D3 Advance not adjusted | horiAdvance from original glyph, not adjusted for outline expansion. |
| D4 Glyph type limits | Composite glyphs, bitmap-only fonts, bold/italic may need special handling. |

### Re-enable Criteria (from Phase 12)

1. Tasks D1-D5 complete
2. FT_Stroker output matches EDT output for ASCII glyphs (except counters)
3. Gradient + outline combo produces correct results
4. No silent failures for standard Latin fonts

### P/Invoke Bindings (Already Complete)

Full FT_Stroker API in `FreeTypeNative.cs` (lines 34-88):
- `FT_Stroker_New`, `FT_Stroker_Set`, `FT_Stroker_Done`
- `FT_Get_Glyph`, `FT_Glyph_StrokeBorder`, `FT_Glyph_To_Bitmap`, `FT_Done_Glyph`
- `ReadBitmapGlyph` helper (lines 102-134)

### Fix Options

**Option A: Fix EDT to respect counters**
- Modify EDT to distinguish "outside glyph" from "inside counter"
- Would need a flood-fill from the image edges to mark true exterior vs counter regions
- EDT then only expands outline into true exterior pixels
- Pro: No FT_Stroker dependency. Con: More complex, may have edge cases.

**Option B: Re-enable and fix FT_Stroker path**
- Fix D1: Replace bare catch with per-glyph EDT fallback + warning log
- Fix D2: Verify offset calculation with real FT_Stroker output
- Fix D3: Adjust advance for outline expansion (already done in OutlinePostProcessor — reuse same logic)
- Fix D4: Test with composite glyphs, bitmap fonts, bold/italic
- Pro: Geometrically precise, vector-based. Con: Need to fix compositing integration.

**Option C: Hybrid — use FT_Stroker for outline generation, EDT for anti-aliasing**
- Generate the outline shape with FT_Stroker (correct topology)
- Apply EDT-based anti-aliasing on the result
- Pro: Best of both. Con: Most complex.

---

## Problem 2: Italic Tilt Difference

Synthetic italic rendering differs visually between BMFont and KernSmith.

### Root Cause

Both tools use a 12-degree shear for synthetic italic:
- **BMFont**: GDI `CreateFont()` with `lfItalic=TRUE` -> Windows applies 12 degree shear
- **KernSmith**: `FT_GlyphSlot_Oblique()` -> FreeType applies 12 degree shear (tan(12) ~ 0.2126)

The visual difference comes from:
1. **Different hinting engines** — GDI grid-fits differently than FreeType at the same angle
2. **Different rasterizers** — GDI ClearType vs FreeType grayscale/light AA
3. **Font's post.italicAngle ignored** — KernSmith always uses 12 degrees regardless of font's specified angle

### Current Code

Synthetic italic applied in `FreeTypeRasterizer.cs` at three call sites (lines 204-207, 346-349, 519-522):
```csharp
if (options.Italic && (_face->style_flags & 0x02) == 0)
{
    FT.FT_GlyphSlot_Oblique(slot);
}
```

- Correctly checks if font already has native italic flag before applying synthetic
- Correctly tries to load italic font variant first (`BmFont.cs` lines 489-496)
- No configurable angle parameter — hardcoded 12 degrees from FreeType

### Fix Options

**Option A: Read post.italicAngle from font**
- Parse the `post` table's `italicAngle` field (already referenced in `reference/REF-03-ttf-font-reference.md`)
- Apply the font's specified angle instead of FreeType's fixed 12 degrees
- Use `FT_Set_Transform` with a custom shear matrix instead of `FT_GlyphSlot_Oblique`

**Option B: Add configurable italic angle**
- Add `ItalicAngle` property to `FontGeneratorOptions` (default: 12.0)
- Apply via `FT_Set_Transform` with `tan(angle)` shear

**Option C: Accept the difference**
- The 12 degree angle matches BMFont's GDI angle
- Visual differences are from hinting/rasterization, not the angle itself
- Document as known limitation of FreeType vs GDI rendering

---

## Key Source Files

| What | Location |
|------|----------|
| EDT outline effect | `src/KernSmith/Rasterizer/OutlineEffect.cs` |
| EDT algorithm | `src/KernSmith/Atlas/EuclideanDistanceTransform.cs` |
| FT_Stroker rasterizer | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` (lines 317-448) |
| FT_Stroker compositor | `src/KernSmith/BmFont.cs` (lines 1054-1153) |
| FT_Stroker P/Invoke | `src/KernSmith/Rasterizer/FreeTypeNative.cs` (lines 34-88) |
| Italic application | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` (lines 204-207) |
| Phase 12 Track D | `plan/done/phase-12-pre-ship-polish.md` (lines 105-150) |
| Phase 9 outline overhaul | `plan/done/phase-09-outline-overhaul.md` |

---

## Decisions

### Problem 1: Fixed with Option A — Flood-Fill EDT Fix

The OutlineEffect uses a BFS flood-fill from image edges through zero-alpha pixels to distinguish true exterior pixels from counter (hole) pixels. Counter pixels (zero-alpha but unreachable from edges) are excluded from outline rendering. Applied in OutlineEffect.cs between EDT computation and RGBA output generation. This prevents letter counters from filling in at large sizes with thick outlines.

### Problem 2: Accepted Option C — Document as Known Limitation

The 12-degree synthetic italic angle already matches BMFont's GDI angle. The remaining visual differences are inherent to FreeType vs GDI rasterization — different hinting engines (FreeType auto-hinter vs GDI grid-fitting) and different anti-aliasing modes (grayscale AA vs ClearType). Documented as a known limitation; no code change required.

### Bonus: Synthetic Bold Counter Bloat Fix

FreeType's `FT_GlyphSlot_Embolden` uses strength `ppem/24` which is too aggressive at larger sizes — fills letter counters in heavy fonts like Bauhaus 93. Replaced with custom `EmboldenGlyph` method using `FT_Outline_Embolden` at `ppem/36` strength (minimum 0.5px) to better approximate GDI's lighter synthetic bold. Metric adjustments after emboldening: width += 2*strength, height += 2*strength, horiBearingY += strength, horiAdvance += strength, vertAdvance += strength. horiBearingX is NOT adjusted.
