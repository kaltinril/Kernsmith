using KernSmith.HintParity.Legibility;

namespace KernSmith.HintParity;

/// <summary>
/// Sample text set with each glyph set's own advances (no kerning), one glyph set per row,
/// scaled up nearest-neighbour: the view a reader gets, as opposed to the per-glyph strip.
/// </summary>
internal static class TextStrip
{
    public const int Scale = 4;
    private const int Margin = 2;
    private const int RowGap = 3;

    public static readonly string[] Lines =
    [
        "The quick brown fox jumps",
        "over the lazy dog 0123456789",
        "Hamburgefonstiv WMgy%#@&",
        "minimum rn m cl d Illegal 1lI 0O 8B 6b",
    ];

    /// <summary>Writes the rows (top to bottom) as one PNG; each row is the sample lines set in that glyph set.</summary>
    public static void Write(string path, IReadOnlyList<(IReadOnlyDictionary<int, GlyphBitmap> Glyphs, int LineHeight)> rows)
    {
        var rendered = rows.Select(r => RenderRow(r.Glyphs, r.LineHeight)).ToArray();
        int width = rendered.Max(r => r.Width);
        int height = rendered.Sum(r => r.Height) + (RowGap * (rendered.Length - 1));
        var rgb = new byte[width * height * 3];

        int y = 0;
        foreach (var row in rendered)
        {
            for (int ry = 0; ry < row.Height; ry++)
                for (int rx = 0; rx < row.Width; rx++)
                {
                    byte v = row.Pixels[(ry * row.Width) + rx];
                    Set(rgb, width, rx, y + ry, v, v, v);
                }

            y += row.Height;
            if (y < height)
            {
                for (int g = 0; g < RowGap; g++)
                    for (int x = 0; x < width; x++)
                        Set(rgb, width, x, y + g, 40, 40, 80);
                y += RowGap;
            }
        }

        var scaled = new byte[width * Scale * height * Scale * 3];
        int scaledWidth = width * Scale;
        for (int sy = 0; sy < height * Scale; sy++)
            for (int sx = 0; sx < scaledWidth; sx++)
            {
                int src = (((sy / Scale) * width) + (sx / Scale)) * 3;
                int dst = ((sy * scaledWidth) + sx) * 3;
                scaled[dst] = rgb[src];
                scaled[dst + 1] = rgb[src + 1];
                scaled[dst + 2] = rgb[src + 2];
            }

        PngWriter.WriteRgb(path, scaled, scaledWidth, height * Scale);
    }

    private static (int Width, int Height, byte[] Pixels) RenderRow(IReadOnlyDictionary<int, GlyphBitmap> glyphs, int lineHeight)
    {
        var lines = Lines.Select(l => LegibilityMetrics.Typeset(glyphs, l)).ToArray();
        int ascent = lines.Where(l => l is not null).Select(l => -l!.BaselineTop).DefaultIfEmpty(0).Max();
        int width = lines.Where(l => l is not null).Select(l => l!.XOffset + l.Width).DefaultIfEmpty(1).Max() + (2 * Margin);
        int height = (lineHeight * Lines.Length) + (2 * Margin);
        var pixels = new byte[width * height];
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] is not { } line)
                continue;
            int baseline = Margin + (i * lineHeight) + ascent;
            for (int y = 0; y < line.Height; y++)
            {
                int py = baseline + line.BaselineTop + y;
                if (py < 0 || py >= height)
                    continue;
                for (int x = 0; x < line.Width; x++)
                {
                    int px = Margin + line.XOffset + x;
                    if (px >= 0 && px < width)
                        pixels[(py * width) + px] = Math.Max(pixels[(py * width) + px], line.At(x, y));
                }
            }
        }

        return (width, height, pixels);
    }

    private static void Set(byte[] rgb, int width, int x, int y, byte r, byte g, byte b)
    {
        int i = ((y * width) + x) * 3;
        rgb[i] = r;
        rgb[i + 1] = g;
        rgb[i + 2] = b;
    }
}
