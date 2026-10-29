using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Replaces the outline with its stroke (<see cref="OutlineStroker"/>), keeping the advance, so
/// running the pipeline with this as the last transform renders the stroke layer.
/// </summary>
/// <remarks>
/// The stroke is a layer of its own, never merged into the fill: nothing adds this transform to
/// the glyph's own list. How a backend-drawn stroke reaches core's compositor is the open design
/// question in the Phase 168 doc.
/// </remarks>
internal sealed class StrokeTransform : IGlyphTransform
{
    /// <summary>Largest deviation of a flattened curve or round join from the true shape, in pixels.</summary>
    private const float TolerancePixels = 0.05f;

    /// <summary>Creates the transform; all lengths in font units.</summary>
    public StrokeTransform(float width, StrokeJoin join, float miterLimit, float tolerance)
    {
        Width = width;
        Join = join;
        MiterLimit = miterLimit;
        Tolerance = tolerance;
    }

    /// <summary>Full stroke width, in font units.</summary>
    public float Width { get; }

    /// <summary>The corner join.</summary>
    public StrokeJoin Join { get; }

    /// <summary>The miter limit, in half-widths.</summary>
    public float MiterLimit { get; }

    /// <summary>Flattening tolerance, in font units.</summary>
    public float Tolerance { get; }

    /// <summary>
    /// A stroke <paramref name="widthPixels"/> wide at this request's size, flattened to within
    /// 0.05px.
    /// </summary>
    public static StrokeTransform FromPixels(float widthPixels, StrokeJoin join, RasterOptions options, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(options);

        float scale = GlyphPipeline.PixelScale(options, unitsPerEm);
        if (!(scale > 0))
            throw new ArgumentOutOfRangeException(nameof(options), "The request has no pixel size.");

        return new StrokeTransform(widthPixels / scale, join, OutlineStroker.DefaultMiterLimit, TolerancePixels / scale);
    }

    /// <summary>
    /// The join whose corners match core's <see cref="OutlineShape"/> distance metric: Euclidean
    /// rounds them, Chebyshev squares them, Manhattan cuts them diagonally.
    /// </summary>
    public static StrokeJoin JoinFor(OutlineShape shape) => shape switch
    {
        OutlineShape.Square => StrokeJoin.Miter,
        OutlineShape.Plus => StrokeJoin.Bevel,
        _ => StrokeJoin.Round,
    };

    /// <inheritdoc />
    public DecodedGlyph Apply(DecodedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.Outline.IsEmpty)
            return glyph;

        return glyph.WithOutline(OutlineStroker.Stroke(glyph.Outline, Width, Join, MiterLimit, Tolerance));
    }
}
