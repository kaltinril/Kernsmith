using KernSmith.Atlas;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Native's half of the fill color and RGBA custom glyph guards: channel packing cannot store an
/// RGBA glyph, and a fill cannot tint real LCD pixels. Each is rejected by generation and by a
/// size query, and every neighbor that renders correctly keeps working.
/// </summary>
public class ChannelLcdFillGuardTests
{
    private static FontGeneratorOptions Options(AntiAliasMode antiAlias = AntiAliasMode.Grayscale, bool packed = false) => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABg i"),
        Backend = RasterizerBackend.Native,
        AntiAlias = antiAlias,
        ChannelPacking = packed,
    };

    private static FontGeneratorOptions Tinted(FontGeneratorOptions options, byte r = 255, byte g = 128, byte b = 0, byte a = 255)
    {
        options.FillColorR = r;
        options.FillColorG = g;
        options.FillColorB = b;
        options.FillColorA = a;
        return options;
    }

    private static CustomGlyph RgbaCustom() => new(4, 4, Enumerable.Repeat((byte)200, 64).ToArray());

    private static CustomGlyph GrayCustom() =>
        new(4, 4, Enumerable.Repeat((byte)200, 16).ToArray(), PixelFormat.Grayscale8);

    /// <summary>A region covering a page rendered larger than the glyphs under test, so they fit.</summary>
    private static AtlasTargetRegion Region()
    {
        var page = BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = 96,
            Characters = CharacterSet.FromChars("ABg i"),
            Backend = RasterizerBackend.Native,
        }).Pages[0];

        return new AtlasTargetRegion { SourcePngData = page.ToPng(), X = 0, Y = 0, Width = page.Width, Height = page.Height };
    }

    private static ChannelConfig CustomChannels() => new(
        Alpha: ChannelContent.Glyph,
        Red: ChannelContent.Glyph,
        Green: ChannelContent.Glyph,
        Blue: ChannelContent.Zero);

    private static int NonZeroBytes(BmFontResult result) =>
        result.Pages.Sum(p => p.PixelData.Count(b => b != 0));

    private static void ShouldReject(byte[] font, FontGeneratorOptions options, params string[] messageParts)
    {
        BmFontResult? result = null;
        var generated = Record.Exception(() => result = BmFont.Generate(font, options));
        if (generated is null)
            Assert.Fail($"Expected InvalidOperationException, but generation wrote an atlas with {NonZeroBytes(result!)} nonzero bytes.");

        AtlasSizeInfo? size = null;
        var queried = Record.Exception(() => size = BmFont.QueryAtlasSize(font, options));
        if (queried is null)
            Assert.Fail($"Expected InvalidOperationException, but QueryAtlasSize reported {size!.GlyphCount} glyphs.");

        foreach (var ex in new[] { generated, queried })
        {
            var invalid = ex.ShouldBeOfType<InvalidOperationException>();
            foreach (var part in messageParts)
                invalid.Message.ShouldContain(part, Case.Sensitive);
        }
    }

    private static BmFontResult ShouldGenerate(byte[] font, FontGeneratorOptions options)
    {
        var result = BmFont.Generate(font, options);
        NonZeroBytes(result).ShouldBeGreaterThan(0);
        BmFont.QueryAtlasSize(font, options).GlyphCount.ShouldBeGreaterThan(0);
        return result;
    }

    [Theory]
    [InlineData(254, 255, 255, 255)]
    [InlineData(255, 254, 255, 255)]
    [InlineData(255, 255, 254, 255)]
    [InlineData(255, 255, 255, 254)]
    public void ChannelPacking_WithAnyNonDefaultFill_IsRejected(byte r, byte g, byte b, byte a)
    {
        ShouldReject(TestFonts.RobotoRegularBytes(), Tinted(Options(packed: true), r, g, b, a), "Channel packing", "fill color");
    }

    [Fact]
    public void ChannelPacking_SingleChannelSdfWithFill_IsRejected()
    {
        var options = Tinted(Options(packed: true));
        options.Sdf = true;

        ShouldReject(TestFonts.RobotoRegularBytes(), options, "Channel packing", "fill color");
    }

    [Fact]
    public void ChannelPacking_WithWhiteFill_KeepsPacking()
    {
        ShouldGenerate(TestFonts.RobotoRegularBytes(), Tinted(Options(packed: true), 255, 255, 255, 255));
    }

    [Fact]
    public void ChannelPacking_FillWithCustomChannels_NeverReachesThePacker()
    {
        var options = Tinted(Options(packed: true));
        options.Channels = CustomChannels();

        ShouldGenerate(TestFonts.RobotoRegularBytes(), options);
    }

    [Fact]
    public void ChannelPacking_FillWithTargetRegion_NeverReachesThePacker()
    {
        var options = Tinted(Options(packed: true));
        options.TargetRegion = Region();

        ShouldGenerate(TestFonts.RobotoRegularBytes(), options);
    }

    [Fact]
    public void ChannelPacking_WithRgbaCustomGlyph_IsRejected()
    {
        var options = Options(packed: true);
        options.CustomGlyphs = new() { ['A'] = RgbaCustom() };

        ShouldReject(TestFonts.RobotoRegularBytes(), options, "Channel packing", "custom glyph", "U+0041");
    }

    [Fact]
    public void ChannelPacking_WithGrayscaleCustomGlyph_PacksItsPixels()
    {
        var options = Options(packed: true);
        options.Characters = CharacterSet.FromChars("A");
        options.CustomGlyphs = new() { ['A'] = GrayCustom() };

        var result = ShouldGenerate(TestFonts.RobotoRegularBytes(), options);

        result.Pages[0].PixelData.Count(b => b == 200).ShouldBe(16);
    }

    [Fact]
    public void ChannelPacking_RgbaCustomGlyphWithCustomChannels_NeverReachesThePacker()
    {
        var options = Options(packed: true);
        options.Channels = CustomChannels();
        options.CustomGlyphs = new() { ['A'] = RgbaCustom() };

        ShouldGenerate(TestFonts.RobotoRegularBytes(), options);
    }

    [Fact]
    public void ChannelPacking_RgbaCustomGlyphWithTargetRegion_NeverReachesThePacker()
    {
        var options = Options(packed: true);
        options.TargetRegion = Region();
        options.CustomGlyphs = new() { ['A'] = RgbaCustom() };

        ShouldGenerate(TestFonts.RobotoRegularBytes(), options);
    }

    [Theory]
    [InlineData(200, 255, 255, 255)]
    [InlineData(255, 255, 255, 128)]
    public void Lcd_WithFill_IsRejected(byte r, byte g, byte b, byte a)
    {
        ShouldReject(TestFonts.RobotoRegularBytes(), Tinted(Options(AntiAliasMode.Lcd), r, g, b, a), "fill color", "LCD");
    }

    [Fact]
    public void Lcd_WithWhiteFill_KeepsLcd()
    {
        ShouldGenerate(TestFonts.RobotoRegularBytes(), Tinted(Options(AntiAliasMode.Lcd), 255, 255, 255, 255));
    }

    [Fact]
    public void Lcd_DistanceFieldWins_KeepsTheFill()
    {
        var options = Tinted(Options(AntiAliasMode.Lcd));
        options.Sdf = true;

        ShouldGenerate(TestFonts.RobotoRegularBytes(), options);
    }

    [Fact]
    public void Lcd_BitmapOnlyFaceStaysGrayscale_KeepsTheFill()
    {
        var options = Tinted(Options(AntiAliasMode.Lcd));
        options.Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x1F601 });

        ShouldGenerate(TestFonts.NotoColorEmojiBytes(), options);
    }

    [Fact]
    public void Lcd_FillOnCustomGlyphsOnly_IsAllowed()
    {
        var options = Tinted(Options(AntiAliasMode.Lcd));
        options.Characters = CharacterSet.FromChars("AB");
        options.CustomGlyphs = new() { ['A'] = GrayCustom(), ['B'] = RgbaCustom() };

        ShouldGenerate(TestFonts.RobotoRegularBytes(), options);
    }

    [Fact]
    public void Lcd_FillOnAllColorColrGlyphs_KeepsTheirColors()
    {
        var options = Tinted(Options(AntiAliasMode.Lcd));
        options.ColorFont = true;
        var white = Options(AntiAliasMode.Lcd);
        white.ColorFont = true;

        var tinted = ShouldGenerate(TestFonts.BungeeColorBytes(), options).Pages[0].PixelData;
        var plain = BmFont.Generate(TestFonts.BungeeColorBytes(), white).Pages[0].PixelData;

        // The fill leaves a color glyph's colors alone; compositing it only re-rounds edges.
        tinted.Length.ShouldBe(plain.Length);
        tinted.Zip(plain, (x, y) => Math.Abs(x - y)).Max().ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Lcd_FillOnColorBitmapGlyphs_IsAllowed()
    {
        var options = Tinted(Options(AntiAliasMode.Lcd));
        options.ColorFont = true;
        options.Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x1F601 });

        ShouldGenerate(TestFonts.NotoColorEmojiBytes(), options);
    }

    [Fact]
    public void Lcd_FillWithColorFontOnAFaceWithoutColor_IsRejected()
    {
        var options = Tinted(Options(AntiAliasMode.Lcd));
        options.ColorFont = true;

        ShouldReject(TestFonts.RobotoRegularBytes(), options, "fill color", "LCD");
    }

    [Fact]
    public void Lcd_FillOnARealMixedFace_RejectsThePlainGlyphAndAllowsTheColorOne()
    {
        // Reem Kufi Ink has COLRv1 color glyphs beside plain outlines.
        var font = TestFonts.ReemKufiInkBytes();
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        var probe = new RasterOptions { Size = 32, Dpi = 72, ColorFont = true };
        var rendered = BmFont.ReadFontInfo(font).AvailableCodepoints
            .Select(cp => rasterizer.RasterizeGlyph(cp, probe))
            .Where(g => g is { Width: > 0, Height: > 0 })
            .ToList();
        var color = rendered.First(g => g!.Format == PixelFormat.Rgba32)!.Codepoint;
        var plain = rendered.First(g => g!.Format == PixelFormat.Grayscale8)!.Codepoint;

        FontGeneratorOptions For(params int[] codepoints)
        {
            var options = Tinted(Options(AntiAliasMode.Lcd));
            options.ColorFont = true;
            options.Characters = CharacterSet.FromChars(codepoints);
            return options;
        }

        ShouldGenerate(font, For(color));
        ShouldReject(font, For(color, plain), "fill color", "LCD");
    }

    [Fact]
    public void Lcd_FillUnderAGradient_IsIgnoredAsDocumented()
    {
        // The gradient is the body layer, so the fill never reaches a pixel at any setting.
        static FontGeneratorOptions Gradient(FontGeneratorOptions options)
        {
            options.GradientStartR = 255;
            options.GradientEndR = 0;
            options.GradientEndB = 255;
            return options;
        }

        var tinted = ShouldGenerate(TestFonts.RobotoRegularBytes(), Gradient(Tinted(Options(AntiAliasMode.Lcd))));
        var white = BmFont.Generate(TestFonts.RobotoRegularBytes(), Gradient(Options(AntiAliasMode.Lcd)));

        tinted.Pages[0].PixelData.ShouldBe(white.Pages[0].PixelData);
    }
}
