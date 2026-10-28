using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Synthetic bold: expands the outline with <see cref="OutlineEmboldener"/> and grows the advance
/// by the same strength, as FreeType's <c>FT_GlyphSlot_Embolden</c> does.
/// </summary>
/// <param name="strength">Total growth in width and height, in font units.</param>
internal sealed class BoldTransform(float strength) : IGlyphTransform
{
    /// <summary>Total growth in width and height, in font units.</summary>
    public float Strength { get; } = strength;

    /// <summary>
    /// The FreeType backend's bold strength, <c>max(0.5px, y_ppem / 24)</c>, converted to font
    /// units for this request.
    /// </summary>
    /// <remarks>
    /// INVARIANT: mirrors <c>FreeTypeRasterizer.EmboldenGlyph</c> bit for bit — <c>y_ppem</c> is
    /// FreeType's whole-pixel em size for the char size <c>EnsureCharSize</c> requests, and the
    /// strength is truncated in 26.6 fixed point. Keep the two in sync, or Native's bold drifts
    /// from FreeType's.
    /// </remarks>
    public static float StrengthInFontUnits(RasterOptions options, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(options);

        float scale = GlyphPipeline.PixelScale(options, unitsPerEm);
        if (!(scale > 0))
            return 0f;

        // FT_Set_Char_Size(size * 64, dpi) → FT_REQUEST_HEIGHT → y_ppem, all in FreeType's integers.
        long charSize = (long)Math.Round(options.Size * 64f);
        long scaledHeight = ((charSize * options.Dpi) + 36) / 72;
        long ppem = (scaledHeight + 32) >> 6;
        long strength = Math.Max(32, ppem * 64 / 24);

        return strength / 64f / scale;
    }

    /// <inheritdoc />
    public DecodedGlyph Apply(DecodedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        // An outline with no orientation is left alone, advance included — FreeType's embolden
        // fails on it and the backend then skips its metric adjustment as well.
        var emboldened = OutlineEmboldener.Embolden(glyph.Outline, Strength);
        if (emboldened is null)
            return glyph;

        var styled = ReferenceEquals(emboldened, glyph.Outline) ? glyph : glyph.WithOutline(emboldened);
        return styled.WithAdvanceWidth(glyph.AdvanceWidth + Strength);
    }
}
