using KernSmith.Imaging;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Bitmaps;

/// <summary>
/// Scales a strike bitmap to the requested size, onto the pixel grid that covers its scaled
/// rectangle.
/// </summary>
/// <remarks>
/// <para>
/// Separable, in premultiplied color (so transparent pixels lend no color to their neighbours),
/// on 8-bit sRGB values as stored — not linear light, matching how COLR version 0 layers and
/// browsers scale bitmaps. The filter depends on direction:
/// </para>
/// <list type="bullet">
/// <item>Downscaling (scale ≤ 1): exact area averaging — each output pixel is the mean of the
/// source area it covers, partial source pixels weighted by their overlap. No ringing, no
/// aliasing, and a scale of exactly 1 at whole-pixel bearings copies the bitmap unchanged.</item>
/// <item>Upscaling (scale &gt; 1): bilinear interpolation between source pixel centres, with
/// transparent pixels outside the bitmap, so edges fade across one source pixel rather than
/// turning into blocks.</item>
/// </list>
/// <para>
/// Output is straight-alpha RGBA (unpremultiplied and rounded), like every color glyph Native
/// returns.
/// </para>
/// </remarks>
internal static class BitmapResampler
{
    /// <summary>
    /// Slack for floating-point error at the box edges: a rectangle edge within this of a whole
    /// pixel snaps to it rather than adding an all-but-empty row or column.
    /// </summary>
    private const double EdgeTolerance = 1e-6;

    /// <summary>The pixel grid covering the bitmap's rectangle once scaled.</summary>
    /// <exception cref="RasterizationException">If the scaled position is not a usable number.</exception>
    public static GlyphBox Box(int glyphIndex, BitmapGlyphImage image)
    {
        double left = image.Left * image.ScaleX;
        double right = (image.Left + image.Width) * image.ScaleX;
        double top = -image.Top * image.ScaleY;
        double bottom = -(image.Top - image.Height) * image.ScaleY;

        const double Limit = 1 << 24;
        if (!(Math.Abs(left) < Limit && Math.Abs(right) < Limit && Math.Abs(top) < Limit && Math.Abs(bottom) < Limit))
            throw new RasterizationException($"Glyph {glyphIndex}'s bitmap would be placed outside any usable range.");

        int minX = (int)Math.Floor(left + EdgeTolerance);
        int maxX = (int)Math.Ceiling(right - EdgeTolerance);
        int minY = (int)Math.Floor(top + EdgeTolerance);
        int maxY = (int)Math.Ceiling(bottom - EdgeTolerance);
        return new GlyphBox(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>Resamples <paramref name="source"/> into <paramref name="box"/>; straight RGBA, <c>box.Width × box.Height × 4</c> bytes.</summary>
    public static byte[] Resample(PngImage source, BitmapGlyphImage image, GlyphBox box)
    {
        // The strike pixel grid lands on the output grid unchanged: copy.
        if (image.ScaleX.Equals(1.0) && image.ScaleY.Equals(1.0) && box.Width == source.Width && box.Height == source.Height
            && ((double)box.MinX).Equals(image.Left) && ((double)box.MinY).Equals(-image.Top))
            return (byte[])source.Pixels.Clone();

        var columns = Weights(image.Left, image.ScaleX, source.Width, box.MinX, box.Width);
        var rows = Weights(-image.Top, image.ScaleY, source.Height, box.MinY, box.Height);

        // Premultiply once, in 0..255 floats.
        var pixels = source.Pixels;
        var premultiplied = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            float a = pixels[i + 3] / 255f;
            premultiplied[i] = pixels[i] * a;
            premultiplied[i + 1] = pixels[i + 1] * a;
            premultiplied[i + 2] = pixels[i + 2] * a;
            premultiplied[i + 3] = pixels[i + 3];
        }

        // Horizontal pass: every source row, output columns.
        var horizontal = new float[source.Height * box.Width * 4];
        for (int y = 0; y < source.Height; y++)
        {
            int sourceRow = y * source.Width * 4;
            int targetRow = y * box.Width * 4;
            for (int x = 0; x < box.Width; x++)
            {
                var taps = columns[x];
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var (index, weight) in taps)
                {
                    int s = sourceRow + (index * 4);
                    r += premultiplied[s] * weight;
                    g += premultiplied[s + 1] * weight;
                    b += premultiplied[s + 2] * weight;
                    a += premultiplied[s + 3] * weight;
                }

                int t = targetRow + (x * 4);
                horizontal[t] = r;
                horizontal[t + 1] = g;
                horizontal[t + 2] = b;
                horizontal[t + 3] = a;
            }
        }

        // Vertical pass, then back to straight alpha.
        var output = new byte[box.Width * box.Height * 4];
        for (int y = 0; y < box.Height; y++)
        {
            var taps = rows[y];
            for (int x = 0; x < box.Width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var (index, weight) in taps)
                {
                    int s = ((index * box.Width) + x) * 4;
                    r += horizontal[s] * weight;
                    g += horizontal[s + 1] * weight;
                    b += horizontal[s + 2] * weight;
                    a += horizontal[s + 3] * weight;
                }

                int alpha = Math.Clamp((int)MathF.Round(a), 0, 255);
                if (alpha == 0)
                    continue;

                int t = ((y * box.Width) + x) * 4;
                output[t] = Unpremultiply(r, a);
                output[t + 1] = Unpremultiply(g, a);
                output[t + 2] = Unpremultiply(b, a);
                output[t + 3] = (byte)alpha;
            }
        }

        return output;
    }

    private static byte Unpremultiply(float channel, float alpha) =>
        (byte)Math.Clamp((int)MathF.Round(channel * 255f / alpha), 0, 255);

    /// <summary>
    /// For each output pixel along one axis, the source pixels it draws from and their weights.
    /// Source pixel <c>j</c> spans <c>[(origin + j) × scale, (origin + j + 1) × scale]</c> in output
    /// pixels; output pixel <c>k</c> spans <c>[start + k, start + k + 1]</c>.
    /// </summary>
    private static (int Index, float Weight)[][] Weights(double origin, double scale, int sourceCount, int start, int count)
    {
        var weights = new (int, float)[count][];
        var taps = new List<(int, float)>();
        for (int k = 0; k < count; k++)
        {
            taps.Clear();
            if (scale <= 1.0)
            {
                // Area: the overlap of the output pixel with each source pixel, in output pixels.
                double u0 = ((start + k) / scale) - origin;
                double u1 = ((start + k + 1) / scale) - origin;
                int first = Math.Max(0, (int)Math.Floor(u0));
                int last = Math.Min(sourceCount - 1, (int)Math.Ceiling(u1) - 1);
                for (int j = first; j <= last; j++)
                {
                    double overlap = Math.Min(u1, j + 1) - Math.Max(u0, j);
                    if (overlap > 0)
                        taps.Add((j, (float)(overlap * scale)));
                }
            }
            else
            {
                // Bilinear: the output pixel centre between the two nearest source centres.
                double u = ((start + k + 0.5) / scale) - origin - 0.5;
                int j0 = (int)Math.Floor(u);
                float f = (float)(u - j0);
                if (j0 >= 0 && j0 < sourceCount)
                    taps.Add((j0, 1f - f));
                if (j0 + 1 >= 0 && j0 + 1 < sourceCount && f > 0)
                    taps.Add((j0 + 1, f));
            }

            weights[k] = [.. taps];
        }

        return weights;
    }
}
