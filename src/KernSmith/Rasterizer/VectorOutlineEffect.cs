using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// The outline layer for one glyph from a backend-drawn <see cref="OutlineMask"/>
/// (<see cref="OutlineMode.Vector"/>): composited exactly as <see cref="OutlineEffect"/> is — same
/// Z-order, color, canvas (the body grown by the outline width on every side) and solid backing
/// under every body pixel — so only the ring's shape differs between the two modes.
/// </summary>
internal sealed class VectorOutlineEffect : IGlyphEffect
{
    private readonly OutlineMask _mask;
    private readonly int _outlineWidth;
    private readonly byte _r;
    private readonly byte _g;
    private readonly byte _b;

    public VectorOutlineEffect(OutlineMask mask, int outlineWidth, byte r, byte g, byte b)
    {
        _mask = mask;
        _outlineWidth = outlineWidth;
        _r = r;
        _g = g;
        _b = b;
    }

    public int ZOrder => 1;

    public GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics)
    {
        var (alpha, w, h, offsetX, offsetY) = _mask.Canvas(alphaData, width, height, pitch, _outlineWidth);
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] == 0)
                continue;

            rgba[(i * 4) + 0] = _r;
            rgba[(i * 4) + 1] = _g;
            rgba[(i * 4) + 2] = _b;
            rgba[(i * 4) + 3] = alpha[i];
        }

        return new GlyphLayer(rgba, w, h, offsetX, offsetY, ZOrder);
    }

    /// <summary>
    /// The routed-outline glyph <c>ChannelCompositor</c> reads for an Outline channel, in place of
    /// <see cref="OutlinePostProcessor"/>'s: the same canvas and body backing as
    /// <see cref="Generate"/>, as grayscale coverage positioned by its own metrics.
    /// </summary>
    public static RasterizedGlyph ChannelGlyph(RasterizedGlyph body, OutlineMask mask, int outlineWidth)
    {
        int bpp = body.Format == PixelFormat.Rgba32 ? 4 : 1;
        var bodyAlpha = new byte[body.Width * body.Height];
        for (int y = 0; y < body.Height; y++)
        {
            for (int x = 0; x < body.Width; x++)
            {
                int index = (y * body.Pitch) + (x * bpp) + (bpp - 1);
                bodyAlpha[(y * body.Width) + x] = index < body.BitmapData.Length ? body.BitmapData[index] : (byte)0;
            }
        }

        var (alpha, w, h, offsetX, offsetY) = mask.Canvas(bodyAlpha, body.Width, body.Height, body.Width, outlineWidth);
        return new RasterizedGlyph
        {
            Codepoint = body.Codepoint,
            GlyphIndex = body.GlyphIndex,
            BitmapData = alpha,
            Width = w,
            Height = h,
            Pitch = w,
            Metrics = new GlyphMetrics(
                BearingX: body.Metrics.BearingX + offsetX,
                BearingY: body.Metrics.BearingY - offsetY,
                Advance: body.Metrics.Advance,
                Width: w,
                Height: h),
            Format = PixelFormat.Grayscale8,
        };
    }
}
