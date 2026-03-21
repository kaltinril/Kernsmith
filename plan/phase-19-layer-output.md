# Phase 19 — Separate Composite Layer Output

> **Status**: Planning (exploratory/future — no timeline)
> **Created**: 2026-03-20
> **Goal**: Allow users to output individual effect layers as separate atlas pages instead of compositing them into a single bitmap per glyph.

---

## Current Behavior

The pipeline composites all effect layers (shadow → outline → body) into a single bitmap per glyph via `GlyphCompositor.Composite()` before atlas packing. Each `AtlasPage` in the result is one fully-composed texture.

The layers only exist temporarily during rendering. After compositing, they are discarded.

---

## What This Feature Would Add

A new option to skip the final compositing blend and output each effect layer as its own set of atlas pages. A font with shadow + outline + body would produce three layer groups, each with its own atlas page(s).

### Use Cases

- **Parallax/3D effects in games** — Draw shadow, outline, and body at different depths or with different transforms.
- **Runtime shaders** — Apply different shader programs to different layers (e.g., blur on shadow, glow on outline).
- **Dynamic adjustment** — Change layer offsets, colors, or opacity at runtime without re-generating the font.

---

## Technical Approach

### New Option

```csharp
public bool SeparateLayerOutput { get; set; }
public bool SynchronizedLayerLayout { get; set; } = true;
```

### Pipeline Changes

When `SeparateLayerOutput` is true:

1. Generate layers via `GlyphCompositor.GenerateLayers()` (extract from existing `Composite()` internals) but do not blend.
2. Convert each `GlyphLayer` to a `RasterizedGlyph` with adjusted metrics.
3. Pack each layer group separately — or use synchronized packing if `SynchronizedLayerLayout` is true.

### Synchronized vs Independent Packing

- **Synchronized** (default) — Compute union bounding box per glyph across all layers, pack once. All layers share the same UV coordinates. Wastes some atlas space but simplifies multi-texture rendering.
- **Independent** — Each layer packed separately. Different UV coordinates per layer. More efficient but harder to consume.

### Result Type

Preferred approach — a wrapper type:

```csharp
public sealed class LayeredBmFontResult
{
    public IReadOnlyDictionary<string, BmFontResult> Layers { get; }
}
```

Layer keys: `"shadow"` (Z=0), `"outline"` (Z=1), `"body"` (Z=2).

### File Output

```
myfont_shadow.fnt   + myfont_shadow_0.png
myfont_outline.fnt  + myfont_outline_0.png
myfont_body.fnt     + myfont_body_0.png
```

---

## Files That Would Change

| File | Change |
|------|--------|
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Add `SeparateLayerOutput` and `SynchronizedLayerLayout` |
| `src/KernSmith/Rasterizer/GlyphCompositor.cs` | Extract `GenerateLayers()` from `Composite()` internals |
| `src/KernSmith/Rasterizer/IGlyphEffect.cs` | Make `IGlyphEffect` and `GlyphLayer` public |
| `src/KernSmith/BmFont.cs` | Branch pipeline when `SeparateLayerOutput` is true |
| `src/KernSmith/Output/BmFontResult.cs` | Add layered output support or create `LayeredBmFontResult` |
| `src/KernSmith/Output/FileWriter.cs` | Handle multi-layer file output |

---

## Considerations

- **Channel packing** — Mutually exclusive with separate layer output.
- **SDF** — No effect layers, so separate layer output does not apply.
- **Custom post-processors** — Need design decision on whether they apply per-layer or are skipped.
- **Backward compatibility** — Default `false` preserves current behavior exactly.
