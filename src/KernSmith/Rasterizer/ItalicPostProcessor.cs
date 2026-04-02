using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Simulates italic by applying a horizontal shear transform at the pixel level.
/// Works as a fallback for rasterizers that don't support outline-level italic transforms.
/// </summary>
public sealed class ItalicPostProcessor : IGlyphPostProcessor
{
    /// <summary>Horizontal shear amount (tangent of the italic angle). Default is tan(12 degrees).</summary>
    public float ShearFactor { get; }

    /// <summary>
    /// Creates an italic shear effect.
    /// </summary>
    /// <param name="shearFactor">Horizontal shear amount. Default is 0.2126f (tan 12 degrees, matching FreeType).</param>
    public ItalicPostProcessor(float shearFactor = 0.2126f)
    {
        ShearFactor = shearFactor;
    }

    /// <inheritdoc />
    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;

        var extraWidth = (int)Math.Ceiling(srcH * ShearFactor);
        var dstW = srcW + extraWidth;
        var dstH = srcH;

        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var dstPitch = dstW * bpp;
        var dst = new byte[dstPitch * dstH];

        for (var dy = 0; dy < dstH; dy++)
        {
            // Shear from bottom so the baseline stays roughly in place.
            var shearOffset = (srcH - 1 - dy) * ShearFactor;

            for (var dx = 0; dx < dstW; dx++)
            {
                var srcX = dx - shearOffset;
                var srcY = dy;

                // Source X integer and fractional parts for bilinear interpolation.
                var sx0 = (int)MathF.Floor(srcX);
                var sx1 = sx0 + 1;
                var fx = srcX - sx0;

                if (bpp == 1)
                {
                    var v0 = GetByte(glyph, srcY, sx0);
                    var v1 = GetByte(glyph, srcY, sx1);
                    dst[dy * dstPitch + dx] = (byte)(v0 + (v1 - v0) * fx);
                }
                else
                {
                    for (var c = 0; c < 4; c++)
                    {
                        var v0 = GetByteRgba(glyph, srcY, sx0, c);
                        var v1 = GetByteRgba(glyph, srcY, sx1, c);
                        dst[dy * dstPitch + dx * 4 + c] = (byte)(v0 + (v1 - v0) * fx);
                    }
                }
            }
        }

        // The shear shifts the top of the glyph to the right. The bottom row has the
        // maximum offset of (srcH - 1) * ShearFactor, but we anchored the shear at
        // the bottom, so the bearing-X stays the same and the extra width goes right.
        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX,
            BearingY: metrics.BearingY,
            Advance: metrics.Advance + extraWidth,
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
        if (col < 0 || col >= g.Width)
            return 0;
        var idx = row * g.Pitch + col;
        return idx < g.BitmapData.Length ? g.BitmapData[idx] : (byte)0;
    }

    private static byte GetByteRgba(RasterizedGlyph g, int row, int col, int channel)
    {
        if (col < 0 || col >= g.Width)
            return 0;
        var idx = row * g.Pitch + col * 4 + channel;
        return idx < g.BitmapData.Length ? g.BitmapData[idx] : (byte)0;
    }
}
