using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Thickens glyph bitmaps using morphological dilation with a circular kernel.
/// Works as a fallback for rasterizers that don't support outline-level bold transforms.
/// </summary>
public sealed class BoldPostProcessor : IGlyphPostProcessor
{
    /// <summary>Pixels to expand in each direction. Default is 1.</summary>
    public int Strength { get; }

    /// <summary>
    /// Creates a bold effect with the given strength.
    /// </summary>
    /// <param name="strength">Pixels to expand in each direction. Minimum is 1.</param>
    public BoldPostProcessor(int strength = 1)
    {
        Strength = Math.Max(1, strength);
    }

    /// <inheritdoc />
    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var s = Strength;

        var dstW = srcW + 2 * s;
        var dstH = srcH + 2 * s;

        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var dstPitch = dstW * bpp;
        var dst = new byte[dstPitch * dstH];

        // Precompute squared strength for circular neighborhood test.
        var strengthSq = s * s;

        for (var dy = 0; dy < dstH; dy++)
        {
            for (var dx = 0; dx < dstW; dx++)
            {
                // Map destination pixel back to source coordinates.
                var cx = dx - s;
                var cy = dy - s;

                if (bpp == 1)
                {
                    // Grayscale: find maximum alpha in circular neighborhood with distance falloff.
                    float bestAlpha = 0;

                    for (var ky = -s; ky <= s; ky++)
                    {
                        for (var kx = -s; kx <= s; kx++)
                        {
                            var distSq = kx * kx + ky * ky;
                            if (distSq > strengthSq)
                                continue;

                            var sx = cx + kx;
                            var sy = cy + ky;

                            if (sx < 0 || sx >= srcW || sy < 0 || sy >= srcH)
                                continue;

                            var srcIdx = sy * glyph.Pitch + sx;
                            if (srcIdx >= glyph.BitmapData.Length)
                                continue;

                            var srcAlpha = glyph.BitmapData[srcIdx];
                            if (srcAlpha == 0)
                                continue;

                            var dist = MathF.Sqrt(distSq);
                            var falloff = 1f - dist / (s + 1f);
                            var effective = srcAlpha * falloff;

                            if (effective > bestAlpha)
                                bestAlpha = effective;
                        }
                    }

                    dst[dy * dstPitch + dx] = (byte)Math.Min(255, (int)(bestAlpha + 0.5f));
                }
                else
                {
                    // RGBA: dilate alpha channel, propagate RGB from nearest non-zero source pixel.
                    float bestAlpha = 0;
                    var bestR = (byte)0;
                    var bestG = (byte)0;
                    var bestB = (byte)0;
                    var bestDistSq = int.MaxValue;

                    for (var ky = -s; ky <= s; ky++)
                    {
                        for (var kx = -s; kx <= s; kx++)
                        {
                            var distSq = kx * kx + ky * ky;
                            if (distSq > strengthSq)
                                continue;

                            var sx = cx + kx;
                            var sy = cy + ky;

                            if (sx < 0 || sx >= srcW || sy < 0 || sy >= srcH)
                                continue;

                            var srcIdx = sy * glyph.Pitch + sx * 4;
                            if (srcIdx + 3 >= glyph.BitmapData.Length)
                                continue;

                            var srcAlpha = glyph.BitmapData[srcIdx + 3];
                            if (srcAlpha == 0)
                                continue;

                            var dist = MathF.Sqrt(distSq);
                            var falloff = 1f - dist / (s + 1f);
                            var effective = srcAlpha * falloff;

                            if (effective > bestAlpha)
                                bestAlpha = effective;

                            // Track nearest non-zero pixel for RGB propagation.
                            if (distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestR = glyph.BitmapData[srcIdx];
                                bestG = glyph.BitmapData[srcIdx + 1];
                                bestB = glyph.BitmapData[srcIdx + 2];
                            }
                        }
                    }

                    var dstIdx = dy * dstPitch + dx * 4;
                    dst[dstIdx] = bestR;
                    dst[dstIdx + 1] = bestG;
                    dst[dstIdx + 2] = bestB;
                    dst[dstIdx + 3] = (byte)Math.Min(255, (int)(bestAlpha + 0.5f));
                }
            }
        }

        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX - s,
            BearingY: metrics.BearingY + s,
            Advance: metrics.Advance,
            Width: metrics.Width + 2 * s,
            Height: metrics.Height + 2 * s);

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
}
