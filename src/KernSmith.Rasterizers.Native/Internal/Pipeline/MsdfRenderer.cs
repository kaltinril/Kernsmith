using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Distance;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Multi-channel signed distance fields as RGBA: R, G and B each hold the distance to the
/// nearest edge of their color (<see cref="EdgeColoring"/>), so <c>median(R, G, B)</c> keeps
/// corners sharp. MSDF sets alpha to 255; MTSDF puts the single-channel field in alpha.
/// </summary>
/// <remarks>
/// Every channel uses <see cref="SdfRenderer"/>'s encoding, spread and padding, so the glyph
/// metrics are identical to the single-channel SDF and MTSDF's alpha is byte-identical to it.
/// </remarks>
internal sealed class MsdfRenderer : IGlyphRenderer
{
    /// <summary>RGB distances, opaque alpha.</summary>
    public static readonly MsdfRenderer Msdf = new(trueDistanceInAlpha: false);

    /// <summary>RGB distances plus the single-channel distance in alpha.</summary>
    public static readonly MsdfRenderer Mtsdf = new(trueDistanceInAlpha: true);

    private readonly bool _trueDistanceInAlpha;

    private MsdfRenderer(bool trueDistanceInAlpha)
    {
        _trueDistanceInAlpha = trueDistanceInAlpha;
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options) =>
        SdfRenderer.Instance.Measure(glyph, options);

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        int pad = SdfRenderer.SpreadPixels(options);
        var metrics = Measure(glyph, options);
        int width = metrics.Width, height = metrics.Height;
        var shape = DistanceShape.Build(
            glyph.Outline,
            glyph.ToPixels,
            glyph.Box.MinX - pad,
            glyph.Box.MinY - pad,
            SignedDistanceField.ToleranceFor(pad),
            colorEdges: true);

        var channels = MultiChannelDistanceField.Compute(shape, width, height, pad);
        MultiChannelDistanceField.CorrectClashes(channels, width, height);
        var alpha = _trueDistanceInAlpha ? SignedDistanceField.Compute(shape, width, height, pad) : null;

        var bitmap = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            bitmap[4 * i] = SignedDistanceField.Encode(channels[3 * i], pad);
            bitmap[(4 * i) + 1] = SignedDistanceField.Encode(channels[(3 * i) + 1], pad);
            bitmap[(4 * i) + 2] = SignedDistanceField.Encode(channels[(3 * i) + 2], pad);
            bitmap[(4 * i) + 3] = alpha is null ? (byte)255 : SignedDistanceField.Encode(alpha[i], pad);
        }

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = bitmap,
            Width = width,
            Height = height,
            Pitch = width * 4,
            Metrics = metrics,
            Format = PixelFormat.Rgba32
        };
    }
}
