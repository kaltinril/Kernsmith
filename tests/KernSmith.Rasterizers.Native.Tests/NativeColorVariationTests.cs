using System.Globalization;
using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColorTables;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phases 171 + 172 together: <c>COLR</c> layer glyphs run through the same pipeline as plain
/// glyphs, so a variable font's instance moves its color layers too, and setting axes on a
/// color font without <c>fvar</c> changes nothing.
/// </summary>
public class NativeColorVariationTests
{
    private const uint Red = 0xFF0000FFu;

    private static readonly RasterOptions Color48 = new() { Size = 48, ColorFont = true };
    private static readonly RasterOptions Plain48 = new() { Size = 48 };

    [Fact]
    public void VariableColorFont_LayersFollowTheSelectedInstance()
    {
        // No committed fixture is both variable and COLR, so RobotoFlex gets a hand-built
        // COLR/CPAL pair: 'O' is drawn from a single red layer that is the 'O' glyph itself.
        var flex = TestFonts.RobotoFlexBytes();
        int o = NativeFontFace.Load(flex).GetGlyphIndex('O');
        var font = SyntheticFonts.WithTables(flex, new Dictionary<string, byte[]?>
        {
            ["COLR"] = Colr((o, [new(o, 0)])),
            ["CPAL"] = Cpal([[Red]]),
        });

        using var regular = Load(font);
        using var black = Load(font);
        black.SetVariationAxes(FvarAxes(font), Axes("wght=1000"));

        var colorRegular = regular.RasterizeGlyph('O', Color48)!;
        var colorBlack = black.RasterizeGlyph('O', Color48)!;
        var plainBlack = black.RasterizeGlyph('O', Plain48)!;

        colorBlack.Format.ShouldBe(PixelFormat.Rgba32, "the variable instance still renders in color");
        colorBlack.Metrics.ShouldBe(black.GetGlyphMetrics('O', Color48)!.Value);

        // The single opaque layer is the glyph's own outline, so its alpha must be exactly the
        // plain coverage at the same instance — i.e. the layer was decoded with gvar deltas.
        colorBlack.Metrics.ShouldBe(plainBlack.Metrics);
        Alpha(colorBlack).ShouldBe(plainBlack.BitmapData);
        RedOnly(colorBlack).ShouldBeTrue("every covered pixel is the palette's red");

        // ...and the instance really is different from the default one.
        Alpha(colorRegular).ShouldNotBe(Alpha(colorBlack));
    }

    [Fact]
    public void NonVariableColorFont_WithAxesSet_IsANoOp()
    {
        var bungee = TestFonts.BungeeColorBytes();
        var axes = FvarAxes(TestFonts.RobotoFlexBytes());

        using var untouched = Load(bungee);
        using var withAxes = Load(bungee);
        withAxes.SetVariationAxes(axes, Axes("wght=1000,wdth=25"));

        foreach (char c in "ABG08")
        {
            var expected = untouched.RasterizeGlyph(c, Color48)!;
            var actual = withAxes.RasterizeGlyph(c, Color48)!;

            actual.Format.ShouldBe(PixelFormat.Rgba32, $"'{c}' still renders in color");
            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}' metrics");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{c}' pixels");
        }

        withAxes.GetFontMetrics(Color48).ShouldBe(untouched.GetFontMetrics(Color48));
    }

    private static NativeRasterizer Load(byte[] font)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    private static IReadOnlyList<VariationAxis> FvarAxes(byte[] bytes) =>
        BmFont.ReadFontInfo(bytes).VariationAxes ?? throw new InvalidOperationException("not a variable font");

    private static Dictionary<string, float> Axes(string instance) =>
        instance.Split(',').Select(p => p.Split('=')).ToDictionary(
            p => p[0], p => float.Parse(p[1], CultureInfo.InvariantCulture));

    private static byte[] Alpha(RasterizedGlyph glyph)
    {
        var alpha = new byte[glyph.Width * glyph.Height];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = glyph.BitmapData[(i * 4) + 3];
        return alpha;
    }

    private static bool RedOnly(RasterizedGlyph glyph)
    {
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i + 3] == 0)
                continue;
            if (glyph.BitmapData[i] != 255 || glyph.BitmapData[i + 1] != 0 || glyph.BitmapData[i + 2] != 0)
                return false;
        }

        return true;
    }
}
