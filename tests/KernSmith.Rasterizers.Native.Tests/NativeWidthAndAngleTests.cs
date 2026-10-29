using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 168 — <see cref="RasterOptions.WidthPercent"/> (synthetic condensed/expanded) and
/// <see cref="RasterOptions.ItalicAngle"/> on the Native backend.
/// </summary>
public class NativeWidthAndAngleTests
{
    private static NativeRasterizer Load()
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        return rasterizer;
    }

    [Theory]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(150)]
    public void WidthPercent_ScalesTheGlyphAndItsAdvance_Horizontally(int percent)
    {
        using var rasterizer = Load();
        var plain = rasterizer.RasterizeGlyph('H', new RasterOptions { Size = 48 })!;
        var scaled = rasterizer.RasterizeGlyph('H', new RasterOptions { Size = 48, WidthPercent = percent })!;
        double s = percent / 100.0;

        Math.Abs(scaled.Metrics.Advance - (plain.Metrics.Advance * s)).ShouldBeLessThanOrEqualTo(1.0);
        Math.Abs(scaled.Width - (plain.Width * s)).ShouldBeLessThanOrEqualTo(2.0);
        scaled.Height.ShouldBe(plain.Height);
        scaled.Metrics.BearingY.ShouldBe(plain.Metrics.BearingY);

        var metrics = rasterizer.GetGlyphMetrics('H', new RasterOptions { Size = 48, WidthPercent = percent })!.Value;
        (metrics.Width, metrics.Advance, metrics.BearingX).ShouldBe((scaled.Width, scaled.Metrics.Advance, scaled.Metrics.BearingX), "metrics and bitmaps agree");
    }

    [Fact]
    public void WidthPercent_100_IsTheDefaultOutput()
    {
        using var rasterizer = Load();
        var plain = rasterizer.RasterizeGlyph('g', new RasterOptions { Size = 32 })!;
        var same = rasterizer.RasterizeGlyph('g', new RasterOptions { Size = 32, WidthPercent = 100 })!;
        same.BitmapData.ShouldBe(plain.BitmapData);
    }

    [Fact]
    public void ItalicAngle_12_IsTheDefaultSlant_Exactly()
    {
        using var rasterizer = Load();
        var italic = rasterizer.RasterizeGlyph('l', new RasterOptions { Size = 48, Italic = true })!;
        var twelve = rasterizer.RasterizeGlyph('l', new RasterOptions { Size = 48, Italic = true, ItalicAngle = 12f })!;
        twelve.BitmapData.ShouldBe(italic.BitmapData);
        twelve.Metrics.ShouldBe(italic.Metrics);
    }

    [Theory]
    [InlineData(25f)]
    [InlineData(-12f)]
    [InlineData(0f)]
    public void ItalicAngle_SlantsByTheAngle(float degrees)
    {
        using var rasterizer = Load();
        var upright = rasterizer.RasterizeGlyph('l', new RasterOptions { Size = 48 })!;
        var slanted = rasterizer.RasterizeGlyph('l', new RasterOptions { Size = 48, Italic = true, ItalicAngle = degrees })!;

        // The stem's horizontal drift from bottom to top is height x tan(angle).
        double drift = InkCentre(slanted, 1) - InkCentre(slanted, slanted.Height - 2);
        double expected = (slanted.Height - 3) * Math.Tan(degrees * Math.PI / 180);
        Math.Abs(drift - expected).ShouldBeLessThan(1.0, $"drift {drift:0.00} vs {expected:0.00}");
        if (degrees == 0f)
            slanted.BitmapData.ShouldBe(upright.BitmapData);
    }

    [Fact]
    public void WidthPercent_AppliesBeforeTheStyles_SoTheSlantStaysTrue()
    {
        using var rasterizer = Load();
        var condensed = rasterizer.RasterizeGlyph('l', new RasterOptions { Size = 48, Italic = true, WidthPercent = 50 })!;

        double drift = InkCentre(condensed, 1) - InkCentre(condensed, condensed.Height - 2);
        double expected = (condensed.Height - 3) * (0x0366A / 65536.0);
        Math.Abs(drift - expected).ShouldBeLessThan(1.0, "a condensed italic leans 12°, not half of it");
    }

    /// <summary>Coverage-weighted centre column of one row.</summary>
    private static double InkCentre(RasterizedGlyph g, int row)
    {
        double sum = 0, weight = 0;
        for (int x = 0; x < g.Width; x++)
        {
            byte v = g.BitmapData[(row * g.Pitch) + x];
            sum += (x + 0.5) * v;
            weight += v;
        }

        return sum / weight;
    }
}
