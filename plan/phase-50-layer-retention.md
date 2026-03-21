# Phase 19 — In-Memory Layer Retention

> **Status**: Planning (future — no timeline)
> **Created**: 2026-03-20
> **Goal**: Optionally retain individual effect layer bitmaps in memory on the result, so NuGet consumers can access shadow, outline, and body pixels separately.

---

## Current Behavior

The pipeline composites all effect layers (shadow → outline → body) into a single bitmap per glyph via `GlyphCompositor.Composite()` before atlas packing. Each `AtlasPage` in the result is one fully-composed texture.

The individual layer bitmaps only exist temporarily during rendering. After compositing, they are discarded.

---

## What This Feature Would Add

An option to retain the per-glyph layer bitmaps alongside the normal composed output. The composed atlas remains the primary output — layers are bonus data for consumers who want to pull things apart in their engine.

Nothing changes about file output, atlas packing, or the default behavior. This is purely in-memory retention of data that already exists during the pipeline but is currently thrown away.

### Use Cases

- **Parallax/3D effects in games** — Load each layer into a separate texture, draw at different depths for a 3D text effect.
- **Runtime shaders** — Apply blur to the shadow layer, glow to the outline, different blend modes per layer.
- **Dynamic adjustment** — Change layer offsets, colors, or opacity at runtime without re-generating the font.
- **Custom compositing** — Consumer wants to composite layers themselves with engine-specific blending.

### Example Usage

```csharp
var options = new FontGeneratorOptions { Size = 48, Outline = 3, RetainLayers = true };
var result = BmFont.Generate(fontBytes, options);

// Normal composed output still works
byte[] composedPng = result.GetPngData(0);
string fnt = result.FntText;

// Access individual layers per glyph
foreach (var glyph in result.GlyphLayers)
{
    // glyph.Codepoint — which character
    // glyph.Shadow — layer bitmap + offset (null if no shadow effect)
    // glyph.Outline — layer bitmap + offset (null if no outline effect)
    // glyph.Body — the base glyph bitmap + offset (always present)

    byte[] shadowPixels = glyph.Shadow?.BitmapData;
    int shadowOffsetX = glyph.Shadow?.OffsetX ?? 0;
    // ... load into engine textures, position with offsets
}
```

---

## Technical Approach

### New Option

```csharp
// In FontGeneratorOptions
public bool RetainLayers { get; set; }
```

Default `false` — no behavior change, no extra memory. When `true`, the compositor saves its intermediate layer data on the result.

### New Types

```csharp
/// <summary>
/// Per-glyph layer data retained from the compositing pipeline.
/// </summary>
public sealed class GlyphLayerData
{
    public int Codepoint { get; }
    public LayerBitmap? Shadow { get; }
    public LayerBitmap? Outline { get; }
    public LayerBitmap Body { get; }
}

/// <summary>
/// A single effect layer's bitmap and positioning offset relative to the composed glyph.
/// </summary>
public sealed class LayerBitmap
{
    public byte[] BitmapData { get; }
    public int Width { get; }
    public int Height { get; }
    public int OffsetX { get; }
    public int OffsetY { get; }
    public PixelFormat Format { get; }
}
```

### Pipeline Changes

Minimal changes to `GlyphCompositor.Composite()`:

1. After generating `GlyphLayer` objects (which already happens internally), if `RetainLayers` is true, copy each layer's bitmap into a `GlyphLayerData` object.
2. Compositing still runs normally — the composed glyph is the primary output.
3. Attach the layer data to the result.

### Result Changes

Add to `BmFontResult`:

```csharp
/// <summary>
/// Per-glyph layer bitmaps. Only populated when RetainLayers is enabled.
/// </summary>
public IReadOnlyList<GlyphLayerData>? GlyphLayers { get; }
```

---

## Files That Would Change

| File | Change |
|------|--------|
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Add `RetainLayers` property |
| `src/KernSmith/Rasterizer/GlyphCompositor.cs` | Save layer bitmaps when `RetainLayers` is true |
| `src/KernSmith/Output/BmFontResult.cs` | Add `GlyphLayers` property |
| `src/KernSmith/Rasterizer/GlyphLayerData.cs` | New file — layer data types |
| `src/KernSmith/BmFont.cs` | Thread `RetainLayers` option and layer data through to result |

---

## Considerations

- **Memory** — Retaining layers roughly doubles or triples memory usage per generation (shadow + outline + body bitmaps kept alongside composed). Only allocate when `RetainLayers` is true.
- **No file output changes** — Layers are in-memory only. `ToFile()` still writes the composed atlas. If a consumer wants to save layers to disk, they can encode the bitmaps themselves.
- **No atlas packing changes** — The composed atlas is packed as usual. Layers are raw per-glyph bitmaps, not packed into atlas pages.
- **Channel packing** — Layer retention should still work with channel packing since compositing happens before channel assignment.
- **SDF** — No effect layers in SDF mode, so `GlyphLayers` would just contain the body layer.
- **Backward compatibility** — Default `false` preserves current behavior with zero overhead.
