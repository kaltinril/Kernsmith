# Phase 10 — Layered Rendering Architecture

> **Status**: Complete
> **Date**: 2026-03-19

---

> Replace the fragile order-dependent post-processor chain with a layered
> compositing system where effects are generated independently and composited
> in a fixed back-to-front order.

## Problem

The current post-processor chain is order-dependent and fragile:

- Gradient skips RGBA input -- outline before gradient kills gradient
- Gradient before outline works, but only by accident of ordering
- Shadow + outline + gradient requires exact ordering knowledge
- Users must understand internal pipeline details to get correct results
- Adding new effects multiplies the ordering complexity

The root cause: **outline, gradient, and shadow operate on different concepts** (border, color, shadow) but are forced into a linear chain where each transforms the output of the previous.

## Current Architecture (Linear Chain)

```
Rasterize -> [PostProcessor1] -> [PostProcessor2] -> [PostProcessor3] -> Pack
```

Each processor receives the output of the previous. Order matters. Some processors skip RGBA input. Adding a new effect requires understanding all existing orderings.

## Proposed Architecture (Layered Compositing)

```
Rasterize -> grayscale glyph body (alpha mask)
                |
                +---> Body effects (gradient, tint) -> colored body layer
                +---> Outline generation (EDT/stroker) -> outline layer
                +---> Shadow generation (blur, offset) -> shadow layer
                |
Composite:  shadow -> outline -> body  (back to front)
```

Each layer is generated **independently from the original grayscale source**. Compositing order is fixed and always correct. Users toggle features on/off without worrying about order.

## Design

### GlyphEffect (new concept)

Replace `IGlyphPostProcessor` with a layered system:

```csharp
interface IGlyphEffect
{
    GlyphLayer Generate(byte[] alphaData, int width, int height, GlyphMetrics metrics);
}

record GlyphLayer(
    byte[] RgbaData,
    int Width, int Height,
    int OffsetX, int OffsetY,  // relative to glyph origin
    int ZOrder                  // compositing order (lower = further back)
);
```

### Built-in effects with fixed Z-order:

| Effect | Z-Order | Description |
|--------|---------|-------------|
| Shadow | 0 | Furthest back -- behind everything |
| Outline | 1 | Behind the glyph body |
| Body (gradient/tint) | 2 | The glyph itself with color applied |

### GlyphCompositor

New class that:
1. Takes the raw grayscale glyph
2. Runs each enabled effect independently (all receive the same grayscale input)
3. Sorts layers by Z-order
4. Composites back-to-front using alpha-over blending
5. Returns a single RGBA glyph ready for packing

### Backward Compatibility

- Keep `IGlyphPostProcessor` for custom user processors
- Custom post-processors run AFTER the compositor output
- Built-in effects (outline, gradient, shadow) use the new system
- `FontGeneratorOptions` keeps the same properties -- no API change for users

## Migration Path

### Phase 1: Internal refactor (no public API change)
- Create `IGlyphEffect`, `GlyphLayer`, `GlyphCompositor`
- Move outline, gradient, shadow from post-processors to effects
- Auto-detect when user has set built-in effects (Outline > 0, GradientTop != null, etc.)
- Use compositor for built-in effects, chain for custom post-processors
- Remove order-dependency for built-in effects

### Phase 2: Public API (optional, future)
- Expose `IGlyphEffect` for users who want custom layers
- Add `WithEffect(IGlyphEffect)` to the builder
- Deprecate `WithPostProcessor()` for built-in effects (keep for backward compat)

## Task Breakdown

| # | Task | Effort | Files |
|---|------|--------|-------|
| 1 | Create `IGlyphEffect` interface and `GlyphLayer` record | Small | New: `Rasterizer/IGlyphEffect.cs` |
| 2 | Create `GlyphCompositor` class | Medium | New: `Rasterizer/GlyphCompositor.cs` |
| 3 | Create `GradientEffect : IGlyphEffect` (extract from GradientPostProcessor) | Medium | New: `Rasterizer/GradientEffect.cs` |
| 4 | Create `OutlineEffect : IGlyphEffect` (extract from OutlinePostProcessor) | Medium | New: `Rasterizer/OutlineEffect.cs` |
| 5 | Create `ShadowEffect : IGlyphEffect` (extract from ShadowPostProcessor) | Medium | New: `Rasterizer/ShadowEffect.cs` |
| 6 | Wire compositor into `BmFont.Generate()` -- detect built-in effects, use compositor | Medium | `BmFont.cs` |
| 7 | Keep `IGlyphPostProcessor` chain for custom processors (runs after compositor) | Small | `BmFont.cs` |
| 8 | Remove post-processor ordering logic from CLI | Small | `GenerateCommand.cs` |
| 9 | Tests: gradient+outline, outline+shadow, all three combined | Medium | `tests/` |

## Edge Cases

| Scenario | Handling |
|----------|----------|
| No effects enabled | Skip compositor, use raw grayscale |
| Only gradient | Single body layer, no compositing needed |
| Only outline | Outline + white body layers |
| All three + custom post-processor | Compositor runs first, then custom post-processor chain |
| Color font (RGBA input) | Body layer preserves RGBA, outline/shadow extract alpha |

## Estimated Effort

- **Total**: 3-4 days
- **Risk**: Medium -- internal refactor with backward compat, but touches core pipeline
- **Benefit**: Eliminates all post-processor ordering bugs permanently

## References

- Photoshop layer compositing model
- BMFont.exe's channel-based approach (glyph in one channel, outline in another)
- Hiero's separate outline + body rendering with fixed composite order
