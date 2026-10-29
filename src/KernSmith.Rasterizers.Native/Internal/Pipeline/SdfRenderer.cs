using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Distance;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Single-channel signed distance field, computed from the outline itself: each texel holds
/// the distance from its centre to the filled region's boundary, signed by the non-zero rule.
/// </summary>
/// <remarks>
/// Output matches FreeType's SDF renderer so the backends are interchangeable: inside bright,
/// 128 on the edge, <c>128 ± ⌊|d|·128/spread⌋</c> clamped to 0..255, and the glyph box padded
/// by the spread on every side with the bearings moved to match.
///
/// The spread is <see cref="RasterOptions.SdfSpread"/> rounded half away from zero — the same
/// rounding the <c>.fnt</c> <c>sdfSpread</c> metadata uses, so a shader reading the metadata
/// decodes the field exactly. Unlike FreeType it is not clamped to 2..32.
/// </remarks>
internal sealed class SdfRenderer : IGlyphRenderer
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly SdfRenderer Instance = new();

    private SdfRenderer()
    {
    }

    /// <summary>The spread in whole pixels — also the padding on each side of the glyph box.</summary>
    public static int SpreadPixels(RasterOptions options)
    {
        // Computed in double and capped, so a huge spread cannot wrap an int; one past
        // GlyphLimits.MaxDimension is already too big for any padded bitmap. NaN and anything
        // under one pixel pad by one pixel.
        double spread = Math.Round((double)options.SdfSpread, MidpointRounding.AwayFromZero);
        return spread >= 1 ? (int)Math.Min(spread, GlyphLimits.MaxDimension + 1) : 1;
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        int pad = SpreadPixels(options);
        GlyphLimits.EnsureBitmap(glyph.GlyphIndex, glyph.Box.Width + (2L * pad), glyph.Box.Height + (2L * pad));
        return new GlyphMetrics(
            BearingX: glyph.Box.BearingX - pad,
            BearingY: glyph.Box.BearingY + pad,
            Advance: glyph.Advance,
            Width: glyph.Box.Width + (2 * pad),
            Height: glyph.Box.Height + (2 * pad));
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        int pad = SpreadPixels(options);
        var metrics = Measure(glyph, options);
        var shape = DistanceShape.Build(
            glyph.Outline,
            glyph.ToPixels,
            glyph.Box.MinX - pad,
            glyph.Box.MinY - pad,
            SignedDistanceField.ToleranceFor(pad));

        var field = SignedDistanceField.Compute(shape, metrics.Width, metrics.Height, pad);
        var bitmap = new byte[field.Length];
        for (int i = 0; i < field.Length; i++)
            bitmap[i] = SignedDistanceField.Encode(field[i], pad);

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = bitmap,
            Width = metrics.Width,
            Height = metrics.Height,
            Pitch = metrics.Width,
            Metrics = metrics,
            Format = PixelFormat.Grayscale8
        };
    }
}
