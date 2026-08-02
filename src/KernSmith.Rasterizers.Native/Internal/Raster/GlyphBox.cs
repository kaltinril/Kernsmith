using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// The pixel grid a glyph is rendered into: its size, where it sits relative to the pen, and
/// the pixel-space origin that maps to the bitmap's top-left corner.
/// </summary>
/// <param name="MinX">Pixel-space X mapping to bitmap column 0 (also the left side bearing).</param>
/// <param name="MinY">Pixel-space Y mapping to bitmap row 0, measured down from the baseline.</param>
/// <param name="Width">Bitmap width in pixels.</param>
/// <param name="Height">Bitmap height in pixels.</param>
internal readonly record struct GlyphBox(int MinX, int MinY, int Width, int Height)
{
    /// <summary>Horizontal offset in pixels from the pen position to the bitmap's left edge.</summary>
    public int BearingX => MinX;

    /// <summary>Vertical offset in pixels from the baseline up to the bitmap's top edge.</summary>
    public int BearingY => -MinY;

    /// <summary>True when there is nothing to render.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Computes the grid for a glyph at a given scale.
    /// </summary>
    /// <param name="glyph">The parsed glyph, for its <c>glyf</c> bounding box.</param>
    /// <param name="outline">The extracted outline, used only as a fallback.</param>
    /// <param name="scale">Pixels per font unit.</param>
    /// <remarks>
    /// The box comes from the glyph's own <c>glyf</c> header bounds, which the OpenType spec
    /// defines as the tight bounds of the rendered outline — curve extrema included. That is
    /// deliberately not <see cref="GlyphOutline"/>'s bounds: those bound the Bezier control
    /// hull, which a curve never reaches, so they would leave blank rows and columns around
    /// every rounded glyph. It is also the box stb_truetype and FreeType (unhinted) derive
    /// their bitmaps from, which keeps native output aligned with the other backends.
    ///
    /// Fonts with a broken or absent header box (composites are the usual offender) fall back
    /// to the control hull: over-sized by a fraction of a pixel is recoverable, clipped ink is
    /// not.
    /// </remarks>
    public static GlyphBox Compute(ParsedGlyph glyph, GlyphOutline outline, float scale)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return default;

        var box = FromBounds(glyph.XMin, glyph.YMin, glyph.XMax, glyph.YMax, scale);
        if (!box.IsEmpty)
            return box;

        return FromBounds(outline.XMin, outline.YMin, outline.XMax, outline.YMax, scale);
    }

    /// <summary>
    /// Converts font-unit bounds into a pixel grid, flipping Y (font units point up, bitmap
    /// rows point down) and expanding outwards so no partially covered pixel is cut off.
    /// </summary>
    private static GlyphBox FromBounds(float xMin, float yMin, float xMax, float yMax, float scale)
    {
        int left = (int)MathF.Floor(xMin * scale);
        int top = (int)MathF.Floor(-yMax * scale);
        int right = (int)MathF.Ceiling(xMax * scale);
        int bottom = (int)MathF.Ceiling(-yMin * scale);

        return new GlyphBox(left, top, right - left, bottom - top);
    }
}
