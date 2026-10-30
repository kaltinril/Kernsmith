using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phases 169/170 × 172 — what a distance-field request does to color glyphs. Single-channel
/// SDF: glyphs with <c>COLR</c> layers still render as color and only glyphs without layers
/// become distance fields, as FreeType does. MSDF/MTSDF: rejected on a face with color glyphs,
/// because color layers in an MSDF atlas would read as distances.
/// </summary>
public class NativeColorDistanceFieldTests
{
    private const string Sample = "ABGOQRSWagmos&08?";

    [Fact]
    public void ColorAndSdf_ColorLayersWin_ExactlyAsWithoutSdf()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.BungeeColorBytes());
        var color = new RasterOptions { Size = 48, ColorFont = true, EnableHinting = false };
        var colorSdf = color with { Sdf = true, SdfSpread = 6 };

        foreach (char c in Sample)
        {
            var expected = native.RasterizeGlyph(c, color)!;
            var actual = native.RasterizeGlyph(c, colorSdf)!;

            actual.Format.ShouldBe(PixelFormat.Rgba32, $"'{c}'");
            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}'");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{c}'");
            native.GetGlyphMetrics(c, colorSdf).ShouldBe(actual.Metrics, $"'{c}' Measure != Render");
        }
    }

    [Fact]
    public void ColorAndSdf_ColrV1GlyphsStayColor_ExactlyAsWithoutSdf()
    {
        // Version 1 paint graphs (ReemKufiInk) win over SDF just as version 0 layers do.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.ReemKufiInkBytes());
        var color = new RasterOptions { Size = 48, ColorFont = true, EnableHinting = false };
        var colorSdf = color with { Sdf = true, SdfSpread = 6 };

        int colored = 0;
        foreach (char c in "١٤؟ءَّ۵")
        {
            var expected = native.RasterizeGlyph(c, color)!;
            var actual = native.RasterizeGlyph(c, colorSdf)!;

            actual.Format.ShouldBe(PixelFormat.Rgba32, $"U+{(int)c:X4}");
            actual.Metrics.ShouldBe(expected.Metrics, $"U+{(int)c:X4}");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"U+{(int)c:X4}");
            native.GetGlyphMetrics(c, colorSdf).ShouldBe(actual.Metrics, $"U+{(int)c:X4} Measure != Render");
            for (int i = 0; i < actual.BitmapData.Length; i += 4)
            {
                if (actual.BitmapData[i + 3] == 255 && (actual.BitmapData[i] | actual.BitmapData[i + 1] | actual.BitmapData[i + 2]) != 0)
                    colored++;
            }
        }

        colored.ShouldBeGreaterThan(100, "the version 1 glyphs should show the palette's non-black entries");
    }

    [Fact]
    public void ColorAndSdf_FreeTypeAlsoRendersColorGlyphsInColor()
    {
        // Pins the reference behaviour Native follows: FreeType draws COLR glyphs as color
        // bitmaps even when SDF is requested.
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(TestFonts.BungeeColorBytes());
        var options = new RasterOptions { Size = 48, ColorFont = true, Sdf = true, SdfSpread = 6, EnableHinting = false };

        foreach (char c in Sample)
            freeType.RasterizeGlyph(c, options)!.Format.ShouldBe(PixelFormat.Rgba32, $"'{c}'");
    }

    [Fact]
    public void ColorAndSdf_OnAFaceWithoutColorGlyphs_IsPlainSdf()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var sdf = new RasterOptions { Size = 32, Sdf = true, SdfSpread = 6 };

        foreach (char c in "AgO&")
        {
            var expected = native.RasterizeGlyph(c, sdf)!;
            var actual = native.RasterizeGlyph(c, sdf with { ColorFont = true })!;

            actual.Format.ShouldBe(PixelFormat.Grayscale8);
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{c}'");
        }
    }

    [Theory]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void ColorAndMsdf_IsRejected_OnAFaceWithColorGlyphs(SdfMode mode)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.BungeeColorBytes());
        var options = new RasterOptions { Size = 48, ColorFont = true, SdfMode = mode };

        Should.Throw<NotSupportedException>(() => native.RasterizeGlyph('A', options)).Message.ShouldContain("MSDF", Case.Sensitive);
        Should.Throw<NotSupportedException>(() => native.GetGlyphMetrics('A', options));

        // Without color, the same face renders as a multi-channel field.
        native.RasterizeGlyph('A', options with { ColorFont = false })!.Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public void ColorAndMsdf_OnAFaceWithoutColorGlyphs_IsPlainMsdf()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var msdf = new RasterOptions { Size = 32, SdfMode = SdfMode.Msdf };

        native.RasterizeGlyph('A', msdf with { ColorFont = true })!.BitmapData
            .ShouldBe(native.RasterizeGlyph('A', msdf)!.BitmapData);
    }
}
