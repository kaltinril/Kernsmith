using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Post-processor that scales glyph bitmaps vertically by a percentage.
/// A value of 100 means no change; 120 stretches glyphs to 120% height.
/// Uses bilinear interpolation for smooth scaling.
/// </summary>
public sealed class HeightStretchPostProcessor : IGlyphPostProcessor
{
    /// <summary>Height percentage (100 = no change).</summary>
    public int HeightPercent { get; }

    public HeightStretchPostProcessor(int heightPercent)
    {
        HeightPercent = Math.Max(10, heightPercent);
    }

    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (HeightPercent == 100 || glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var dstW = srcW;
        var dstH = Math.Max(1, srcH * HeightPercent / 100);

        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var dstPitch = dstW * bpp;
        var dst = new byte[dstPitch * dstH];

        // Bilinear interpolation vertically, direct copy horizontally.
        var yScale = (double)srcH / dstH;

        for (var dy = 0; dy < dstH; dy++)
        {
            var srcY = dy * yScale;
            var sy0 = (int)srcY;
            var sy1 = Math.Min(sy0 + 1, srcH - 1);
            var fy = (float)(srcY - sy0);

            for (var dx = 0; dx < dstW; dx++)
            {
                if (bpp == 1)
                {
                    var v0 = GetByte(glyph, sy0, dx);
                    var v1 = GetByte(glyph, sy1, dx);
                    dst[dy * dstPitch + dx] = (byte)(v0 + (v1 - v0) * fy);
                }
                else
                {
                    for (var c = 0; c < 4; c++)
                    {
                        var v0 = GetByteRgba(glyph, sy0, dx, c);
                        var v1 = GetByteRgba(glyph, sy1, dx, c);
                        dst[dy * dstPitch + dx * 4 + c] = (byte)(v0 + (v1 - v0) * fy);
                    }
                }
            }
        }

        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX,
            BearingY: metrics.BearingY * HeightPercent / 100,
            Advance: metrics.Advance,
            Width: dstW,
            Height: dstH);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstPitch,
            Metrics = newMetrics,
            Format = glyph.Format
        };
    }

    private static byte GetByte(RasterizedGlyph g, int row, int col)
    {
        var idx = row * g.Pitch + col;
        return idx < g.BitmapData.Length ? g.BitmapData[idx] : (byte)0;
    }

    private static byte GetByteRgba(RasterizedGlyph g, int row, int col, int channel)
    {
        var idx = row * g.Pitch + col * 4 + channel;
        return idx < g.BitmapData.Length ? g.BitmapData[idx] : (byte)0;
    }
}
