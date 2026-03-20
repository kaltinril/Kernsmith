# bmfontier -- Texture Packing

> Part of the [Master Plan](master-plan.md).
> Related: [Rasterization](plan-rasterization.md), [Output Formats](plan-output-formats.md), [API Design](plan-api-design.md)

> All types used in this document (`GlyphRect`, `GlyphPlacement`, `PackResult`, `AtlasPage`, `PixelFormat`) are defined in [plan-data-types.md](plan-data-types.md).

---

## IAtlasPacker Interface

> `IAtlasPacker`, `IAtlasEncoder` interfaces and `GlyphRect`, `GlyphPlacement`, `PackResult` types are defined in [plan-data-types.md](plan-data-types.md).

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

## Algorithm Implementation References

The plan describes WHAT algorithms to use. For HOW to implement them:

### MaxRects
See [texture-packing-reference.md](../reference/texture-packing-reference.md), "MaxRects" section for:
- Free rectangle splitting mechanism (4-way split when placed rect overlaps a free rect)
- Containment pruning (remove free rects fully contained within another)
- BestShortSideFit scoring formula: `min(freeRect.width - rect.width, freeRect.height - rect.height)`
- Batch mode: evaluate ALL remaining glyphs against ALL free rects, pick the globally best placement, repeat

### Skyline
See texture-packing-reference.md, "Skyline" section for:
- Segment data structure: list of `(x, y, width)` tuples representing the skyline profile
- Placement search: find the position where the rect fits and the resulting max height is minimized
- Segment update: merge/replace affected segments after placement

---

## Packing Constraints (BMFont Format)

- **No rotation**: The BMFont format has no rotation field, so glyphs cannot be rotated during packing.
- **Integer coordinates**: All x, y, width, height values are integers.
- **Power-of-2 textures**: Optional but recommended. Many game engines require or prefer power-of-2 texture dimensions.
- **Padding and spacing**: Glyphs need configurable padding (added to each glyph's region) and spacing (gap between glyphs). See Padding/Spacing Ownership below.

---

## Padding/Spacing Ownership

Effective size calculation is the CALLER's responsibility. Before calling `Pack()`, the pipeline inflates each glyph rect:

```csharp
var effectiveWidth = glyph.Width + padding.Left + padding.Right + spacing.Horizontal;
var effectiveHeight = glyph.Height + padding.Up + padding.Down + spacing.Vertical;
var rect = new GlyphRect(glyph.Codepoint, effectiveWidth, effectiveHeight);
```

The packer treats these as opaque rectangles. It does not know about padding or spacing.

---

## Multi-Page Strategy

Page size selection is the CALLER's responsibility. The pipeline estimates the optimal page size before calling `Pack()`:

```csharp
int estimatedSize = NextPowerOfTwo((int)Math.Sqrt(totalGlyphArea * 1.2));
estimatedSize = Math.Min(estimatedSize, options.MaxTextureSize);
var result = packer.Pack(glyphRects, estimatedSize, estimatedSize);
```

The packer itself does not auto-size. It packs into the given dimensions and overflows to new pages as needed.

Steps:

1. Calculate effective glyph sizes (see Padding/Spacing Ownership above).
2. Pack glyphs into the current page. When a glyph does not fit, start a new page.
3. After packing, optionally try a smaller page size if the last page is mostly empty.

---

## AtlasBuilder

The `AtlasBuilder` is an internal class (not part of the public API) that composes the final atlas pages.

```csharp
internal static class AtlasBuilder
{
    public static IReadOnlyList<AtlasPage> Build(
        IReadOnlyList<RasterizedGlyph> glyphs,
        PackResult packResult,
        Padding padding,
        IAtlasEncoder encoder)
    {
        // For each page:
        //   1. Allocate a byte[] of pageWidth * pageHeight (grayscale) or *4 (RGBA)
        //   2. For each glyph placed on this page:
        //      - Copy glyph bitmap data into the page buffer at (placement.X + padding.Left, placement.Y + padding.Up)
        //   3. Create AtlasPage with the pixel data
    }
}
```

---

## Error Handling

- **Glyph larger than page**: If any single `GlyphRect` has `Width > pageWidth` or `Height > pageHeight`, throw `AtlasPackingException` before attempting to pack.
- **Zero glyphs**: Return a `PackResult` with `PageCount = 0` and empty `Placements`.
- **Duplicate IDs**: The packer does not validate uniqueness. Callers must ensure `GlyphRect.Id` values are unique.

---

## IAtlasEncoder Interface

PNG encoding (or any image format) is abstracted behind `IAtlasEncoder` so the encoder can be swapped:

> `IAtlasEncoder` interface and `PixelFormat` enum are defined in [plan-data-types.md](plan-data-types.md#interfaces).

The default implementation, `StbPngEncoder`, uses StbImageWriteSharp. Alternative implementations could use BigGustave, a custom minimal PNG writer, or even encode to a non-PNG format (e.g., WebP, raw bitmap) for specialized use cases.

Pixel data in `AtlasPage.PixelData` and `IAtlasEncoder.Encode()` is always top-to-bottom, left-to-right row order. This matches FreeType's bitmap output and PNG's natural row order.

See [Project Structure](plan-project-structure.md) for the full PNG encoding library comparison.
