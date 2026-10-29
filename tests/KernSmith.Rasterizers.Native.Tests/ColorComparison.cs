using KernSmith.Font.Models;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Compares RGBA glyphs from two backends: aligns them on a shared canvas by their bearings and
/// scores each channel with SSIM.
/// </summary>
internal static class ColorComparison
{
    /// <summary>
    /// A copy of an RGBA glyph with each color channel multiplied by alpha — the form FreeType's
    /// color path returns.
    /// </summary>
    public static RasterizedGlyph Premultiply(RasterizedGlyph glyph)
    {
        var data = (byte[])glyph.BitmapData.Clone();
        for (int i = 0; i < data.Length; i += 4)
        {
            int a = data[i + 3];
            data[i] = (byte)(((data[i] * a) + 127) / 255);
            data[i + 1] = (byte)(((data[i + 1] * a) + 127) / 255);
            data[i + 2] = (byte)(((data[i + 2] * a) + 127) / 255);
        }

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = data,
            Width = glyph.Width,
            Height = glyph.Height,
            Pitch = glyph.Pitch,
            Metrics = glyph.Metrics,
            Format = glyph.Format
        };
    }

    /// <summary>SSIM of R, G, B and A, in that order, over the union of both glyph boxes.</summary>
    public static double[] ChannelSsim(RasterizedGlyph a, RasterizedGlyph b)
    {
        if (a.Format != PixelFormat.Rgba32 || b.Format != PixelFormat.Rgba32)
            throw new ArgumentException("Both glyphs must be RGBA.");

        int left = Math.Min(a.Metrics.BearingX, b.Metrics.BearingX);
        int top = Math.Max(a.Metrics.BearingY, b.Metrics.BearingY);
        int right = Math.Max(a.Metrics.BearingX + a.Width, b.Metrics.BearingX + b.Width);
        int bottom = Math.Min(a.Metrics.BearingY - a.Height, b.Metrics.BearingY - b.Height);
        int width = right - left;
        int height = top - bottom;

        var scores = new double[4];
        for (int channel = 0; channel < 4; channel++)
        {
            var planeA = Plane(a, channel, left, top, width, height);
            var planeB = Plane(b, channel, left, top, width, height);
            scores[channel] = Ssim(planeA, planeB, width, height);
        }

        return scores;
    }

    /// <summary>The lowest of the four channel scores.</summary>
    public static double MinimumChannelSsim(RasterizedGlyph a, RasterizedGlyph b) => ChannelSsim(a, b).Min();

    private static byte[] Plane(RasterizedGlyph glyph, int channel, int left, int top, int width, int height)
    {
        var plane = new byte[width * height];
        int offsetX = glyph.Metrics.BearingX - left;
        int offsetY = top - glyph.Metrics.BearingY;
        for (int row = 0; row < glyph.Height; row++)
        {
            for (int col = 0; col < glyph.Width; col++)
                plane[((row + offsetY) * width) + col + offsetX] = glyph.BitmapData[(row * glyph.Pitch) + (col * 4) + channel];
        }

        return plane;
    }

    /// <summary>
    /// Mean structural similarity over 8x8 windows, C1 = (0.01*255)^2 and C2 = (0.03*255)^2 —
    /// the same formulation as the coverage comparison in <c>ScanlineRasterizerSsimTests</c>.
    /// </summary>
    private static double Ssim(byte[] a, byte[] b, int width, int height)
    {
        const double C1 = 6.5025;
        const double C2 = 58.5225;
        const int Window = 8;

        double total = 0;
        int windows = 0;

        for (int wy = 0; wy < height; wy += Window)
        {
            for (int wx = 0; wx < width; wx += Window)
            {
                int x1 = Math.Min(wx + Window, width);
                int y1 = Math.Min(wy + Window, height);
                int n = (x1 - wx) * (y1 - wy);

                double meanA = 0, meanB = 0;
                for (int y = wy; y < y1; y++)
                {
                    for (int x = wx; x < x1; x++)
                    {
                        meanA += a[(y * width) + x];
                        meanB += b[(y * width) + x];
                    }
                }

                meanA /= n;
                meanB /= n;

                double varA = 0, varB = 0, covariance = 0;
                for (int y = wy; y < y1; y++)
                {
                    for (int x = wx; x < x1; x++)
                    {
                        double da = a[(y * width) + x] - meanA;
                        double db = b[(y * width) + x] - meanB;
                        varA += da * da;
                        varB += db * db;
                        covariance += da * db;
                    }
                }

                double divisor = Math.Max(1, n - 1);
                varA /= divisor;
                varB /= divisor;
                covariance /= divisor;

                total += (((2 * meanA * meanB) + C1) * ((2 * covariance) + C2))
                    / (((meanA * meanA) + (meanB * meanB) + C1) * (varA + varB + C2));
                windows++;
            }
        }

        return windows == 0 ? 1.0 : total / windows;
    }
}
