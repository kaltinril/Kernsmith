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
    public void HairlineWithVerticesOnScanlineCentres_KeepsOnePixelPerRow()
    {
        // The same hairline built from one-pixel edges whose shared vertices sit exactly on the
        // row scanlines (y = n + 0.5): each row's crossing comes from an edge that ends there
        // and one that starts there, so a scanline range that misses an edge's exact endpoint
        // loses that row's dropout pixel.
        var edges = new List<EdgeSegment>();
        float[] ys = [0f, 0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6f];
        for (int i = 0; i + 1 < ys.Length; i++)
        {
            edges.Add(new EdgeSegment(2.4f, ys[i], 2.4f, ys[i + 1]));
            edges.Add(new EdgeSegment(2.1f, ys[i + 1], 2.1f, ys[i]));
        }

        edges.Add(new EdgeSegment(2.1f, 0f, 2.4f, 0f));
        edges.Add(new EdgeSegment(2.4f, 6f, 2.1f, 6f));
        var bitmap = Mono(5, 6, [.. edges]);

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
    public void AbuttingContours_DropOutAsOneStroke()
    {
        // Two 0.4px contours meeting exactly at x = 2: one 0.8px stroke under 50% in both
        // columns. It must restore one pixel per row, as the same stroke drawn as one contour
        // does, not one per contour.
        var bitmap = Mono(5, 4, [.. Rect(1.6f, 0f, 2f, 4f), .. Rect(2f, 0f, 2.4f, 4f)]);
        var single = Mono(5, 4, Rect(1.6f, 0f, 2.4f, 4f));

        bitmap.ShouldBe(single);
        for (int y = 0; y < 4; y++)
            Enumerable.Range(0, 5).Count(x => bitmap[(y * 5) + x] == 255).ShouldBe(1, $"row {y}");
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
