using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Bounds on the bitmap one glyph may need, so damaged font data (a tiny <c>unitsPerEm</c>,
/// outlines thousands of ems across, a <c>COLR</c> clip box the size of the coordinate space)
/// or extreme options (a huge DPI or <see cref="RasterOptions.SdfSpread"/>) fail
/// fast with a <see cref="RasterizationException"/> instead of allocating without bound or
/// computing a distance field for minutes.
/// </summary>
internal static class GlyphLimits
{
    /// <summary>
    /// Largest bitmap side, in pixels, after any distance-field padding: the largest atlas page a
    /// GPU commonly accepts, so nothing bigger could be packed anyway.
    /// </summary>
    public const int MaxDimension = 16384;

    /// <summary>
    /// Largest glyph box side, in ems at the requested size. Real glyphs span a few ems at most
    /// (long Arabic ligatures and swashes included); a box this many times the em size only
    /// comes from damaged metrics or coordinates.
    /// </summary>
    public const int MaxEms = 64;

    /// <summary>The requested em size in pixels.</summary>
    public static double PixelsPerEm(RasterOptions options) => (double)options.Size * options.Dpi / 72.0;

    /// <summary>Rejects a glyph box larger than <see cref="MaxEms"/> ems or <see cref="MaxDimension"/> pixels.</summary>
    /// <param name="glyphIndex">The glyph, for the error message.</param>
    /// <param name="width">Box width in pixels.</param>
    /// <param name="height">Box height in pixels.</param>
    /// <param name="options">The request, whose size and DPI set the em.</param>
    /// <exception cref="RasterizationException">If the box is over either limit, or not a number.</exception>
    public static void EnsureGlyphBox(int glyphIndex, double width, double height, RasterOptions options)
    {
        double pixelsPerEm = PixelsPerEm(options);

        // The em limit never drops below one pixel per em, so sub-pixel requests keep their glyphs.
        double emLimit = MaxEms * Math.Max(1.0, Math.Abs(pixelsPerEm));
        if (!(width <= emLimit && height <= emLimit))
            throw new RasterizationException(
                $"Glyph {glyphIndex} would be {width}x{height} px at {pixelsPerEm:0.##} px per em, more than the "
                + $"{MaxEms}-em limit; the font's metrics or coordinates are damaged.");

        EnsureBitmap(glyphIndex, width, height);
    }

    /// <summary>Rejects a bitmap with a side longer than <see cref="MaxDimension"/>.</summary>
    /// <exception cref="RasterizationException">If the bitmap is over the limit, or not a number.</exception>
    public static void EnsureBitmap(int glyphIndex, double width, double height)
    {
        if (!(width <= MaxDimension && height <= MaxDimension))
            throw new RasterizationException(
                $"Glyph {glyphIndex} would be a {width}x{height} px bitmap, beyond the {MaxDimension} px limit per side.");
    }
}
