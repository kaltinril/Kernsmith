using KernSmith.Font.Tables;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phases 169/170 × 171 — distance fields are computed from the varied outline, so SDF and
/// MSDF follow the selected instance exactly as coverage does.
/// </summary>
public class NativeVariableDistanceFieldTests
{
    private const string Sample = "AHOgeks&@";

    private static readonly Dictionary<string, float> Black = new() { ["wght"] = 900 };

    [Fact]
    public void Sdf_AtWght900_DiffersFromTheDefaultInstance_AndMatchesFreeTypeAtThatInstance()
    {
        // FreeType at the same instance is the reference: identical boxes and a field within
        // two code values means the varied outline, not the default one, was measured.
        var bytes = TestFonts.RobotoFlexBytes();
        var options = new RasterOptions { Size = 48, Sdf = true, SdfSpread = 6, EnableHinting = false };

        using var plain = Native(bytes, axes: null);
        using var black = Native(bytes, Black);
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(FvarAxes(bytes), Black);

        foreach (char c in Sample)
        {
            var regular = plain.RasterizeGlyph(c, options)!;
            var heavy = black.RasterizeGlyph(c, options)!;
            var reference = freeType.RasterizeGlyph(c, options)!;

            // Heavier: more texels inside the outline than the default instance.
            Inside(heavy.BitmapData, 1).ShouldBeGreaterThan(Inside(regular.BitmapData, 1), $"'{c}' did not get heavier");

            black.GetGlyphMetrics(c, options).ShouldBe(heavy.Metrics, $"'{c}' Measure != Render");
            heavy.Width.ShouldBe(reference.Width, $"'{c}' width");
            heavy.Height.ShouldBe(reference.Height, $"'{c}' height");
            heavy.Metrics.BearingX.ShouldBe(reference.Metrics.BearingX, $"'{c}' bearingX");
            heavy.Metrics.BearingY.ShouldBe(reference.Metrics.BearingY, $"'{c}' bearingY");
            AssertLipschitz(heavy, 6, c);

            if (OverlappingContours.Contains(c))
                continue;

            ImageSimilarity.Ssim(heavy.BitmapData, reference.BitmapData, heavy.Width, heavy.Height)
                .ShouldBeGreaterThan(0.99, $"'{c}' SSIM vs FreeType");
            heavy.BitmapData.Zip(reference.BitmapData, (a, b) => Math.Abs(a - b)).Max()
                .ShouldBeLessThanOrEqualTo(2, $"'{c}' max |diff| vs FreeType");
        }
    }

    /// <summary>RobotoFlex glyphs whose contours overlap (as variable-font outlines often do).</summary>
    private const string OverlappingContours = "Aek&";

    [Fact]
    public void Sdf_AtWght900_OverlappingContours_HaveNoInteriorSeam_WhereFreeTypeHasOne()
    {
        // FreeType's SDF measures to every contour, so the hidden edges where RobotoFlex's
        // contours overlap dig ~128 seams through solid ink; that is why the FreeType comparison
        // above skips these glyphs. Native measures to the union's boundary: a texel whose whole
        // pixel is ink lies at least half a pixel inside (a quarter, allowing the coverage
        // flattener's tolerance), so it must encode well above 128.
        var bytes = TestFonts.RobotoFlexBytes();
        var options = new RasterOptions { Size = 48, Sdf = true, SdfSpread = 6, EnableHinting = false };
        var coverageOptions = options with { Sdf = false };
        int floor = 128 + (int)(0.25 * 128 / 6);

        using var black = Native(bytes, Black);
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(FvarAxes(bytes), Black);

        int freeTypeSeams = 0;
        foreach (char c in OverlappingContours)
        {
            var coverage = black.RasterizeGlyph(c, coverageOptions)!;
            var field = black.RasterizeGlyph(c, options)!;
            var reference = freeType.RasterizeGlyph(c, options)!;
            field.Width.ShouldBe(reference.Width);

            for (int y = 0; y < coverage.Height; y++)
            {
                for (int x = 0; x < coverage.Width; x++)
                {
                    if (coverage.BitmapData[(y * coverage.Width) + x] != 255)
                        continue;

                    int i = ((y + 6) * field.Width) + x + 6;
                    field.BitmapData[i].ShouldBeGreaterThanOrEqualTo((byte)floor, $"'{c}' seam at ({x},{y})");
                    if (Math.Abs(reference.BitmapData[i] - 128) <= 16)
                        freeTypeSeams++;
                }
            }
        }

        freeTypeSeams.ShouldBeGreaterThan(0, "these glyphs no longer overlap; fold them back into the FreeType comparison");
    }

    [Theory]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void Msdf_AtWght900_DiffersFromTheDefaultInstance_AndAgreesWithTheVariedSdf(SdfMode mode)
    {
        var bytes = TestFonts.RobotoFlexBytes();
        var sdfOptions = new RasterOptions { Size = 48, Sdf = true, SdfSpread = 6 };
        var msdfOptions = sdfOptions with { Sdf = false, SdfMode = mode };

        using var plain = Native(bytes, axes: null);
        using var black = Native(bytes, Black);

        foreach (char c in Sample)
        {
            var regular = plain.RasterizeGlyph(c, msdfOptions)!;
            var heavy = black.RasterizeGlyph(c, msdfOptions)!;
            var sdf = black.RasterizeGlyph(c, sdfOptions)!;

            heavy.Format.ShouldBe(PixelFormat.Rgba32);
            heavy.Metrics.ShouldBe(sdf.Metrics, $"'{c}' MSDF and SDF boxes differ");
            heavy.BitmapData.ShouldNotBe(regular.BitmapData, $"'{c}' ignored the axes");

            // The median's inside/outside agrees with the varied SDF wherever the SDF is not on
            // the edge itself; MTSDF alpha is that SDF exactly.
            for (int i = 0; i < sdf.BitmapData.Length; i++)
            {
                int r = heavy.BitmapData[4 * i], g = heavy.BitmapData[(4 * i) + 1], b = heavy.BitmapData[(4 * i) + 2];
                int median = Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b));
                if (Math.Abs(sdf.BitmapData[i] - 128) > 2)
                    (median >= 128).ShouldBe(sdf.BitmapData[i] >= 128, $"'{c}' texel {i % sdf.Width},{i / sdf.Width}");
                if (mode == SdfMode.Mtsdf)
                    heavy.BitmapData[(4 * i) + 3].ShouldBe(sdf.BitmapData[i]);
            }
        }
    }

    [Fact]
    public void Generate_SdfWithVariationAxes_ThroughThePublicPipeline()
    {
        // Core forwards VariationAxes to SetVariationAxes only for backends that report
        // SupportsVariableFonts; the SDF atlas must then differ from the default instance.
        FontGeneratorOptions Options(Dictionary<string, float>? axes) => new()
        {
            Size = 48,
            Characters = CharacterSet.FromChars(Sample),
            Backend = RasterizerBackend.Native,
            Sdf = true,
            SdfSpread = 6,
            VariationAxes = axes,
        };

        var regular = BmFont.Generate(TestFonts.RobotoFlexBytes(), Options(null));
        var heavy = BmFont.Generate(TestFonts.RobotoFlexBytes(), Options(Black));

        heavy.FntText.ShouldContain("sdfSpread=6", Case.Sensitive);
        var hRegular = BmFontReader.ReadText(regular.FntText).Characters.First(ch => ch.Id == 'H');
        var hHeavy = BmFontReader.ReadText(heavy.FntText).Characters.First(ch => ch.Id == 'H');
        hHeavy.Width.ShouldBeGreaterThan(hRegular.Width, "wght 900 'H' should be wider");
        hHeavy.XAdvance.ShouldBeGreaterThan(hRegular.XAdvance);
    }

    private static NativeRasterizer Native(byte[] bytes, Dictionary<string, float>? axes)
    {
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        if (axes is not null)
            ((IRasterizer)native).SetVariationAxes(FvarAxes(bytes), axes);
        return native;
    }

    private static IReadOnlyList<VariationAxis> FvarAxes(byte[] bytes) =>
        BmFont.ReadFontInfo(bytes).VariationAxes ?? throw new InvalidOperationException("not a variable font");

    private static int Inside(byte[] pixels, int stride)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += stride)
        {
            if (pixels[i] >= 128)
                count++;
        }

        return count;
    }

    /// <summary>A true distance field changes by at most 128/spread (+1 for the floor) per texel.</summary>
    private static void AssertLipschitz(RasterizedGlyph glyph, int spread, char c)
    {
        int limit = (int)Math.Ceiling(128.0 / spread) + 1;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                int v = glyph.BitmapData[(y * glyph.Width) + x];
                if (x + 1 < glyph.Width)
                    Math.Abs(v - glyph.BitmapData[(y * glyph.Width) + x + 1]).ShouldBeLessThanOrEqualTo(limit, $"'{c}' ({x},{y})");
                if (y + 1 < glyph.Height)
                    Math.Abs(v - glyph.BitmapData[((y + 1) * glyph.Width) + x]).ShouldBeLessThanOrEqualTo(limit, $"'{c}' ({x},{y})");
            }
        }
    }
}
