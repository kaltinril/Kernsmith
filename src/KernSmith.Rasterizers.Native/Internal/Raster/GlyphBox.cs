using KernSmith.Rasterizers.Native.Internal.Outlines;

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
    /// Computes the grid for an outline at a given scale.
    /// </summary>
    /// <param name="outline">The outline to bound, in font units.</param>
    /// <param name="declaredBounds">
    /// The font's own box for this exact outline (the <c>glyf</c> header, or the points' control
    /// box where that header is stale), or null when the source has none (CFF) or a transform
    /// has replaced the outline.
    /// </param>
    /// <param name="scale">Pixels per font unit.</param>
    /// <remarks>
    /// With no declared box, the grid comes from the outline's tight bounds — curve extrema
    /// included, control points that the curve never reaches excluded — so rounded glyphs get
    /// no blank border rows or columns.
    ///
    /// A declared box is used as given. The OpenType spec defines the <c>glyf</c> header box as
    /// the tight bounds of the outline, and it is the box stb_truetype derives its bitmaps from.
    /// INVARIANT: untransformed TrueType glyphs keep taking their grid from the declared box —
    /// the header box whenever it holds the outline (the decoder substitutes the control box
    /// only for a stale header) — and a declared box that scales to an empty grid keeps falling
    /// back to the outline's control hull, not its tight bounds. Native's characterized pixel
    /// output depends on both; changing either changes bitmaps for existing fonts.
    /// </remarks>
    public static GlyphBox Compute(GlyphOutline outline, OutlineBounds? declaredBounds, float scale)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return default;

        if (declaredBounds is { } declared)
        {
            var box = FromBounds(declared.XMin, declared.YMin, declared.XMax, declared.YMax, scale);
            if (!box.IsEmpty)
                return box;

            // Over-sized by a fraction of a pixel is recoverable; clipped ink is not.
            return FromBounds(outline.XMin, outline.YMin, outline.XMax, outline.YMax, scale);
        }

        var tight = OutlineBounds.Tight(outline);
        return FromBounds(tight.XMin, tight.YMin, tight.XMax, tight.YMax, scale);
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
