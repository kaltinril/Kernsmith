# bmfontier -- FT_Stroker Compositing Fix

> The FT_Stroker vector outline path is implemented but disabled due to
> compositing issues with post-processor effects (gradient, shadow).
> The EDT-based OutlinePostProcessor is the active fallback.
>
> **Date**: 2026-03-19

---

## Problem

The FT_Stroker path in `BmFont.cs` (`CompositeWithFtStroker`) re-rasterizes the glyph from the font to composite it on top of the stroked outline. This discards any post-processor effects (gradient colors, shadow) that were applied to the glyph before the stroker runs.

**Pipeline order when FT_Stroker is enabled:**
1. Rasterize glyph (grayscale)
2. Post-processors run (gradient → RGBA with colors)
3. OutlinePostProcessor is SKIPPED (line 151-152)
4. `CompositeWithFtStroker` calls `RasterizeOutline()` → gets fresh outline bitmap
5. Composites the gradient-colored glyph on top of the outline

**What goes wrong:** Step 5 should work — it reads from the already-processed glyph. But the result showed glyphs overlapping and cut off in the atlas, suggesting either:
- The outline bitmap metrics don't match the glyph metrics (offset calculation wrong)
- The FT_Stroker is silently failing for some glyphs (the `catch` on line 581-585 swallows all errors and returns the original unoutlined glyph)
- Mixed outlined + non-outlined glyphs in the same atlas cause size/position inconsistencies

---

## Root Cause Analysis Needed

The following needs investigation:

### Issue 1: Silent failure swallowing
```csharp
catch
{
    // FT_Stroker can fail for certain glyph types; fall back gracefully.
    return glyph;
}
```
This catch-all means some glyphs get outlined and others don't, with no indication. The atlas ends up with mixed-size glyphs — some expanded by the outline, some at original size — causing overlap.

**Fix:** Log which glyphs fail. Consider falling back to EDT for failed glyphs rather than returning the unoutlined glyph.

### Issue 2: Offset calculation assumes matching coordinate systems
```csharp
var offsetX = outlineGlyph.Metrics.BearingX - glyph.Metrics.BearingX;
var offsetY = glyph.Metrics.BearingY - outlineGlyph.Metrics.BearingY;
```
If the stroked glyph has different BearingX/BearingY than expected (e.g., FT_Stroker changes the bearing differently than OutlinePostProcessor's `BearingX - outlineWidth`), the glyph body won't align with the outline.

**Fix:** Verify bearing values for several glyphs. Print/log original vs outline metrics.

### Issue 3: Advance not adjusted for outline expansion
```csharp
Advance: F26Dot6ToRounded(slot->metrics.horiAdvance),
```
The advance comes from the original glyph slot, not adjusted for the outline. While this doesn't affect atlas packing (packing uses bitmap Width), it affects the `.fnt` output's `xadvance` field, which controls character spacing at render time.

**Fix:** Add `2 * outlineWidth` to the advance, or use the stroked glyph's advance if FreeType provides one.

### Issue 4: FT_Stroker may not work for all glyph types
- Composite glyphs (glyphs referencing other glyphs) may need special handling
- Bitmap-only fonts (no outlines) will fail on `FT_Load_Glyph` with `FT_LOAD_NO_BITMAP`
- Bold/italic embolden/oblique is applied to the slot but the stroker operates on the glyph copy — verify the transforms are applied before `FT_Get_Glyph`

---

## Current State

| Component | Status |
|-----------|--------|
| FT_Stroker P/Invoke bindings | Done (`FreeTypeNative.cs`) |
| `FreeTypeRasterizer.RasterizeOutline()` | Done but untested with real rendering |
| `CompositeWithFtStroker()` | Done but offset/metrics issues |
| Pipeline integration | Done but disabled (`useFtStroker = false`) |
| EDT-based OutlinePostProcessor | Active, working correctly with all effects |

---

## Task Breakdown

| # | Task | Effort |
|---|------|--------|
| 1 | Add diagnostic logging to `CompositeWithFtStroker` — print metrics for outline vs glyph | Small |
| 2 | Test `RasterizeOutline` in isolation — render a single glyph and inspect the bitmap | Small |
| 3 | Fix silent catch — fall back to EDT per-glyph instead of returning unoutlined glyph | Medium |
| 4 | Verify offset calculation with real FT_Stroker output metrics | Medium |
| 5 | Fix advance adjustment for outlined glyphs | Small |
| 6 | Test with composite glyphs, bitmap fonts, bold/italic | Medium |
| 7 | Add integration test that compares FT_Stroker vs EDT output dimensions | Medium |
| 8 | Re-enable FT_Stroker path with proper feature flag | Small |

---

## When to Re-enable

Re-enable `useFtStroker` when:
1. Tasks 1-5 are complete
2. FT_Stroker output matches EDT output for at least ASCII glyphs
3. Gradient + outline combo produces correct results
4. No silent failures for standard Latin fonts

The EDT path is a solid production-quality fallback, so there's no urgency. FT_Stroker is a quality improvement (vector-precise curves) but not a requirement.

---

## Files

| File | Role |
|------|------|
| `src/Bmfontier/BmFont.cs:144` | `useFtStroker = false` — the disable line |
| `src/Bmfontier/BmFont.cs:548-646` | `CompositeWithFtStroker()` — compositing logic |
| `src/Bmfontier/Rasterizer/FreeTypeRasterizer.cs:214-341` | `RasterizeOutline()` — stroker rasterization |
| `src/Bmfontier/Rasterizer/FreeTypeNative.cs` | P/Invoke bindings for FT_Stroker |
