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
            var expected = stb.RasterizeGlyph(c, gray)!;
            var actual = stb.RasterizeGlyph(c, mono)!;

            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}'");
            actual.BitmapData.ShouldBe(Threshold(expected.BitmapData), $"'{c}'");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void DropoutControl_OnlyAddsPixels(float size, bool italic)
    {
        // Same box and metrics as the grayscale render, and every pixel plain thresholding lit
        // stays lit: dropout control restores ink, it never moves or removes any.
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());
        var mono = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, Italic = italic };
        var gray = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Grayscale, Italic = italic };

        for (char c = '!'; c <= '~'; c++)
        {
            var expected = stb.RasterizeGlyph(c, gray)!;
            var actual = stb.RasterizeGlyph(c, mono)!;
            var thresholded = Threshold(expected.BitmapData);

            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}'");
            Enumerable.Range(0, thresholded.Length)
                .Where(i => thresholded[i] == 255 && actual.BitmapData[i] != 255)
                .ShouldBeEmpty($"'{c}' lost thresholded pixels");
        }
    }

    private static byte[] Threshold(byte[] coverage) =>
        coverage.Select(v => v >= 128 ? (byte)255 : (byte)0).ToArray();

    [Fact]
    public void Grayscale_IsNotThresholded()
    {
        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(Roboto());

        var glyph = stb.RasterizeGlyph('l', new RasterOptions { Size = 12, AntiAlias = AntiAliasMode.Grayscale })!;

        glyph.BitmapData.ShouldContain(v => v > 0 && v < 255);
    }
}
