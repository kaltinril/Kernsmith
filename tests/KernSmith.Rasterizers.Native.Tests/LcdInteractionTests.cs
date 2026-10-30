using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 173 — how LCD subpixel rendering combines with color layers (Phase 172) and synthetic
/// bold/italic (Phase 167).
/// </summary>
public class LcdInteractionTests
{
    // ---- Color layers --------------------------------------------------------------------------

    [Fact]
    public void ColorGlyphs_IgnoreLcd_AndRenderExactlyAsGrayscaleColor()
    {
        // FreeType's behaviour, pinned below: a COLR glyph requested with ColorFont renders its
        // color layers whatever the anti-alias mode, so Lcd changes nothing for it. Native's
        // layers are coverage-composited the same way, and LCD applies only to glyphs without
        // layers.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.BungeeColorBytes());

        int layered = 0;
        for (int codepoint = 0x21; codepoint < 0x7F; codepoint++)
        {
            var gray = native.RasterizeGlyph(codepoint, Options(AntiAliasMode.Grayscale, color: true));
            var lcd = native.RasterizeGlyph(codepoint, Options(AntiAliasMode.Lcd, color: true));
            if (gray is null)
            {
                lcd.ShouldBeNull();
                continue;
            }

            lcd.ShouldNotBeNull();
            lcd!.Format.ShouldBe(gray.Format, $"U+{codepoint:X4}");
            lcd.Metrics.ShouldBe(gray.Metrics, $"U+{codepoint:X4}");
            lcd.BitmapData.ShouldBe(gray.BitmapData, $"U+{codepoint:X4}");
            native.GetGlyphMetrics(codepoint, Options(AntiAliasMode.Lcd, color: true)).ShouldBe(lcd.Metrics);
            if (gray.Format == PixelFormat.Rgba32)
                layered++;
        }

        layered.ShouldBeGreaterThan(0, "Bungee Color must exercise layered glyphs for this test to mean anything");
    }

    [Fact]
    public void ColrV1Glyphs_IgnoreLcd_AndRenderExactlyAsGrayscaleColor()
    {
        // Version 1 paint graphs (ReemKufiInk) follow the same rule as version 0 layers.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.ReemKufiInkBytes());

        foreach (char c in "١٤؟ءَّ۵")
        {
            var gray = native.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale, color: true))!;
            var lcd = native.RasterizeGlyph(c, Options(AntiAliasMode.Lcd, color: true))!;

            gray.Format.ShouldBe(PixelFormat.Rgba32, $"U+{(int)c:X4}");
            lcd.Format.ShouldBe(gray.Format, $"U+{(int)c:X4}");
            lcd.Metrics.ShouldBe(gray.Metrics, $"U+{(int)c:X4}");
            lcd.BitmapData.ShouldBe(gray.BitmapData, $"U+{(int)c:X4}");
            native.GetGlyphMetrics(c, Options(AntiAliasMode.Lcd, color: true)).ShouldBe(lcd.Metrics, $"U+{(int)c:X4}");
        }
    }

    [Fact]
    public void FreeType_AlsoRendersColorGlyphsIdenticallyUnderLcdAndGrayscale()
    {
        // The reference behaviour the test above matches.
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(TestFonts.BungeeColorBytes());

        var gray = freeType.RasterizeGlyph('A', Options(AntiAliasMode.Grayscale, color: true))!;
        var lcd = freeType.RasterizeGlyph('A', Options(AntiAliasMode.Lcd, color: true))!;

        gray.Format.ShouldBe(PixelFormat.Rgba32);
        lcd.Format.ShouldBe(gray.Format);
        lcd.Width.ShouldBe(gray.Width);
        lcd.BitmapData.ShouldBe(gray.BitmapData);
    }

    [Fact]
    public void WithoutColorLayers_ColorFontStillRendersLcd()
    {
        // Bungee Color without ColorFont, and Roboto (no COLR) with it: both are plain LCD.
        using var bungee = new NativeRasterizer();
        bungee.LoadFont(TestFonts.BungeeColorBytes());
        var plain = bungee.RasterizeGlyph('A', Options(AntiAliasMode.Lcd, color: false))!;
        plain.Format.ShouldBe(PixelFormat.Rgba32);
        HasDistinctChannels(plain).ShouldBeTrue("Bungee without ColorFont must render LCD subpixels");

        using var roboto = new NativeRasterizer();
        roboto.LoadFont(TestFonts.RobotoRegularBytes());
        var withColor = roboto.RasterizeGlyph('l', Options(AntiAliasMode.Lcd, color: true))!;
        var withoutColor = roboto.RasterizeGlyph('l', Options(AntiAliasMode.Lcd, color: false))!;
        withColor.BitmapData.ShouldBe(withoutColor.BitmapData);
        withColor.Metrics.ShouldBe(withoutColor.Metrics);
        HasDistinctChannels(withColor).ShouldBeTrue();
    }

    // ---- Synthetic styles ------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SyntheticStyles_ApplyToLcdGlyphs(bool bold, bool italic)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var face = native.Face!;
        var pipeline = new GlyphPipeline(OutlineDecoders.ForFace(face)!, face.Head.UnitsPerEm);
        var styled = Options(AntiAliasMode.Lcd, color: false) with { Bold = bold, Italic = italic };
        var regular = Options(AntiAliasMode.Lcd, color: false);

        foreach (char character in "lHOa")
        {
            var actual = native.RasterizeGlyph(character, styled)!;
            var unstyled = native.RasterizeGlyph(character, regular)!;

            // The LCD renderer draws the transformed outline: the same as rendering the glyph
            // prepared through the request's transform list.
            int glyphIndex = native.GetGlyphIndex(character);
            var prepared = pipeline.Prepare(glyphIndex, styled, GlyphTransforms.For(styled, face.Head.UnitsPerEm));
            var expected = LcdRenderer.Instance.Render(character, glyphIndex, prepared, styled);

            actual.Format.ShouldBe(PixelFormat.Rgba32);
            actual.Metrics.ShouldBe(expected.Metrics, $"'{character}'");
            actual.BitmapData.ShouldBe(expected.BitmapData, $"'{character}'");
            actual.BitmapData.ShouldNotBe(unstyled.BitmapData, $"'{character}' is unstyled");
            native.GetGlyphMetrics(character, styled).ShouldBe(actual.Metrics, $"'{character}'");

            if (bold)
                Ink(actual).ShouldBeGreaterThan(Ink(unstyled), $"'{character}' bold must add ink");
            // A 12° shear across a full-height stem moves its top by several pixels.
            if (italic && !bold && character is 'l' or 'H')
                actual.Width.ShouldBeGreaterThan(unstyled.Width, $"'{character}' slant must widen it");
        }
    }

    [Fact]
    public void SyntheticStyles_ApplyToColorLayersUnderLcdToo()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.BungeeColorBytes());

        var bold = native.RasterizeGlyph('A', Options(AntiAliasMode.Lcd, color: true) with { Bold = true })!;
        var boldGray = native.RasterizeGlyph('A', Options(AntiAliasMode.Grayscale, color: true) with { Bold = true })!;
        var regular = native.RasterizeGlyph('A', Options(AntiAliasMode.Lcd, color: true))!;

        bold.BitmapData.ShouldBe(boldGray.BitmapData);
        bold.BitmapData.ShouldNotBe(regular.BitmapData);
    }

    private static RasterOptions Options(AntiAliasMode mode, bool color) =>
        new() { Size = 24, AntiAlias = mode, ColorFont = color, EnableHinting = false };

    private static bool HasDistinctChannels(RasterizedGlyph glyph)
    {
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i] != glyph.BitmapData[i + 1] || glyph.BitmapData[i + 1] != glyph.BitmapData[i + 2])
                return true;
        }

        return false;
    }

    private static long Ink(RasterizedGlyph glyph)
    {
        long sum = 0;
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
            sum += glyph.BitmapData[i] + glyph.BitmapData[i + 1] + glyph.BitmapData[i + 2];
        return sum;
    }
}
