using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// StbTrueType <see cref="AntiAliasMode.None"/> output: thresholding stb_truetype's coverage at
/// 50% must not erase a stroke thinner than a pixel, or a one-pixel stroke split evenly over two
/// pixels (Roboto at 12px rendered "The quick brown fox jumps" as "I ne quick brown fox umos").
/// </summary>
public class StbTrueTypeMonoDropoutTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    public static TheoryData<float, bool> Sizes => new()
    {
        { 8f, false }, { 10f, false }, { 12f, false }, { 16f, false },
        { 8f, true }, { 10f, true }, { 12f, true }, { 16f, true },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void FullHeightStems_LightEveryInnerRow(float size, bool italic)
    {
        // Each glyph has a stroke running the outline's full height (T's stem hangs from its
        // bar), so every row strictly inside the box crosses it. Only the first and last rows
        // may hold a sliver under half a pixel and stay empty.
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, Italic = italic };
        var failures = new List<string>();

        foreach (char c in "lI|hbkdpqT1LEFHBDPRN")
        {
            var glyph = stb.RasterizeGlyph(c, options)!;
            for (int y = 1; y < glyph.Height - 1; y++)
            {
                if (!Enumerable.Range(0, glyph.Width).Any(x => glyph.BitmapData[(y * glyph.Pitch) + x] != 0))
                    failures.Add($"'{c}' row {y} of {glyph.Height} empty");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void FullWidthBars_LightEveryInnerColumn(float size, bool italic)
    {
        // Each glyph has a horizontal bar running the outline's full width.
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, Italic = italic };
        var failures = new List<string>();

        foreach (char c in "TEZFL-_=")
        {
            var glyph = stb.RasterizeGlyph(c, options)!;
            for (int x = 1; x < glyph.Width - 1; x++)
            {
                if (!Enumerable.Range(0, glyph.Height).Any(y => glyph.BitmapData[(y * glyph.Pitch) + x] != 0))
                    failures.Add($"'{c}' column {x} of {glyph.Width} empty");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void NoGlyphWithAnOutline_ComesOutBlank(float size, bool italic)
    {
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, Italic = italic };
        var failures = new List<string>();

        for (char c = '!'; c <= '~'; c++)
        {
            var glyph = stb.RasterizeGlyph(c, options)!;
            if (glyph.Width > 0 && glyph.BitmapData.All(v => v == 0))
                failures.Add($"'{c}' blank");
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void ThickUprightStrokes_AreThresholdedOnly()
    {
        // At 32px Roboto's stems and bars are about 3px: plain 50% thresholding of the
        // grayscale coverage already lights every span, so nothing may be added (no fattening).
        // Upright only: a slanted stem's corner can leave a sub-half-pixel stub that dropout
        // control rightly lights.
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var mono = new RasterOptions { Size = 32, AntiAlias = AntiAliasMode.None };
        var gray = new RasterOptions { Size = 32, AntiAlias = AntiAliasMode.Grayscale };

        foreach (char c in "lI|HETLn-=")
        {
            var expected = CropToInk(Threshold(stb.RasterizeGlyph(c, gray)!));
            var actual = stb.RasterizeGlyph(c, mono)!;

            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}'");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{c}'");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void DropoutControl_OnlyAddsPixels(float size, bool italic)
    {
        // The same advance as the grayscale render, a box inside its box (cropped to the ink), and
        // every pixel plain thresholding lit stays lit at the same pen-relative position: dropout
        // control restores ink, it never moves or removes any.
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var mono = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, Italic = italic };
        var gray = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Grayscale, Italic = italic };

        for (char c = '!'; c <= '~'; c++)
        {
            var expected = Threshold(stb.RasterizeGlyph(c, gray)!);
            var actual = stb.RasterizeGlyph(c, mono)!;

            actual.Metrics.Advance.ShouldBe(expected.Metrics.Advance, $"'{c}'");
            int dx = actual.Metrics.BearingX - expected.Metrics.BearingX;
            int dy = expected.Metrics.BearingY - actual.Metrics.BearingY;
            dx.ShouldBeGreaterThanOrEqualTo(0, $"'{c}'");
            dy.ShouldBeGreaterThanOrEqualTo(0, $"'{c}'");
            (dx + actual.Width).ShouldBeLessThanOrEqualTo(expected.Width, $"'{c}'");
            (dy + actual.Height).ShouldBeLessThanOrEqualTo(expected.Height, $"'{c}'");

            Enumerable.Range(0, expected.BitmapData.Length)
                .Where(i => expected.BitmapData[i] == 255)
                .Where(i =>
                {
                    int x = (i % expected.Width) - dx, y = (i / expected.Width) - dy;
                    return x < 0 || y < 0 || x >= actual.Width || y >= actual.Height
                        || actual.BitmapData[(y * actual.Pitch) + x] != 255;
                })
                .ShouldBeEmpty($"'{c}' lost thresholded pixels");
        }
    }

    /// <summary>The grayscale glyph with its coverage thresholded at 50%, box and metrics kept.</summary>
    private static RasterizedGlyph Threshold(RasterizedGlyph glyph) => new()
    {
        Codepoint = glyph.Codepoint,
        GlyphIndex = glyph.GlyphIndex,
        BitmapData = glyph.BitmapData.Select(v => v >= 128 ? (byte)255 : (byte)0).ToArray(),
        Width = glyph.Width,
        Height = glyph.Height,
        Pitch = glyph.Pitch,
        Metrics = glyph.Metrics,
        Format = glyph.Format,
    };

    /// <summary>Drops the glyph's empty edge rows and columns, moving the bearings to match.</summary>
    private static RasterizedGlyph CropToInk(RasterizedGlyph glyph)
    {
        int left = glyph.Width, top = glyph.Height, right = -1, bottom = -1;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                if (glyph.BitmapData[(y * glyph.Pitch) + x] == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0)
            return glyph;

        int width = right - left + 1, height = bottom - top + 1;
        var data = new byte[width * height];
        for (int y = 0; y < height; y++)
            Array.Copy(glyph.BitmapData, ((top + y) * glyph.Pitch) + left, data, y * width, width);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = data,
            Width = width,
            Height = height,
            Pitch = width,
            Metrics = glyph.Metrics with
            {
                BearingX = glyph.Metrics.BearingX + left,
                BearingY = glyph.Metrics.BearingY - top,
                Width = width,
                Height = height,
            },
            Format = glyph.Format,
        };
    }

    [Fact]
    public void Grayscale_IsNotThresholded()
    {
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());

        var glyph = stb.RasterizeGlyph('l', new RasterOptions { Size = 12, AntiAlias = AntiAliasMode.Grayscale })!;

        glyph.BitmapData.ShouldContain(v => v > 0 && v < 255);
    }
}
