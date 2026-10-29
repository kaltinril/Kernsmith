using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColrV1;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 174 hinting and mono dropout against the other Native features: CFF outlines, CFF2 instances, distance
/// fields, LCD and COLR color glyphs. Hinting is a <see cref="AntiAliasMode.Light"/>-only step
/// (with <see cref="RasterOptions.EnableHinting"/>) on the outline every renderer prepares, so it
/// reaches every outline format and every color glyph's clips and layers, and nothing else.
/// </summary>
public class NativeHintingInterplayTests
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const uint Red = 0xFF0000FFu;

    private static readonly NativeFontFace Roboto = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
    private static readonly int H = Roboto.GetGlyphIndex('H');

    private static RasterOptions Light(float size, bool hint = true) =>
        new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = hint };

    // ---- CFF -------------------------------------------------------------------------------

    [Fact]
    public void Cff_LightHinting_SnapsTheXHeightRow()
    {
        // Source Serif 4's top 'x' serifs are flat on the x-height. Unhinted at 14px the x-height
        // falls mid-pixel and the serifs smear into a soft top row (peak ~165); hinted, the
        // x-height lands on a pixel boundary and the serifs fill their row. (The diagonals make
        // the row-against-row solidity of NativeHintingTests meaningless for 'x'.)
        using var native = Load(TestFonts.SourceSerifOtfBytes());

        var hinted = native.RasterizeGlyph('x', Light(14f))!;
        var unhinted = native.RasterizeGlyph('x', Light(14f, hint: false))!;

        TopRowPeak(unhinted).ShouldBeLessThan(200, "unhinted x-height row should be soft, or this test proves nothing");
        TopRowPeak(hinted).ShouldBeGreaterThan(220);
        hinted.Metrics.Advance.ShouldBe(unhinted.Metrics.Advance);
        native.GetGlyphMetrics('x', Light(14f))!.Value.ShouldBe(hinted.Metrics);
    }

    [Fact]
    public void Cff_MonoDropout_KeepsExtraLightStems()
    {
        // Source Code's variable default instance is ExtraLight: its stems are about half a pixel
        // at 16px and thresholding alone erased 'L' and 'T'. Dropout control keeps every stem row.
        using var native = Load(TestFonts.SourceCodeVariableOtfBytes());
        var options = new RasterOptions { Size = 16f, AntiAlias = AntiAliasMode.None };
        var failures = new StringBuilder();

        foreach (char c in "LT")
        {
            var glyph = native.RasterizeGlyph(c, options)!;
            glyph.Width.ShouldBeGreaterThan(0, $"'{c}'");
            for (int y = 0; y < glyph.Height; y++)
            {
                bool lit = false;
                for (int x = 0; x < glyph.Width; x++)
                    lit |= glyph.BitmapData[(y * glyph.Pitch) + x] != 0;
                if (!lit)
                    failures.Append(CultureInfo.InvariantCulture, $"'{c}' row {y} of {glyph.Height} empty\n");
            }
        }

        failures.ToString().ShouldBeEmpty();
    }

    // ---- CFF2 variable instances -------------------------------------------------------------

    [Fact]
    public void Cff2Instance_LightHinting_MeasuresTheVariedInstance()
    {
        // The hinter measures blue zones and stems from the outlines the decoder returns, cached
        // per instance. Source Code's x-height is 478 units at the ExtraLight default and 500 at
        // wght 900, and its stems grow fivefold. At 15px the black x-height (7.5px) is off the grid
        // and the default's zone does not reach it, so a rasterizer that hinted the default first
        // must, after switching to wght 900, render exactly what a fresh wght 900 rasterizer does,
        // with the x-height row snapped solid. (At 14px the black x-height is exactly 7px, so
        // default-instance zones would go unnoticed there.)
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var black = new Dictionary<string, float> { ["wght"] = 900 };
        using var fresh = Load(bytes);
        fresh.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), black);
        using var switched = Load(bytes);
        var light = switched.RasterizeGlyph('x', Light(15f))!;
        switched.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), black);

        var expected = fresh.RasterizeGlyph('x', Light(15f))!;
        var actual = switched.RasterizeGlyph('x', Light(15f))!;
        var unhinted = fresh.RasterizeGlyph('x', Light(15f, hint: false))!;

        expected.BitmapData.ShouldNotBe(light.BitmapData, "wght 900 must not render the default instance");
        expected.BitmapData.ShouldNotBe(unhinted.BitmapData, "hinting must move 'x' at 15px");
        TopRowPeak(unhinted).ShouldBeLessThan(200, "the unhinted x-height row should be soft, or this test proves nothing");
        TopRowPeak(expected).ShouldBeGreaterThan(220);
        actual.Metrics.ShouldBe(expected.Metrics);
        actual.BitmapData.ShouldBe(expected.BitmapData);
        expected.Metrics.Advance.ShouldBe(unhinted.Metrics.Advance);
    }

    [Fact]
    public void Cff2Instance_MonoDropout_RendersTheBlackInstance()
    {
        // None (thresholded, with dropout control) at wght 900: the ink of 'x', 'L' and 'T' sits
        // where FreeType's mono ink does, with no gap between its first and last rows, and covers
        // more pixels than the ExtraLight default.
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var black = new Dictionary<string, float> { ["wght"] = 900 };
        using var thin = Load(bytes);
        using var native = Load(bytes);
        native.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), black);
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), black);
        var options = new RasterOptions { Size = 16f, AntiAlias = AntiAliasMode.None };
        var failures = new StringBuilder();

        foreach (char c in "xLT")
        {
            var glyph = native.RasterizeGlyph(c, options)!;
            var ink = NativeVariableFontTests.InkBounds(glyph).ShouldNotBeNull($"'{c}'");
            var expected = NativeVariableFontTests.InkBounds(freeType.RasterizeGlyph(c, options)!)!.Value;
            if (Math.Abs(ink.Left - expected.Left) > 1 || Math.Abs(ink.Top - expected.Top) > 1
                || Math.Abs(ink.Right - expected.Right) > 1 || Math.Abs(ink.Bottom - expected.Bottom) > 1)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' ink {ink} vs FreeType {expected}\n");

            for (int y = glyph.Metrics.BearingY - ink.Top; y < glyph.Metrics.BearingY - ink.Bottom; y++)
            {
                bool lit = false;
                for (int x = 0; x < glyph.Width; x++)
                    lit |= glyph.BitmapData[(y * glyph.Pitch) + x] != 0;
                if (!lit)
                    failures.Append(CultureInfo.InvariantCulture, $"'{c}' row {y} of {glyph.Height} empty\n");
            }

            Lit(glyph).ShouldBeGreaterThan(Lit(thin.RasterizeGlyph(c, options)!), $"'{c}' at wght 900 should cover more than at 200");
        }

        failures.ToString().ShouldBeEmpty();
    }

    // ---- Distance fields and LCD -------------------------------------------------------------

    [Theory]
    [InlineData(SdfMode.Sdf)]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void DistanceFields_AreNeverHinted(SdfMode mode)
    {
        // A distance field is resampled at any scale, so snapping its outline to one pixel grid
        // would only distort it: Light + hinting renders exactly the Grayscale field.
        using var native = Load(TestFonts.RobotoRegularBytes());
        var gray = new RasterOptions { Size = 14f, SdfMode = mode, EnableHinting = true };
        var light = gray with { AntiAlias = AntiAliasMode.Light };

        foreach (char c in "Hxzp")
        {
            var expected = native.RasterizeGlyph(c, gray)!;
            var actual = native.RasterizeGlyph(c, light)!;
            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}' {mode}");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{c}' {mode}");
        }
    }

    [Fact]
    public void Lcd_IsNotHinted()
    {
        // Native's hinting is light hinting, which is AntiAliasMode.Light; LCD is its own mode
        // and renders the unhinted outline whatever EnableHinting says.
        using var native = Load(TestFonts.RobotoRegularBytes());
        var hinted = new RasterOptions { Size = 14f, AntiAlias = AntiAliasMode.Lcd, EnableHinting = true };
        var unhinted = hinted with { EnableHinting = false };

        foreach (char c in "Hxzp")
        {
            var expected = native.RasterizeGlyph(c, unhinted)!;
            var actual = native.RasterizeGlyph(c, hinted)!;
            actual.Format.ShouldBe(PixelFormat.Rgba32);
            actual.Metrics.ShouldBe(expected.Metrics, $"'{c}'");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{c}'");
        }
    }

    // ---- Color glyphs ------------------------------------------------------------------------

    [Fact]
    public void ColrV1_GlyphClip_IsHintedUnderLight_LikePlainCoverage()
    {
        // Version 1 glyph clips are prepared through the same pipeline as version 0 layers, so
        // both are hinted under Light: a solid fill clipped to 'H' has exactly the alpha of the
        // hinted plain 'H'.
        using var native = Load(SyntheticFonts.RobotoWith(
            ("COLR", Colr(H, new Glyph(H, new Solid(0)))),
            ("CPAL", SyntheticColorTables.Cpal([[Red]]))));
        var color = Light(16f) with { ColorFont = true };

        var glyph = native.RasterizeGlyph('H', color)!;
        var plain = native.RasterizeGlyph('H', Light(16f))!;

        plain.BitmapData.ShouldNotBe(native.RasterizeGlyph('H', Light(16f, hint: false))!.BitmapData, "hinting must move 'H' at 16px");
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Metrics.ShouldBe(plain.Metrics);
        Alpha(glyph).ShouldBe(plain.BitmapData);
        native.GetGlyphMetrics('H', color)!.Value.ShouldBe(glyph.Metrics);
    }

    [Fact]
    public void ColrV0_Layer_IsHintedUnderLight_LikePlainCoverage()
    {
        using var native = Load(SyntheticFonts.RobotoWith(
            ("COLR", SyntheticColorTables.Colr((H, [new(H, 0)]))),
            ("CPAL", SyntheticColorTables.Cpal([[Red]]))));
        var color = Light(16f) with { ColorFont = true };

        var glyph = native.RasterizeGlyph('H', color)!;
        var plain = native.RasterizeGlyph('H', Light(16f))!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Metrics.ShouldBe(plain.Metrics);
        Alpha(glyph).ShouldBe(plain.BitmapData);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(7f)]
    [InlineData(8f)]
    public void ColrV1_GlyphClip_UnderNone_HasTheDropoutControlOfPlainCoverage(float size)
    {
        // Dropout control sees thin horizontal strokes from vertical scanlines, which needs the
        // outline's horizontal edges; a version 1 glyph clip must keep them as plain coverage does.
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None };
        var failures = new StringBuilder();

        foreach (char c in Letters)
        {
            int glyphIndex = Roboto.GetGlyphIndex(c);
            using var native = Load(SyntheticFonts.RobotoWith(
                ("COLR", Colr(glyphIndex, new Glyph(glyphIndex, new Solid(0)))),
                ("CPAL", SyntheticColorTables.Cpal([[Red]]))));

            var glyph = native.RasterizeGlyph(c, options with { ColorFont = true })!;
            var plain = native.RasterizeGlyph(c, options)!;
            if (glyph.Metrics != plain.Metrics || !Alpha(glyph).SequenceEqual(plain.BitmapData))
                failures.Append(CultureInfo.InvariantCulture, $"'{c}'\n");
        }

        failures.ToString().ShouldBeEmpty($"version 1 alpha differs from plain mono coverage at {size}px");
    }

    private static int TopRowPeak(RasterizedGlyph glyph)
    {
        int peak = 0;
        for (int x = 0; x < glyph.Width; x++)
            peak = Math.Max(peak, glyph.BitmapData[x]);
        return peak;
    }

    private static int Lit(RasterizedGlyph glyph)
    {
        int lit = 0;
        for (int y = 0; y < glyph.Height; y++)
            for (int x = 0; x < glyph.Width; x++)
                lit += glyph.BitmapData[(y * glyph.Pitch) + x] != 0 ? 1 : 0;
        return lit;
    }

    private static NativeRasterizer Load(byte[] font)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    private static byte[] Alpha(RasterizedGlyph glyph)
    {
        var alpha = new byte[glyph.Width * glyph.Height];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = glyph.BitmapData[(i * 4) + 3];
        return alpha;
    }
}
