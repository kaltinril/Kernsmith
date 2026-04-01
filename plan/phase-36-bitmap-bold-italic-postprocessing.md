# Phase 36 — Bitmap-Level Bold & Italic Post-Processing

> **Status**: Planning
> **Created**: 2026-03-31
> **Depends on**: Phase 32d (outline-level synthetic bold/italic)
> **Related**: Phase 32 (StbTrueType rasterizer), Phase 78G (synthetic bold/italic guards)

## Goal

Add optional bitmap-level bold and italic post-processing effects that work with ANY rasterizer backend, applied after rasterization. This enables stacking effects (e.g., extra-bold, double-italic) and works even when the rasterizer has no outline-level synthesis.

## Motivation

Phase 32d adds outline-level synthetic bold/italic to StbTrueType, matching what FreeType/GDI/DirectWrite do internally. But all of those approaches apply bold/italic **once** at the outline level before rasterization.

Bitmap-level post-processing opens up:
- **Stackable effects**: `.Bold().Bold().Bold()` → progressively bolder glyphs (one big blob!)
- **Backend-agnostic**: Works with any rasterizer, even future custom ones with no outline access
- **Composable with outline bold**: Apply outline bold first (Phase 32d), then add extra bitmap dilation on top
- **Artistic control**: Fine-tuned bold/italic strength independent of what the rasterizer provides

## Architecture

Implement as `IGlyphPostProcessor` instances (same pattern as existing `OutlinePostProcessor`, `ShadowPostProcessor`, `GradientPostProcessor`):

```
Rasterize glyph → [BoldPostProcessor] → [ItalicPostProcessor] → pack into atlas
```

These slot into the existing post-processing pipeline after rasterization and before atlas packing. Multiple instances can be chained.

## Implementation Plan

### BoldPostProcessor (Bitmap Dilation)

Apply a morphological dilation (max filter) to expand the glyph bitmap by N pixels:

```
For each pixel (x, y):
    output[x, y] = max(input[x-r..x+r, y-r..y+r])
```

Where `r` is the bold radius in pixels.

**Algorithm**: For efficiency, separate into horizontal and vertical passes (separable max filter):
1. Horizontal pass: for each row, sliding window max of width `2*r + 1`
2. Vertical pass: for each column, sliding window max of width `2*r + 1`

This is O(n) per pixel regardless of radius (using deque-based sliding window max).

**Metrics adjustment:**
- `Width += 2 * radius`
- `Height += 2 * radius`
- `BearingX -= radius`
- `Advance += 2 * radius` (or `radius` — compare with FreeType)
- Bitmap pitch changes to match new width

**Grayscale handling**: Dilation on grayscale bitmaps produces smooth expanded edges (better than binary dilation).

### ItalicPostProcessor (Bitmap Shear)

Apply horizontal shear to the rendered bitmap:

```
For each row y (from baseline):
    shift_x = (y - baseline) * shear_factor
    shift each pixel in the row by shift_x
```

**Shear factor**: `tan(12°) ≈ 0.2126` for FreeType parity, configurable.

**Sub-pixel shifting**: For fractional shift values, use bilinear interpolation between adjacent pixels for smooth results.

**Metrics adjustment:**
- `Width += abs(total_shear)` (glyph gets wider)
- `BearingX` adjusted for shear direction
- Height unchanged

### API Design

#### Option 1: Post-processor pipeline (follow existing pattern)

```csharp
var result = BmFont.Builder()
    .WithFont("font.ttf")
    .WithSize(32)
    .WithPostProcessor(new BoldPostProcessor(radius: 1))
    .WithPostProcessor(new ItalicPostProcessor(shear: 0.2126f))
    .Build();

// Stack for extreme effect
var result2 = BmFont.Builder()
    .WithFont("font.ttf")
    .WithSize(64)
    .WithPostProcessor(new BoldPostProcessor(radius: 1))
    .WithPostProcessor(new BoldPostProcessor(radius: 1))
    .WithPostProcessor(new BoldPostProcessor(radius: 1))
    .Build();
// ^ each BoldPostProcessor dilates the previous output — progressively bolder!
```

#### Option 2: Convenience methods on builder

```csharp
var result = BmFont.Builder()
    .WithFont("font.ttf")
    .WithSize(32)
    .WithBitmapBold(radius: 2)
    .WithBitmapItalic(shear: 0.3f)
    .Build();
```

### Interaction with Outline-Level Bold/Italic

These are independent and composable:
- **Outline bold** (Phase 32d / FreeType) modifies the glyph shape before rasterization
- **Bitmap bold** (this phase) dilates the rendered bitmap after rasterization
- Both can be applied together: `WithBold()` + `WithBitmapBold()` = outline bold + extra bitmap dilation
- A user could skip outline bold entirely and use only bitmap bold (lower quality but works everywhere)

## Testing

- Verify dilation with radius 1, 2, 3 produces progressively wider glyphs
- Verify shear produces slanted glyphs with correct metrics
- Verify stacking: 3x BoldPostProcessor produces visibly bolder output than 1x
- Verify composability with outline bold (Phase 32d)
- Verify works with all rasterizer backends (FreeType, GDI, DW, StbTrueType)
- Verify SDF compatibility (dilation on SDF values — needs special handling, distance field math)
- Performance benchmark: dilation should be fast (< 1ms per glyph)

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Bitmap dilation quality lower than outline expansion | Low | Expected — this is a convenience feature, not a replacement for outline-level synthesis |
| SDF + bitmap bold is semantically wrong | Medium | Either skip bitmap bold for SDF, or implement distance-field-aware dilation |
| Excessive stacking could break metrics/atlas packing | Low | Document that extreme stacking is for fun, not production use |
| Sub-pixel shear interpolation adds complexity | Low | Start with nearest-neighbor, upgrade to bilinear if quality demands it |

## Success Criteria

- [ ] `BoldPostProcessor` implements bitmap dilation with configurable radius
- [ ] `ItalicPostProcessor` implements bitmap shear with configurable factor
- [ ] Both integrate into the existing `IGlyphPostProcessor` pipeline
- [ ] Stacking works: multiple post-processors can be chained
- [ ] Metrics are correctly adjusted after each transform
- [ ] Builder API exposes convenience methods
- [ ] Works with all rasterizer backends
- [ ] Performance is acceptable (< 1ms per glyph for typical sizes)
