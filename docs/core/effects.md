# Effects and Post-Processing

KernSmith provides two mechanisms for transforming glyph bitmaps: **effects** (composited layers) and **post-processors** (sequential bitmap transforms). Both run after rasterization and before atlas packing.

## Pipeline Overview

```
Font file --> Rasterize glyphs --> Effects (GlyphCompositor) --> Post-processors --> Atlas packing
```

1. The rasterizer produces a grayscale (or RGBA) bitmap for each glyph.
2. **Effects** generate independent layers (shadow, outline, body/gradient) that are composited back-to-front by <xref:KernSmith.Rasterizer.GlyphCompositor>.
3. **Post-processors** receive the composited bitmap and transform it sequentially -- each processor's output feeds the next.
4. The final bitmap is packed into the texture atlas.

## Effects vs Post-Processors

| | Effects (`IGlyphEffect`) | Post-Processors (`IGlyphPostProcessor`) |
|---|---|---|
| **How they work** | Generate independent RGBA layers, composited by `GlyphCompositor` in Z-order | Transform the bitmap in place, chained sequentially |
| **Canvas expansion** | Each layer specifies its own offset; the compositor calculates the union canvas | Each processor must handle its own size changes |
| **Layering** | Back-to-front alpha-over blending (shadow Z=0, outline Z=1, body Z=2) | No layering -- output replaces input |
| **API** | Configured via Builder methods (`WithOutline`, `WithShadow`, `WithGradient`) | Added via `WithPostProcessor()` |
| **Visibility** | Internal (`IGlyphEffect` is internal) | Public (`IGlyphPostProcessor` is public) |

Effects are the recommended path for outline, shadow, and gradient. Post-processors are the extension point for custom transforms and for bold/italic when the rasterizer backend lacks native support.

## Built-in Effects

Effects are configured through the Builder API. When any effect is active, `GlyphCompositor` generates layers and composites them. If no gradient is specified, the glyph body renders as white text.

### Outline

Adds a colored border around each glyph using Euclidean Distance Transform (EDT). The outline expands the glyph bitmap by `width` pixels in each direction. Interior counters (holes in glyphs like O, B, e) are preserved -- only the exterior outline is drawn.

**Parameters:**
- `width` -- outline thickness in pixels
- `r`, `g`, `b` -- outline color (default: black)

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithOutline(2, r: 0, g: 0, b: 0) // 2px black outline
    .Build();
```

### Shadow

Generates a drop shadow layer behind the glyph. Supports offset, blur (two-pass box blur), configurable color, opacity, and a hard shadow mode that binarizes the alpha before blurring.

**Parameters:**
- `offsetX`, `offsetY` -- shadow offset in pixels (default: 2, 2)
- `blur` -- blur radius in pixels (default: 0 = sharp)
- `color` -- shadow color as `(R, G, B)` (default: black)
- `opacity` -- 0.0 to 1.0 (default: 1.0)

When both outline and shadow are active, the shadow is generated from the combined outline+body silhouette so the shadow matches the full bordered shape.

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithShadow(offsetX: 3, offsetY: 3, blur: 2,
        color: (0, 0, 0), opacity: 0.7f)
    .Build();
```

Use `WithHardShadow()` for a binarized shadow silhouette (all non-zero alpha becomes fully opaque before blur).

### Gradient

Applies a two-color linear gradient across the glyph body. Replaces the default white body layer (Z-order 2). The gradient angle and midpoint are configurable.

**Parameters:**
- `startColor`, `endColor` -- gradient colors as `(R, G, B)`
- `angleDegrees` -- gradient direction (default: 90 = top-to-bottom)
- `midpoint` -- where the 50% blend falls, 0.0 to 1.0 (default: 0.5)

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(48)
    .WithGradient(
        startColor: (255, 200, 0),   // gold
        endColor:   (255, 80, 0),    // orange
        angleDegrees: 90f)
    .Build();
```

### Combining Effects

All three effects can be used together:

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(48)
    .WithOutline(3, r: 20, g: 20, b: 60)
    .WithShadow(offsetX: 4, offsetY: 4, blur: 3, opacity: 0.6f)
    .WithGradient(
        startColor: (255, 255, 255),
        endColor:   (180, 180, 255))
    .Build();
```

## Built-in Post-Processors

Post-processors are added via `WithPostProcessor()` and run in the order they are added. Each receives a <xref:KernSmith.Rasterizer.RasterizedGlyph> and returns a new one.

### BoldPostProcessor

Thickens glyph bitmaps using morphological dilation with a circular kernel and distance-based falloff. Expands the bitmap by `strength` pixels in each direction. Works with both grayscale and RGBA glyphs.

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithPostProcessor(new BoldPostProcessor(strength: 1))
    .Build();
```

### ItalicPostProcessor

Simulates italic by applying a horizontal shear transform with bilinear interpolation. The default shear factor is `0.2126f` (tangent of 12 degrees, matching FreeType's synthetic italic angle). The shear is anchored at the bottom so the baseline stays in place.

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithPostProcessor(new ItalicPostProcessor()) // default 12-degree shear
    .Build();
```

### HeightStretchPostProcessor

Scales the glyph height by a percentage using bilinear interpolation. Width is unchanged. Useful for condensed or extended text effects.

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithPostProcessor(new HeightStretchPostProcessor(heightPercent: 120)) // 20% taller
    .Build();
```

### OutlinePostProcessor, ShadowPostProcessor, GradientPostProcessor

These are post-processor equivalents of the built-in effects. They exist for use cases where you need a post-processor-style pipeline (e.g., chaining with other post-processors), but the effect versions via `WithOutline`/`WithShadow`/`WithGradient` are preferred.

> [!NOTE]
> When effects are active, matching post-processors (`OutlinePostProcessor`, `ShadowPostProcessor`, `GradientPostProcessor`) are automatically skipped to avoid double-applying.

### Stacking Multiple Post-Processors

Post-processors chain in order. For example, apply bold first, then stretch:

```csharp
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithPostProcessor(new BoldPostProcessor(strength: 2))
    .WithPostProcessor(new HeightStretchPostProcessor(heightPercent: 110))
    .Build();
```

## Synthetic Bold/Italic: Outline-Level vs Bitmap-Level

KernSmith supports two approaches to bold and italic:

| Approach | How it works | When to use |
|----------|-------------|-------------|
| **Outline-level** (via `WithBold()` / `WithItalic()`) | The rasterizer applies the transform before rasterization, at the vector outline level | Preferred -- produces higher quality results |
| **Bitmap-level** (via `BoldPostProcessor` / `ItalicPostProcessor`) | Transforms the rasterized bitmap using dilation or shear | Fallback for backends that lack native synthetic support (e.g., StbTrueType) |

### Auto-Skip Behavior

When `WithBold()` is active, any `BoldPostProcessor` in the post-processor list is automatically skipped to prevent double-bolding. The same applies to `WithItalic()` and `ItalicPostProcessor`. This means you can safely include bitmap-level post-processors as a fallback without worrying about duplication when outline-level bold/italic is also configured.

```csharp
// Bold post-processor is auto-skipped because WithBold() handles it
var result = BmFont.Builder()
    .WithFont("Roboto-Regular.ttf")
    .WithSize(32)
    .WithBold()
    .WithPostProcessor(new BoldPostProcessor(strength: 1)) // skipped at runtime
    .Build();
```
