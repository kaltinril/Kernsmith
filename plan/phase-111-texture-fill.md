# Phase 111 — Texture Fill for Glyphs

> **Status**: Exploratory
> **Created**: 2026-03-30
> **Depends on**: Core rasterizer + effects pipeline
> **Goal**: Allow glyph interiors to be filled with a texture image instead of flat colors or gradients, enabling rich visual styles like wood grain, metal, stone, fire, fabric, or any arbitrary image mapped onto text.

---

## Background

KernSmith currently supports three fill modes for glyphs:
- **White/flat**: Default — renders glyphs in solid white (game engine applies color at runtime)
- **Gradient**: Two-color linear gradient via `GradientEffect`
- **Outline + fill**: Outline color with inner fill via `OutlineEffect`

Many bitmap font tools (and game UIs in general) support filling text with a texture or pattern. This is especially popular for:
- Fantasy/RPG game UI (stone, metal, wood lettering)
- Title screens and logos
- Stylized UI where text matches the game's visual theme
- Retro/pixel art fonts with pattern fills

## Proposed Capabilities

### 1. Texture Fill Effect

A new `IGlyphEffect` implementation that composites a source texture image into the glyph shape.

- **Input**: A texture image (PNG, TGA, or any supported format) + configuration
- **Behavior**: The glyph's alpha channel acts as a mask — texture pixels are visible only where the glyph has coverage
- **Output**: Glyph pixels replaced with texture pixels, preserving the original alpha shape

### 2. Texture Mapping Modes

How the texture maps onto glyph/atlas space:

- **Tile**: Texture repeats across the glyph (useful for seamless patterns like fabric, scales)
- **Stretch**: Texture stretches to fill each glyph's bounding box (useful for unique-per-glyph looks)
- **Atlas-relative**: Texture maps across the entire atlas, so adjacent glyphs show continuous texture (useful for large scenic textures where you want text to look like a window into the image)
- **Per-glyph offset**: Each glyph samples from a different region of the texture (for variety)
- **Fixed / World-relative**: Texture position is fixed regardless of glyph position (at runtime, text scrolling reveals different parts of the texture — though this may be more of a runtime concern)

### 3. Texture Transform Options

- **Scale**: Zoom the texture in or out relative to glyph size
- **Offset**: Shift the texture origin (X, Y pixels or percentage)
- **Rotation**: Rotate the texture by an angle (e.g., diagonal wood grain)
- **Mirror / Flip**: Horizontal or vertical mirroring

### 4. Blending with Existing Effects

Texture fill should compose well with other effects:

- **Texture + Outline**: Textured interior with a solid-color or separately-textured outline
- **Texture + Shadow**: Textured glyphs casting a solid or blurred shadow
- **Texture + Gradient overlay**: Gradient applied on top of (or blended with) the texture fill
- **Multiple textures**: Layer two textures with different blend modes (e.g., base stone texture + overlay crack pattern)

### 5. Configuration

```csharp
// Proposed options shape
TextureFillOptions
{
    string TexturePath          // Path to texture image file
    // or
    byte[] TextureData          // In-memory texture data

    TextureMappingMode Mode     // Tile, Stretch, AtlasRelative
    float ScaleX, ScaleY        // Texture scale
    float OffsetX, OffsetY      // Texture offset
    float Rotation              // Texture rotation in degrees
    float Opacity               // Blend strength (0.0 - 1.0)
    BlendMode BlendMode         // Normal, Multiply, Screen, Overlay
    bool MirrorX, MirrorY       // Texture mirroring
}
```

## Architecture Considerations

### Implementation as IGlyphEffect

The texture fill fits naturally as an `IGlyphEffect`:

```
TextureFillEffect : IGlyphEffect
    - Loads texture image once (cached across glyphs)
    - For each glyph: samples texture at appropriate coordinates, composites with glyph alpha
    - Effect ordering matters: texture fill should typically be the base layer, with outline/shadow applied after
```

### Texture Loading

- Support common formats: PNG, TGA, BMP, JPEG
- Load once per generation run, cache the decoded pixel buffer
- Validate dimensions (warn if texture is very small relative to atlas size — will look blurry)
- Support both file path and in-memory byte array for flexibility

### Coordinate Mapping

The trickiest part is mapping texture coordinates to glyph pixels:

- **Tile mode**: `texX = glyphX % textureWidth`, `texY = glyphY % textureHeight`
- **Stretch mode**: `texX = glyphX * textureWidth / glyphWidth`, etc.
- **Atlas-relative mode**: Requires knowing the glyph's position in the final atlas — may need to defer texture application to post-packing (like a post-processor), or do a two-pass approach

### Atlas-Relative Mode Complexity

Atlas-relative mapping (continuous texture across the whole atlas) conflicts with the current pipeline where effects are applied per-glyph before packing. Options:

- **Option A**: Apply texture fill as a post-packing step (overlaps with Phase 110 post-processing)
- **Option B**: Apply per-glyph during rasterization using predicted atlas positions (fragile)
- **Option C**: Only support per-glyph modes (tile, stretch) at generation time; atlas-relative is a post-processing feature (Phase 110)

Option C is probably the pragmatic choice for initial implementation.

### Effect Ordering

With texture fill added, the effect chain becomes:

1. Rasterize glyph (produces alpha mask)
2. **Texture fill** (fills interior with texture, masked by alpha)
3. Gradient overlay (optional, blends with texture)
4. Outline (adds border — could also be texture-filled)
5. Shadow (offset copy, blurred)

The `GlyphCompositor` already handles effect ordering — texture fill would slot in as a new layer type.

## CLI Integration

```
kernsmith generate -f MyFont.ttf -s 48 --texture-fill wood.png --texture-mode tile --texture-scale 0.5
```

Multiple textures for different layers:
```
kernsmith generate -f MyFont.ttf -s 48 \
    --texture-fill wood.png --texture-mode tile \
    --outline-texture metal.png --outline-width 2
```

## Open Questions

1. **Outline texture**: Should the outline support a separate texture from the fill? This enables "gold metal border on stone fill" effects but adds complexity.
2. **Runtime vs. bake-time**: Some engines apply texture to text at runtime via shaders. Should KernSmith focus only on bake-time texture fill (burned into the PNG), or also support metadata that tells the engine "use this texture at runtime"?
3. **Seamless texture validation**: Should KernSmith warn if a tiling texture isn't seamless (edge pixels don't match)? Helpful but potentially annoying.
4. **Texture resolution mismatch**: What happens when texture resolution is much lower or higher than glyph size? Auto-scale? Warn? Require explicit scale factor?
5. **Color font interaction**: How does texture fill interact with color fonts (COLR/CPAL, SVG, sbix) that already have their own colors? Override? Blend? Skip?
6. **Memory**: Large textures (4K+) loaded into memory for every generation run could be wasteful. Stream/tile on demand, or just document recommended texture sizes?

## Non-Goals (for this phase)

- Procedural texture generation (Perlin noise, Voronoi, etc. — could be a future phase)
- Animated textures or sprite sheet cycling
- 3D texture mapping (bump maps, normal maps on glyphs)
- Runtime shader generation or export
