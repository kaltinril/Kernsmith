using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// StbTrueType <see cref="AntiAliasMode.None"/> glyph boxes are cropped to their ink, as the
/// Native backend's are. stb_truetype's box is the outline's extent rounded outward, so after
/// thresholding most small glyphs carried an empty edge row or column (Roboto at 12px: 73 of 94
/// glyphs, boxes 30% larger in area than Native's), and <c>GetGlyphMetrics</c> must describe the
/// cropped bitmap <c>RasterizeGlyph</c> returns.
/// </summary>
public sealed class StbTrueTypeMonoCropTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    /// <summary>Size, bold, italic: the plain path and the styled (<c>stbtt_Rasterize</c>) path.</summary>
    public static TheoryData<float, bool, bool> Cases => new()
    {
        { 8f, false, false }, { 12f, false, false }, { 16f, false, false },
        { 8f, true, false }, { 12f, true, false }, { 16f, true, false },
        { 8f, false, true }, { 12f, false, true }, { 16f, false, true },
    };

    private static RasterOptions Options(float size, bool bold, bool italic, AntiAliasMode aa = AntiAliasMode.None) =>
        new() { Size = size, AntiAlias = aa, Bold = bold, Italic = italic };

    [Theory]
    [MemberData(nameof(Cases))]
    public void NoGlyph_HasAnEmptyEdgeRowOrColumn(float size, bool bold, bool italic)
    {
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var options = Options(size, bold, italic);
        var failures = new List<string>();

        for (char c = '!'; c <= '~'; c++)
        {
            var glyph = stb.RasterizeGlyph(c, options)!;
            if (glyph.Width == 0 || glyph.Height == 0)
                continue;

            if (RowIsEmpty(glyph, 0)) failures.Add($"'{c}' first row");
            if (RowIsEmpty(glyph, glyph.Height - 1)) failures.Add($"'{c}' last row");
            if (ColumnIsEmpty(glyph, 0)) failures.Add($"'{c}' first column");
            if (ColumnIsEmpty(glyph, glyph.Width - 1)) failures.Add($"'{c}' last column");
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GlyphMetrics_DescribeTheCroppedBitmap(float size, bool bold, bool italic)
    {
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var options = Options(size, bold, italic);
        var failures = new List<string>();

        for (char c = '!'; c <= '~'; c++)
        {
            var rendered = stb.RasterizeGlyph(c, options)!;
            var measured = stb.GetGlyphMetrics(c, options)!.Value;
            if (measured != rendered.Metrics)
                failures.Add($"'{c}': measured {measured}, rendered {rendered.Metrics}");
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CroppedBox_KeepsEveryInkPixelInPlace(float size, bool bold, bool italic)
    {
        // The cropped glyph is the uncropped mono glyph with its empty edges removed: same ink at
        // the same pen-relative positions. The uncropped render is the grayscale box with the
        // grayscale coverage thresholded (dropout control only adds pixels inside that box).
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var mono = Options(size, bold, italic);
        var gray = Options(size, bold, italic, AntiAliasMode.Grayscale);
        var failures = new List<string>();

        for (char c = '!'; c <= '~'; c++)
        {
            var cropped = stb.RasterizeGlyph(c, mono)!;
            var box = stb.RasterizeGlyph(c, gray)!;
            if (cropped.Width == 0)
                continue;

            int dx = cropped.Metrics.BearingX - box.Metrics.BearingX;
            int dy = box.Metrics.BearingY - cropped.Metrics.BearingY;
            if (dx < 0 || dy < 0 || dx + cropped.Width > box.Width || dy + cropped.Height > box.Height)
            {
                failures.Add($"'{c}': cropped box {cropped.Metrics} is not inside {box.Metrics}");
                continue;
            }

            if (cropped.Metrics.Advance != box.Metrics.Advance)
                failures.Add($"'{c}': advance {cropped.Metrics.Advance} vs {box.Metrics.Advance}");

            for (int y = 0; y < box.Height; y++)
            {
                for (int x = 0; x < box.Width; x++)
                {
                    if (box.BitmapData[(y * box.Pitch) + x] < 128)
                        continue;
                    int cx = x - dx, cy = y - dy;
                    bool lit = cx >= 0 && cy >= 0 && cx < cropped.Width && cy < cropped.Height
                        && cropped.BitmapData[(cy * cropped.Pitch) + cx] == 255;
                    if (!lit)
                        failures.Add($"'{c}': thresholded pixel ({x},{y}) lost");
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(8f)]
    [InlineData(12f)]
    [InlineData(16f)]
    public void Grayscale_IsNotCropped(float size)
    {
        // Grayscale keeps stb's box and its metrics (byte-identical output); only None crops.
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var gray = Options(size, bold: false, italic: false, AntiAliasMode.Grayscale);

        for (char c = '!'; c <= '~'; c++)
        {
            var rendered = stb.RasterizeGlyph(c, gray)!;
            var measured = stb.GetGlyphMetrics(c, gray)!.Value;
            measured.ShouldBe(rendered.Metrics, $"'{c}'");
        }
    }

    private static bool RowIsEmpty(RasterizedGlyph glyph, int y) =>
        !Enumerable.Range(0, glyph.Width).Any(x => glyph.BitmapData[(y * glyph.Pitch) + x] != 0);

    private static bool ColumnIsEmpty(RasterizedGlyph glyph, int x) =>
        !Enumerable.Range(0, glyph.Height).Any(y => glyph.BitmapData[(y * glyph.Pitch) + x] != 0);
}
