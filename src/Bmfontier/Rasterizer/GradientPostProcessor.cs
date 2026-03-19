namespace Bmfontier.Rasterizer;

/// <summary>
/// Post-processor that applies a vertical color gradient to glyph bitmaps,
/// converting them from grayscale to RGBA.
/// </summary>
public sealed class GradientPostProcessor : IGlyphPostProcessor
{
    public byte TopR { get; }
    public byte TopG { get; }
    public byte TopB { get; }
    public byte BottomR { get; }
    public byte BottomG { get; }
    public byte BottomB { get; }

    public GradientPostProcessor(byte topR, byte topG, byte topB, byte bottomR, byte bottomG, byte bottomB)
    {
        TopR = topR; TopG = topG; TopB = topB;
        BottomR = bottomR; BottomG = bottomG; BottomB = bottomB;
    }

    /// <summary>
    /// Creates a gradient post-processor from two RGB color tuples.
    /// </summary>
    public static GradientPostProcessor Create(
        (byte R, byte G, byte B) topColor,
        (byte R, byte G, byte B) bottomColor)
        => new(topColor.R, topColor.G, topColor.B,
               bottomColor.R, bottomColor.G, bottomColor.B);

    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (glyph.Width == 0 || glyph.Height == 0)
            return glyph;

        // Convert grayscale bitmap to RGBA with gradient coloring
        var rgba = new byte[glyph.Width * glyph.Height * 4];

        for (var y = 0; y < glyph.Height; y++)
        {
            // Interpolation factor: 0.0 at top, 1.0 at bottom
            var t = glyph.Height > 1 ? (float)y / (glyph.Height - 1) : 0f;

            var r = (byte)(TopR + (BottomR - TopR) * t);
            var g = (byte)(TopG + (BottomG - TopG) * t);
            var b = (byte)(TopB + (BottomB - TopB) * t);

            for (var x = 0; x < glyph.Width; x++)
            {
                var srcIdx = y * glyph.Pitch + x;
                var alpha = glyph.BitmapData[srcIdx]; // grayscale value becomes alpha

                var dstIdx = (y * glyph.Width + x) * 4;
                rgba[dstIdx + 0] = r;     // R
                rgba[dstIdx + 1] = g;     // G
                rgba[dstIdx + 2] = b;     // B
                rgba[dstIdx + 3] = alpha; // A (from original grayscale)
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = rgba,
            Width = glyph.Width,
            Height = glyph.Height,
            Pitch = glyph.Width * 4,
            Metrics = glyph.Metrics,
            Format = PixelFormat.Rgba32
        };
    }
}
