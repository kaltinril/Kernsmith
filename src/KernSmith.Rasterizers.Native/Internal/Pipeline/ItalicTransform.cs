using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Synthetic italic: a horizontal shear about the baseline, <c>x += y · tan 12°</c>, on every
/// point. Exact for Béziers, and the advance is unchanged.
/// </summary>
internal sealed class ItalicTransform : IGlyphTransform
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly ItalicTransform Instance = new();

    /// <summary>
    /// tan 12° as FreeType's <c>FT_GlyphSlot_Oblique</c> encodes it (16.16 fixed <c>0x0366A</c>),
    /// so the slant matches the FreeType backend exactly.
    /// </summary>
    public const float Shear = 0x0366A / 65536f;

    private ItalicTransform()
    {
    }

    /// <inheritdoc />
    public DecodedGlyph Apply(DecodedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.Outline.IsEmpty)
            return glyph;

        var source = glyph.Outline.Commands;
        var sheared = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            sheared[i] = c.Type == OutlineCommandType.CubicTo
                ? c with { EndX = c.EndX + (c.EndY * Shear), Ctrl1X = c.Ctrl1X + (c.Ctrl1Y * Shear), Ctrl2X = c.Ctrl2X + (c.Ctrl2Y * Shear) }
                : c with { EndX = c.EndX + (c.EndY * Shear) };
        }

        return glyph.WithOutline(GlyphOutline.FromCommands(sheared));
    }
}
