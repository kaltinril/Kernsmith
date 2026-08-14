namespace KernSmith.Atlas;

/// <summary>
/// Packs glyph rectangles into texture atlas pages using a bin-packing algorithm.
/// </summary>
public interface IAtlasPacker
{
    /// <summary>
    /// Packs glyphs into pages that fit within the given max size.
    /// </summary>
    /// <param name="glyphs">The glyph sizes to pack.</param>
    /// <param name="maxWidth">Max page width in pixels.</param>
    /// <param name="maxHeight">Max page height in pixels.</param>
    /// <returns>Where each glyph was placed and how many pages were needed.</returns>
    PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight);

    /// <summary>
    /// Packs new glyphs into pages of the given size where the <paramref name="occupied"/>
    /// regions are already taken. Existing regions are never moved; new pages may be added
    /// beyond <paramref name="pageCount"/> if the new glyphs don't fit. The returned
    /// <see cref="PackResult.Placements"/> cover only <paramref name="newGlyphs"/>.
    /// </summary>
    /// <param name="newGlyphs">The new glyph sizes to place.</param>
    /// <param name="occupied">Regions already taken by existing glyphs.</param>
    /// <param name="pageCount">Number of existing pages.</param>
    /// <param name="maxWidth">Page width in pixels.</param>
    /// <param name="maxHeight">Page height in pixels.</param>
    /// <returns>Where each new glyph was placed and the total page count after packing.</returns>
    /// <exception cref="NotSupportedException">
    /// The packer does not support incremental packing (the default).
    /// </exception>
    PackResult PackInto(IReadOnlyList<GlyphRect> newGlyphs, IReadOnlyList<OccupiedRect> occupied,
        int pageCount, int maxWidth, int maxHeight)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support incremental packing; use the MaxRects packer.");
}
