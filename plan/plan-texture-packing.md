# bmfontier -- Texture Packing

> Part of the [Master Plan](master-plan.md).
> Related: [Rasterization](plan-rasterization.md), [Output Formats](plan-output-formats.md), [API Design](plan-api-design.md)

---

## IAtlasPacker Interface

```csharp
public interface IAtlasPacker
{
    PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int pageWidth, int pageHeight);
}

public record struct GlyphRect(int Id, int Width, int Height);

public class PackResult
{
    public IReadOnlyList<GlyphPlacement> Placements { get; }
    public int PageCount { get; }
}

public record struct GlyphPlacement(int Id, int X, int Y, int Page);
```

Two implementations are provided: `MaxRectsPacker` (primary) and `SkylinePacker` (fast mode). Users can implement `IAtlasPacker` to plug in any other algorithm.

---

## MaxRects (Primary Algorithm)

- **Algorithm**: Maintain a list of free rectangles. For each glyph, find the free rectangle that yields the best BestShortSideFit score. Place the glyph, split overlapping free rectangles, prune contained rectangles.
- **Batch mode**: At each step, evaluate ALL remaining unplaced glyphs against ALL free rectangles. Place the globally best glyph. This gives better results than inserting in a fixed order.
- **Pre-sort**: Sort glyphs by height descending before batch insertion.
- **No rotation**: BMFont format has no rotation field.
- **Efficiency**: 93-97% packing efficiency.

---

## Skyline (Fast Mode)

- **Algorithm**: Maintain a 1D height map (skyline). Place each glyph at the position that minimizes the resulting skyline height.
- **Heuristic**: Bottom-Left (minimize y, break ties by x).
- **Performance**: 2-5x faster than MaxRects, 2-5% less efficient.
- **Use case**: Good for preview/iteration; MaxRects for final export.

---

## Packing Constraints (BMFont Format)

- **No rotation**: The BMFont format has no rotation field, so glyphs cannot be rotated during packing.
- **Integer coordinates**: All x, y, width, height values are integers.
- **Power-of-2 textures**: Optional but recommended. Many game engines require or prefer power-of-2 texture dimensions.
- **Padding and spacing**: Glyphs need configurable padding (added to each glyph's region) and spacing (gap between glyphs). Effective glyph size = `width + padding_left + padding_right`, `height + padding_top + padding_bottom`.

---

## Multi-Page Strategy

1. Calculate effective glyph sizes: `width + padding_left + padding_right`, `height + padding_top + padding_bottom`.
2. Estimate optimal page size: smallest power-of-2 where `size^2 >= total_glyph_area * 1.2`.
3. Cap at `MaxTextureSize` from options.
4. Pack glyphs into the current page. When a glyph does not fit, start a new page.
5. After packing, optionally try a smaller page size if the last page is mostly empty.

---

## Atlas Builder

The `AtlasBuilder` takes packing results and rasterized glyphs, then composites them into atlas page bitmaps:

1. Create a blank bitmap for each page (dimensions from packer).
2. For each `GlyphPlacement`, copy the rasterized glyph's bitmap data to the correct (x, y) position on the page bitmap.
3. Apply padding (fill padding regions with transparent/black pixels).
4. Pass the final page bitmap to the `IAtlasEncoder` for encoding.

---

## IAtlasEncoder Interface

PNG encoding (or any image format) is abstracted behind `IAtlasEncoder` so the encoder can be swapped:

```csharp
public interface IAtlasEncoder
{
    /// Encode raw pixel data to an image format (e.g., PNG).
    byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format);

    /// File extension for the encoded format (e.g., ".png").
    string FileExtension { get; }
}

public enum PixelFormat
{
    Grayscale8,    // 8-bit grayscale (1 byte per pixel)
    Rgba32,        // 32-bit RGBA (4 bytes per pixel)
}
```

The default implementation, `StbPngEncoder`, uses StbImageWriteSharp. Alternative implementations could use BigGustave, a custom minimal PNG writer, or even encode to a non-PNG format (e.g., WebP, raw bitmap) for specialized use cases.

See [Project Structure](plan-project-structure.md) for the full PNG encoding library comparison.
