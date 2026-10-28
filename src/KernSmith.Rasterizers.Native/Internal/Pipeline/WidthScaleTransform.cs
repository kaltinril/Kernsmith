using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Condensed / extended: scales every X coordinate and the advance by
/// <see cref="Scale"/>, leaving Y untouched. Below 1 condenses, above 1 extends.
/// </summary>
internal sealed class WidthScaleTransform : IGlyphTransform
{
    /// <summary>Creates the transform.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="scale"/> is not positive and finite.</exception>
    public WidthScaleTransform(float scale)
    {
        if (!(scale > 0) || !float.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Width scale must be positive and finite.");

        Scale = scale;
    }

    /// <summary>The horizontal scale.</summary>
    public float Scale { get; }

    /// <inheritdoc />
    public DecodedGlyph Apply(DecodedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        var scaled = glyph.Outline.IsEmpty
            ? glyph
            : glyph.WithOutline(OutlineScaling.Scale(glyph.Outline, Scale, 1f));
        return scaled.WithAdvanceWidth(glyph.AdvanceWidth * Scale);
    }
}
