namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// A single outline point in font design units. <see cref="OnCurve"/> distinguishes an
/// anchor point on the contour from a quadratic Bezier control point.
/// </summary>
internal readonly record struct GlyphPoint(float X, float Y, bool OnCurve);

/// <summary>
/// One closed contour of a glyph outline. The last point implicitly joins back to the first.
/// </summary>
internal sealed class GlyphContour
{
    /// <summary>Creates a contour over the supplied points.</summary>
    public GlyphContour(GlyphPoint[] points) => Points = points;

    /// <summary>The contour's points, in winding order.</summary>
    public GlyphPoint[] Points { get; }
}

/// <summary>
/// A glyph's outline as parsed from the <c>glyf</c> table, in font design units and with
/// composite components already resolved into concrete contours.
/// </summary>
internal sealed class ParsedGlyph
{
    /// <summary>Creates a parsed glyph.</summary>
    public ParsedGlyph(
        int glyphIndex,
        GlyphContour[] contours,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        bool isComposite)
    {
        GlyphIndex = glyphIndex;
        Contours = contours;
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
        IsComposite = isComposite;
    }

    /// <summary>The glyph index this outline was parsed from.</summary>
    public int GlyphIndex { get; }

    /// <summary>The glyph's contours; empty for glyphs with no outline (e.g. space).</summary>
    public GlyphContour[] Contours { get; }

    /// <summary>Left edge of the glyph's bounding box, in font units.</summary>
    public short XMin { get; }

    /// <summary>Bottom edge of the glyph's bounding box, in font units.</summary>
    public short YMin { get; }

    /// <summary>Right edge of the glyph's bounding box, in font units.</summary>
    public short XMax { get; }

    /// <summary>Top edge of the glyph's bounding box, in font units.</summary>
    public short YMax { get; }

    /// <summary>True when the glyph was assembled from component glyphs.</summary>
    public bool IsComposite { get; }

    /// <summary>True when the glyph has no outline at all.</summary>
    public bool IsEmpty => Contours.Length == 0;

    /// <summary>Total point count across every contour.</summary>
    public int PointCount
    {
        get
        {
            int total = 0;
            foreach (var contour in Contours)
                total += contour.Points.Length;
            return total;
        }
    }

    /// <summary>An outline-less glyph, such as a space.</summary>
    public static ParsedGlyph Empty(int glyphIndex) =>
        new(glyphIndex, [], 0, 0, 0, 0, isComposite: false);
}
