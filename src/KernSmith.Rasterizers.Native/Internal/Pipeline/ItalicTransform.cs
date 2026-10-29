using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Synthetic italic: a horizontal shear about the baseline, <c>x += y · <see cref="Shear"/></c>,
/// on every point. Exact for Béziers, and the advance is unchanged.
/// </summary>
internal sealed class ItalicTransform : IGlyphTransform
{
    /// <summary>
    /// tan 12° as FreeType's <c>FT_GlyphSlot_Oblique</c> encodes it (16.16 fixed <c>0x0366A</c>),
    /// so the default slant matches the FreeType backend exactly.
    /// </summary>
    public const float DefaultShear = 0x0366A / 65536f;

    /// <summary>The default 12° slant, shared.</summary>
    public static readonly ItalicTransform Instance = new(DefaultShear);

    private ItalicTransform(float shear) => Shear = shear;

    /// <summary>Horizontal offset per font unit of height.</summary>
    public float Shear { get; }

    /// <summary>
    /// The slant for a <c>post.italicAngle</c>: degrees counter-clockwise from vertical, so the
    /// usual negative angle leans right.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the angle is not finite or not within ±60°.</exception>
    public static ItalicTransform FromItalicAngle(float degrees)
    {
        if (!float.IsFinite(degrees) || Math.Abs(degrees) > 60f)
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "Italic angle must be within ±60°.");

        return new ItalicTransform((float)Math.Tan(-degrees * Math.PI / 180.0));
    }

    /// <inheritdoc />
    public DecodedGlyph Apply(DecodedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.Outline.IsEmpty)
            return glyph;

        float shear = Shear;
        var source = glyph.Outline.Commands;
        var sheared = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            sheared[i] = c.Type == OutlineCommandType.CubicTo
                ? c with { EndX = c.EndX + (c.EndY * shear), Ctrl1X = c.Ctrl1X + (c.Ctrl1Y * shear), Ctrl2X = c.Ctrl2X + (c.Ctrl2Y * shear) }
                : c with { EndX = c.EndX + (c.EndY * shear) };
        }

        return glyph.WithOutline(GlyphOutline.FromCommands(sheared));
    }
}
