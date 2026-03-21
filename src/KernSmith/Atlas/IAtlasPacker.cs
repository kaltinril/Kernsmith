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
}
