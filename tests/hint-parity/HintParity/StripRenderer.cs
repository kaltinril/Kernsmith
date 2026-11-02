namespace KernSmith.HintParity;

/// <summary>
/// Side-by-side strip for one cell: row 1 FreeType, row 2 Native, row 3 diff (gray where equal,
/// magenta where the two differ, brighter for a larger difference). Every glyph sits on its own
/// common canvas (bearing-aligned, 2 px padding), glyphs are laid out left to right in codepoint
/// order, and the whole strip is scaled up by <see cref="Scale"/> with nearest-neighbour sampling.
/// </summary>
internal static class StripRenderer
{
    public const int Scale = 4;
    private const int Gap = 1;
    private const int RowGap = 2;

    public static void Write(string path, CellResult result)
    {
        var canvases = result.Glyphs
            .Where(g => !g.BothEmpty)
            .Select(g => (g.Id, Canvas: CommonCanvas.Place(g.A, g.B)))
            .ToArray();
        if (canvases.Length == 0)
            return;

        int rowHeight = canvases.Max(c => c.Canvas.Height);
        int width = canvases.Sum(c => c.Canvas.Width + Gap) - Gap;
        int height = (3 * rowHeight) + (2 * RowGap);

        var rgb = new byte[width * height * 3];
        // Row separators: a dim line so the three rows read apart.
        for (int r = 1; r <= 2; r++)
        {
            int y = (r * rowHeight) + ((r - 1) * RowGap);
            for (int x = 0; x < width; x++)
                Set(rgb, width, x, y, 40, 40, 80);
        }

        int cursor = 0;
        foreach (var (_, canvas) in canvases)
        {
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    byte a = canvas.A[(y * canvas.Width) + x];
                    byte b = canvas.B[(y * canvas.Width) + x];
                    Set(rgb, width, cursor + x, y, a, a, a);
                    Set(rgb, width, cursor + x, rowHeight + RowGap + y, b, b, b);

                    int d = Math.Abs(a - b);
                    if (d == 0)
                        Set(rgb, width, cursor + x, (2 * (rowHeight + RowGap)) + y, a, a, a);
                    else
                    {
                        byte m = (byte)Math.Max(96, d);
                        Set(rgb, width, cursor + x, (2 * (rowHeight + RowGap)) + y, m, 0, m);
                    }
                }
            }

            cursor += canvas.Width + Gap;
        }

        var scaled = new byte[width * Scale * height * Scale * 3];
        int scaledWidth = width * Scale;
        for (int y = 0; y < height * Scale; y++)
            for (int x = 0; x < scaledWidth; x++)
            {
                int src = (((y / Scale) * width) + (x / Scale)) * 3;
                int dst = ((y * scaledWidth) + x) * 3;
                scaled[dst] = rgb[src];
                scaled[dst + 1] = rgb[src + 1];
                scaled[dst + 2] = rgb[src + 2];
            }

        PngWriter.WriteRgb(path, scaled, scaledWidth, height * Scale);
    }

    private static void Set(byte[] rgb, int width, int x, int y, byte r, byte g, byte b)
    {
        int i = ((y * width) + x) * 3;
        rgb[i] = r;
        rgb[i + 1] = g;
        rgb[i + 2] = b;
    }
}
