using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Dropout control for <see cref="AntiAliasMode.None"/>: thresholding coverage at 50% must not
/// erase strokes thinner than a pixel, or a one-pixel stroke split evenly over two pixels.
/// </summary>
public class MonoDropoutTests
{
    [Fact]
    public void HairlineVerticalStroke_KeepsOnePixelPerRow()
    {
        // 0.3px wide, inside column 2: 30% coverage, under the threshold everywhere.
        var bitmap = Mono(5, 6, Rect(2.1f, 0f, 2.4f, 6f));

        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 5; x++)
                bitmap[(y * 5) + x].ShouldBe((byte)(x == 2 ? 255 : 0), $"pixel ({x},{y})");
        }
    }

    [Fact]
    public void HairlineHorizontalStroke_KeepsOnePixelPerColumn()
    {
        var bitmap = Mono(5, 6, Rect(0f, 3.2f, 5f, 3.5f));

        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 5; x++)
                bitmap[(y * 5) + x].ShouldBe((byte)(y == 3 ? 255 : 0), $"pixel ({x},{y})");
        }
    }

    [Fact]
    public void OnePixelStrokeSplitEvenly_KeepsExactlyOnePixelPerRow()
    {
        // x 2.5-3.5: columns 2 and 3 are each exactly half covered, which truncates to 127 and
        // thresholds off — the whole stroke used to vanish.
        var bitmap = Mono(6, 4, Rect(2.5f, 0f, 3.5f, 4f));

        for (int y = 0; y < 4; y++)
        {
            int lit = 0;
            for (int x = 0; x < 6; x++)
                lit += bitmap[(y * 6) + x] == 255 ? 1 : 0;
            lit.ShouldBe(1, $"row {y}");
            (bitmap[(y * 6) + 2] | bitmap[(y * 6) + 3]).ShouldBe((byte)255, $"row {y}");
        }
    }

    [Fact]
    public void ShapesThatLitEverySpan_AreUnchanged()
    {
        // A solid off-grid rectangle: plain 50% thresholding of its coverage, nothing added.
        var edges = Rect(1.2f, 1.3f, 4.6f, 4.8f);
        var gray = ScanlineRasterizer.Rasterize(edges, 6, 6).Bitmap;
        var mono = Mono(6, 6, edges);

        mono.ShouldBe(gray.Select(v => v >= 128 ? (byte)255 : (byte)0).ToArray());
    }

    [Fact]
    public void Grayscale_IsNotTouched()
    {
        var bitmap = ScanlineRasterizer.Rasterize(Rect(2.1f, 0f, 2.4f, 6f), 5, 6).Bitmap;

        bitmap.ShouldAllBe(v => v < 128);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(7f)]
    [InlineData(8f)]
    public void TinyRoboto_StemsStayConnected(float size)
    {
        // Roboto's stems are about 0.09em: 0.55px at 6px, easily split under 50% across two
        // columns. A vertical stroke must stay unbroken between its first and last lit rows (an
        // end row holding only a sliver of the stroke may stay empty, as with centre sampling),
        // and no glyph with an outline may come out blank.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None };
        var failures = new List<string>();

        foreach (char c in "lI|ijt1Lfk")
        {
            var glyph = native.RasterizeGlyph(c, options)!;
            var lit = Enumerable.Range(0, glyph.Height)
                .Where(y => Enumerable.Range(0, glyph.Width).Any(x => glyph.BitmapData[(y * glyph.Pitch) + x] != 0))
                .ToList();
            if (lit.Count == 0)
                continue; // reported as blank below
            for (int y = lit[0]; y <= lit[^1]; y++)
            {
                if (!lit.Contains(y) && c is not ('i' or 'j'))
                    failures.Add($"'{c}' row {y} empty");
            }
        }

        for (char c = '!'; c <= '~'; c++)
        {
            var glyph = native.RasterizeGlyph(c, options)!;
            if (glyph.Width > 0 && glyph.BitmapData.All(v => v == 0))
                failures.Add($"'{c}' blank");
        }

        failures.ShouldBeEmpty();
    }

    private static byte[] Mono(int width, int height, EdgeSegment[] edges) =>
        ScanlineRasterizer.Rasterize(edges, width, height, antiAlias: AntiAliasMode.None).Bitmap;

    private static EdgeSegment[] Rect(float left, float top, float right, float bottom) =>
    [
        new EdgeSegment(left, top, right, top),
        new EdgeSegment(right, top, right, bottom),
        new EdgeSegment(right, bottom, left, bottom),
        new EdgeSegment(left, bottom, left, top),
    ];
}
