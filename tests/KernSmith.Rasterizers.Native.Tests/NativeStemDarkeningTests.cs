using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Thin-stroke darkening in <see cref="HintLevel.Normal"/> grayscale at small sizes: a stem
/// under a pixel wide, which the fitter lands in one column at its true coverage, is lifted
/// toward solid so 6-8 ppem text does not read as mid-grey, while the anti-aliased fringe of
/// a wider stroke (a partial pixel beside a solid one) is left alone. Off from 9 ppem up, and
/// never in <see cref="HintLevel.Default"/>, so default output does not move.
/// </summary>
public class NativeStemDarkeningTests
{
    /// <summary>Roboto's em size for a BMFont cell height.</summary>
    private static float RobotoCell(float cell) => cell * 2048f / (2146f + 555f);

    private static RasterOptions Normal(float size) => new() { Size = size, HintLevel = HintLevel.Normal };

    [Fact]
    public void Roboto8pxCell_ThinStemsReadDark()
    {
        // 6.07 ppem: the 'l' stem is 0.55 px — 55% coverage, a mid-grey bar. Darkened it is
        // at least 75%.
        using var native = Load();
        foreach (char c in "lI")
        {
            var glyph = native.RasterizeGlyph(c, Normal(RobotoCell(8f)))!;
            StemColumnMean(glyph).ShouldBeGreaterThanOrEqualTo(190, $"'{c}'");
        }
    }

    [Fact]
    public void Factor_TapersFromSixToNinePpem()
    {
        ThinStrokeDarkening.Factor(5f).ShouldBe(1.4f, 1e-5f);
        ThinStrokeDarkening.Factor(6f).ShouldBe(1.4f, 1e-5f);
        ThinStrokeDarkening.Factor(7.5f).ShouldBe(1.2f, 1e-5f);
        ThinStrokeDarkening.Factor(9f).ShouldBe(1f, 1e-5f);
        ThinStrokeDarkening.Factor(24f).ShouldBe(1f, 1e-5f);
    }

    [Fact]
    public void Apply_LiftsIsolatedPartialPixels_NotFringesBesideSolidOnes()
    {
        // Row 0: a lone 140 (thin stem) between zeros. Row 1: 140 beside a 255 (a fringe) and
        // the 255 itself. Row 2: a 140 under the row-1 fringe — isolated horizontally, so a
        // thin stroke running vertically, lifted. 3 wide, 3 tall.
        byte[] coverage =
        [
            0, 140, 0,
            255, 140, 0,
            0, 140, 0,
        ];

        ThinStrokeDarkening.Apply(coverage, 3, 3, ppem: 6f);

        coverage[1].ShouldBe((byte)196, "isolated across x");
        coverage[4].ShouldBe((byte)140, "beside a solid pixel, with ink above and below");
        coverage[3].ShouldBe((byte)255);
        coverage[7].ShouldBe((byte)196);
    }

    [Fact]
    public void Apply_IsANoOpAtNinePpem()
    {
        byte[] coverage = [0, 140, 0];
        ThinStrokeDarkening.Apply(coverage, 3, 1, ppem: 9f);
        coverage[1].ShouldBe((byte)140);
    }

    [Fact]
    public void Roboto8pxCell_UnhintedAndHintingOffAreUntouched()
    {
        using var native = Load();
        var unhinted = native.RasterizeGlyph('l', new RasterOptions { Size = RobotoCell(8f), HintLevel = HintLevel.None })!;
        var off = native.RasterizeGlyph('l', new RasterOptions { Size = RobotoCell(8f), EnableHinting = false })!;
        // Unhinted, the 0.55 px stem straddles two columns at well under 75% each.
        StemColumnMean(unhinted).ShouldBeLessThan(190);
        off.BitmapData.ShouldBe(unhinted.BitmapData);
    }

    [Fact]
    public void AboveNinePpem_NothingIsDarkened()
    {
        // 16 px cell = 12.1 ppem: the stem is 1.1 px, one solid column and a fringe; the fringe
        // beside the solid column must stay as rasterized (no fringe pixel is lifted).
        using var native = Load();
        var glyph = native.RasterizeGlyph('l', Normal(RobotoCell(16f)))!;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                byte v = glyph.BitmapData[(y * glyph.Pitch) + x];
                bool besideSolid = (x > 0 && glyph.BitmapData[(y * glyph.Pitch) + x - 1] == 255) || (x + 1 < glyph.Width && glyph.BitmapData[(y * glyph.Pitch) + x + 1] == 255);
                if (v is > 0 and < 255 && besideSolid)
                    v.ShouldBeLessThan((byte)80, $"a fringe pixel at ({x},{y}) was lifted");
            }
        }
    }

    /// <summary>Mean coverage of the glyph's darkest column.</summary>
    private static int StemColumnMean(RasterizedGlyph glyph)
    {
        int best = 0;
        for (int x = 0; x < glyph.Width; x++)
        {
            int sum = 0;
            for (int y = 0; y < glyph.Height; y++)
                sum += glyph.BitmapData[(y * glyph.Pitch) + x];
            best = Math.Max(best, sum / glyph.Height);
        }

        return best;
    }

    private static NativeRasterizer Load()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }
}
