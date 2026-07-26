using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Produces the drop shadow's own coverage as an expanded RGBA glyph, with no
/// original glyph pixels drawn on top. Used to populate a <see cref="ChannelContent.Shadow"/>
/// atlas channel so the shadow can be recolored independently of the glyph at draw time
/// (unlike <see cref="ShadowPostProcessor"/>, which composites the glyph over the shadow).
/// Reuses <see cref="ShadowEffect"/> to generate the shadow layer so the offset/blur math
/// stays identical to the normal (non-channel) shadow rendering path.
/// </summary>
internal sealed class ShadowCoveragePostProcessor : IGlyphPostProcessor
{
    private readonly ShadowEffect _shadowEffect;

    public ShadowCoveragePostProcessor(
        int offsetX,
        int offsetY,
        int blurRadius,
        float opacity = 1.0f,
        bool hardShadow = false,
        int blurPasses = 1,
        int blurKernelSize = 0)
    {
        // Color is irrelevant here: only alpha is ever read out of a Shadow channel.
        _shadowEffect = new ShadowEffect(
            offsetX, offsetY, blurRadius,
            shadowR: 0, shadowG: 0, shadowB: 0,
            opacity: opacity, hardShadow: hardShadow,
            blurPasses: blurPasses, blurKernelSize: blurKernelSize);
    }

    /// <inheritdoc />
    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var alphaData = ExtractAlpha(glyph);
        var layer = _shadowEffect.Generate(alphaData, glyph.Width, glyph.Height, glyph.Width, glyph.Metrics);

        // layer.OffsetX/OffsetY are <= 0 (how far the shadow canvas extends before the
        // glyph origin), matching the expandLeft/expandTop convention used by
        // ShadowPostProcessor/OutlinePostProcessor's Metrics adjustment.
        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX + layer.OffsetX,
            BearingY: metrics.BearingY - layer.OffsetY,
            Advance: metrics.Advance,
            Width: layer.Width,
            Height: layer.Height);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = layer.RgbaData,
            Width = layer.Width,
            Height = layer.Height,
            Pitch = layer.Width * 4,
            Metrics = newMetrics,
            Format = PixelFormat.Rgba32
        };
    }

    /// <summary>
    /// Extracts the alpha channel from a glyph, handling both grayscale and RGBA formats.
    /// Returns a byte array with one byte per pixel (mirrors GlyphCompositor.ExtractAlpha).
    /// </summary>
    private static byte[] ExtractAlpha(RasterizedGlyph glyph)
    {
        var w = glyph.Width;
        var h = glyph.Height;
        var alpha = new byte[w * h];

        if (glyph.Format == PixelFormat.Rgba32)
        {
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var srcIdx = y * glyph.Pitch + x * 4 + 3;
                    alpha[y * w + x] = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                }
            }
        }
        else
        {
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var srcIdx = y * glyph.Pitch + x;
                    alpha[y * w + x] = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                }
            }
        }

        return alpha;
    }
}
