using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Tests.Goldens;

/// <summary>An 8-bit image, one (grayscale) or four (RGBA) bytes per pixel, rows top to bottom.</summary>
internal sealed record GoldenImage(int Width, int Height, int Channels, byte[] Pixels)
{
    /// <summary>
    /// Lays glyphs out left to right, <paramref name="perRow"/> to a row, on a shared baseline
    /// per row, with a two-pixel gutter. Blank and unmapped glyphs take no cell. The sheet is
    /// RGBA when any glyph is; a grayscale glyph on it becomes black with its coverage as alpha,
    /// so it stays visible in an image viewer's usual light background.
    /// </summary>
    public static GoldenImage ContactSheet(IReadOnlyList<RasterizedGlyph> glyphs, int perRow = 12)
    {
        const int Gutter = 2;
        var inked = glyphs.Where(g => g.Width > 0 && g.Height > 0).ToList();
        int channels = inked.Any(g => g.Format == PixelFormat.Rgba32) ? 4 : 1;
        if (inked.Count == 0)
            return new GoldenImage(1, 1, channels, new byte[channels]);

        int cellWidth = inked.Max(g => g.Width) + Gutter;
        var rows = inked.Chunk(perRow).ToList();
        var rowTops = new List<(int Top, int Ascent)>();
        int height = Gutter;
        foreach (var row in rows)
        {
            int ascent = row.Max(g => g.Metrics.BearingY);
            int descent = row.Max(g => g.Height - g.Metrics.BearingY);
            rowTops.Add((height, ascent));
            height += ascent + descent + Gutter;
        }

        int width = Gutter + (Math.Min(perRow, inked.Count) * cellWidth);
        var pixels = new byte[width * height * channels];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int i = 0; i < rows[r].Length; i++)
            {
                var glyph = rows[r][i];
                int left = Gutter + (i * cellWidth);
                int top = rowTops[r].Top + rowTops[r].Ascent - glyph.Metrics.BearingY;
                Blit(glyph, pixels, width, channels, left, top);
            }
        }

        return new GoldenImage(width, height, channels, pixels);
    }

    /// <summary>
    /// Compares two images over the union of their sizes (missing pixels count as zero).
    /// Returns the largest per-channel difference, the number of differing pixels, and a
    /// grayscale-on-black diff image: unchanged pixels show the expected image dimmed, differing
    /// pixels are magenta, brighter for a bigger difference.
    /// </summary>
    public static (int MaxLevelDiff, int DifferingPixels, GoldenImage Diff) Compare(GoldenImage expected, GoldenImage actual)
    {
        int width = Math.Max(expected.Width, actual.Width);
        int height = Math.Max(expected.Height, actual.Height);
        var diff = new byte[width * height * 4];
        int maxDiff = 0, differing = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var e = expected.Rgba(x, y);
                var a = actual.Rgba(x, y);
                int pixelDiff = 0;
                for (int c = 0; c < 4; c++)
                    pixelDiff = Math.Max(pixelDiff, Math.Abs(e[c] - a[c]));

                int o = ((y * width) + x) * 4;
                if (pixelDiff == 0)
                {
                    byte dim = (byte)((e[3] * Math.Max(e[0], Math.Max(e[1], e[2]))) / 255 / 3);
                    diff[o] = diff[o + 1] = diff[o + 2] = dim;
                }
                else
                {
                    differing++;
                    maxDiff = Math.Max(maxDiff, pixelDiff);
                    byte level = (byte)Math.Min(255, 128 + (pixelDiff * 4));
                    diff[o] = level;
                    diff[o + 1] = 0;
                    diff[o + 2] = level;
                }

                diff[o + 3] = 255;
            }
        }

        return (maxDiff, differing, new GoldenImage(width, height, 4, diff));
    }

    /// <summary>The pixel at (x, y) as RGBA; outside the image it is transparent black.</summary>
    private byte[] Rgba(int x, int y)
    {
        if (x >= Width || y >= Height)
            return [0, 0, 0, 0];

        int o = ((y * Width) + x) * Channels;
        return Channels == 4
            ? [Pixels[o], Pixels[o + 1], Pixels[o + 2], Pixels[o + 3]]
            : [Pixels[o], Pixels[o], Pixels[o], 255];
    }

    private static void Blit(RasterizedGlyph glyph, byte[] sheet, int sheetWidth, int channels, int left, int top)
    {
        int bytesPerPixel = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                int source = (y * glyph.Pitch) + (x * bytesPerPixel);
                int target = (((top + y) * sheetWidth) + left + x) * channels;
                if (channels == 1)
                {
                    sheet[target] = glyph.BitmapData[source];
                }
                else if (bytesPerPixel == 4)
                {
                    Array.Copy(glyph.BitmapData, source, sheet, target, 4);
                }
                else
                {
                    sheet[target] = sheet[target + 1] = sheet[target + 2] = 0;
                    sheet[target + 3] = glyph.BitmapData[source];
                }
            }
        }
    }
}
