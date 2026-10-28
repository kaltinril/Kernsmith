using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Small-caps geometry: scales a (capital) glyph uniformly about the pen origin on the baseline,
/// advance included, so its cap height lands on the font's x-height.
/// </summary>
/// <remarks>
/// Geometry only. Which codepoints are drawn as scaled capitals — lowercase input mapped to its
/// uppercase glyph, presumably — changes which glyph a codepoint renders, and is a decision
/// recorded as open in the Phase 168 doc; nothing builds this transform yet.
/// </remarks>
internal sealed class SmallCapsTransform : IGlyphTransform
{
    /// <summary>Creates the transform.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="scale"/> is not positive and finite.</exception>
    public SmallCapsTransform(float scale)
    {
        if (!(scale > 0) || !float.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Small-caps scale must be positive and finite.");

        Scale = scale;
    }

    /// <summary>The uniform scale.</summary>
    public float Scale { get; }

    /// <summary>
    /// <c>sxHeight / sCapHeight</c> from <c>OS/2</c>, or null when the font does not declare both
    /// (table version below 2, or zero values).
    /// </summary>
    public static float? ScaleFor(Os2Table os2)
    {
        if (os2.XHeight <= 0 || os2.CapHeight <= 0)
            return null;

        return (float)os2.XHeight / os2.CapHeight;
    }

    /// <inheritdoc />
    public DecodedGlyph Apply(DecodedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        var scaled = glyph.Outline.IsEmpty
            ? glyph
            : glyph.WithOutline(OutlineScaling.Scale(glyph.Outline, Scale, Scale));
        return scaled.WithAdvanceWidth(glyph.AdvanceWidth * Scale);
    }
}
