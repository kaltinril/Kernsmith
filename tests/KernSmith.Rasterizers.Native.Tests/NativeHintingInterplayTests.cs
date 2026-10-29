using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColrV1;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 174 hinting and mono dropout against the other Native features: CFF outlines, distance
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
