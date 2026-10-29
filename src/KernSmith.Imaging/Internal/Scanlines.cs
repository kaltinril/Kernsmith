namespace KernSmith.Imaging.Internal;

/// <summary>
/// Reverses the scanline filters of the decompressed image data and writes each pass's pixels,
/// as 8-bit RGBA, to their places in the full image.
/// </summary>
internal static class Scanlines
{
    /// <summary>Unfilters <paramref name="raw"/> in place and fills <paramref name="rgba"/>.</summary>
    /// <param name="raw">Exactly <see cref="Adam7.RawSize"/> bytes of decompressed data.</param>
    /// <param name="header">The image header.</param>
    /// <param name="format">The image's sample layout, palette and transparency.</param>
    /// <param name="rgba">The output, <c>Width × Height × 4</c> bytes.</param>
    /// <exception cref="PngFormatException">On an unknown filter type or an out-of-range palette index.</exception>
    public static void Reconstruct(byte[] raw, PngHeader header, PixelFormat format, byte[] rgba)
    {
        int stride = format.FilterStride;
        int offset = 0;
        byte[]? zeros = null;

        foreach (var pass in Adam7.Passes(header.Interlaced))
        {
            int width = Adam7.Width(pass, header.Width);
            int height = Adam7.Height(pass, header.Height);
            if (width == 0 || height == 0)
                continue;

            int rowBytes = (int)Adam7.RowBytes(width, format.BitsPerPixel);
            // The row above the first row of each pass counts as all zeros.
            zeros ??= new byte[Adam7.RowBytes(header.Width, format.BitsPerPixel)];
            ReadOnlySpan<byte> prior = zeros.AsSpan(0, rowBytes);

            for (int y = 0; y < height; y++)
            {
                int filter = raw[offset];
                var row = raw.AsSpan(offset + 1, rowBytes);
                Unfilter(filter, row, prior, stride);
                Expand(row, format, header, rgba, pass.StartY + (y * pass.StepY), pass, width);
                prior = row;
                offset += 1 + rowBytes;
            }
        }
    }

    /// <summary>Reverses one row's filter, in place.</summary>
    /// <param name="filter">The row's filter type, 0-4.</param>
    /// <param name="row">The filtered bytes, overwritten with the reconstructed ones.</param>
    /// <param name="prior">The reconstructed row above (zeros for a pass's first row).</param>
    /// <param name="stride">Bytes per complete pixel, at least 1.</param>
    private static void Unfilter(int filter, Span<byte> row, ReadOnlySpan<byte> prior, int stride)
    {
        int length = row.Length;
        switch (filter)
        {
            case 0: // None
                return;

            case 1: // Sub: x + a
                for (int i = stride; i < length; i++)
                    row[i] += row[i - stride];
                return;

            case 2: // Up: x + b
                for (int i = 0; i < length; i++)
                    row[i] += prior[i];
                return;

            case 3: // Average: x + floor((a + b) / 2), in at least nine bits
                for (int i = 0; i < stride && i < length; i++)
                    row[i] += (byte)(prior[i] >> 1);
                for (int i = stride; i < length; i++)
                    row[i] += (byte)((row[i - stride] + prior[i]) >> 1);
                return;

            case 4: // Paeth: x + the neighbour nearest a + b - c
                for (int i = 0; i < stride && i < length; i++)
                    row[i] += prior[i]; // a = c = 0, so the predictor is b
                for (int i = stride; i < length; i++)
                    row[i] += Paeth(row[i - stride], prior[i], prior[i - stride]);
                return;

            default:
                throw new PngFormatException($"The PNG uses unknown scanline filter type {filter}.");
        }
    }

    /// <summary>The Paeth predictor; the order of the comparisons is fixed by the spec.</summary>
    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    /// <summary>Converts one reconstructed row of <paramref name="count"/> pixels to RGBA in the full image.</summary>
    private static void Expand(ReadOnlySpan<byte> row, PixelFormat format, PngHeader header, byte[] rgba, int imageY, Adam7.Pass pass, int count)
    {
        int step = pass.StepX * 4;
        int target = (int)((((long)imageY * header.Width) + pass.StartX) * 4);
        int depth = header.BitDepth;

        switch (header.ColorType)
        {
            case PngColorType.TruecolorAlpha when depth == 8 && pass.StepX == 1:
                row.CopyTo(rgba.AsSpan(target, count * 4));
                return;

            case PngColorType.TruecolorAlpha when depth == 8:
                for (int x = 0, s = 0; x < count; x++, s += 4, target += step)
                {
                    rgba[target] = row[s];
                    rgba[target + 1] = row[s + 1];
                    rgba[target + 2] = row[s + 2];
                    rgba[target + 3] = row[s + 3];
                }

                return;

            case PngColorType.TruecolorAlpha:
                for (int x = 0, s = 0; x < count; x++, s += 8, target += step)
                {
                    rgba[target] = Scale16(Sample16(row, s));
                    rgba[target + 1] = Scale16(Sample16(row, s + 2));
                    rgba[target + 2] = Scale16(Sample16(row, s + 4));
                    rgba[target + 3] = Scale16(Sample16(row, s + 6));
                }

                return;

            case PngColorType.Truecolor:
                for (int x = 0; x < count; x++, target += step)
                {
                    int r, g, b;
                    if (depth == 8)
                    {
                        r = row[x * 3];
                        g = row[(x * 3) + 1];
                        b = row[(x * 3) + 2];
                        rgba[target] = (byte)r;
                        rgba[target + 1] = (byte)g;
                        rgba[target + 2] = (byte)b;
                    }
                    else
                    {
                        r = Sample16(row, x * 6);
                        g = Sample16(row, (x * 6) + 2);
                        b = Sample16(row, (x * 6) + 4);
                        rgba[target] = Scale16(r);
                        rgba[target + 1] = Scale16(g);
                        rgba[target + 2] = Scale16(b);
                    }

                    bool clear = format.HasTransparentColor
                        && r == format.TransparentRed && g == format.TransparentGreen && b == format.TransparentBlue;
                    rgba[target + 3] = clear ? (byte)0 : (byte)255;
                }

                return;

            case PngColorType.GrayscaleAlpha:
                for (int x = 0; x < count; x++, target += step)
                {
                    byte gray, alpha;
                    if (depth == 8)
                    {
                        gray = row[x * 2];
                        alpha = row[(x * 2) + 1];
                    }
                    else
                    {
                        gray = Scale16(Sample16(row, x * 4));
                        alpha = Scale16(Sample16(row, (x * 4) + 2));
                    }

                    rgba[target] = rgba[target + 1] = rgba[target + 2] = gray;
                    rgba[target + 3] = alpha;
                }

                return;

            case PngColorType.Grayscale:
                for (int x = 0; x < count; x++, target += step)
                {
                    int value = depth switch
                    {
                        16 => Sample16(row, x * 2),
                        8 => row[x],
                        _ => PackedSample(row, x, depth),
                    };
                    byte gray = depth switch
                    {
                        16 => Scale16(value),
                        8 => (byte)value,
                        _ => (byte)(value * 255 / ((1 << depth) - 1)),
                    };
                    rgba[target] = rgba[target + 1] = rgba[target + 2] = gray;
                    rgba[target + 3] = format.HasTransparentColor && value == format.TransparentGray ? (byte)0 : (byte)255;
                }

                return;

            default: // Indexed
                var palette = format.Palette!;
                int entries = format.PaletteCount;
                for (int x = 0; x < count; x++, target += step)
                {
                    int index = depth == 8 ? row[x] : PackedSample(row, x, depth);
                    if (index >= entries)
                        throw new PngFormatException($"The PNG uses palette index {index}, but its PLTE has only {entries} entries.");
                    palette.AsSpan(index * 4, 4).CopyTo(rgba.AsSpan(target, 4));
                }

                return;
        }
    }

    /// <summary>A 1-, 2- or 4-bit sample; samples are packed from the most significant bit.</summary>
    private static int PackedSample(ReadOnlySpan<byte> row, int x, int depth)
    {
        int bit = x * depth;
        return (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
    }

    private static int Sample16(ReadOnlySpan<byte> row, int offset) => (row[offset] << 8) | row[offset + 1];

    /// <summary>round(v × 255 / 65535), which equals round(v / 257) since 65535 = 255 × 257.</summary>
    private static byte Scale16(int value) => (byte)((value + 128) / 257);
}
