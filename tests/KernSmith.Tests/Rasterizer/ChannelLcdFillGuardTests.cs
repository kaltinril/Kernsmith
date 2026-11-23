using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Option combinations that used to write wrong pixels without an error: a fill color or an RGBA
/// custom glyph under channel packing (the packer skips RGBA, leaving the glyph blank), and a fill
/// color over real LCD glyphs (their RGB is subpixel coverage, which the fill cannot tint). Each
/// is rejected by generation and by a size query, and every neighbor that renders correctly keeps
/// working.
/// </summary>
[Collection("RasterizerFactory")]
public class ChannelLcdFillGuardTests
{
    // Native is not registered in this project; KernSmith.Rasterizers.Native.Tests has its half.
    private const RasterizerBackend Backend = RasterizerBackend.FreeType;

    private const string Chars = "ABg i";

    private static byte[] Fixture(params string[] path) =>
        File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, "Fixtures", .. path]));

    private static byte[] Roboto() => Fixture("Roboto-Regular.ttf");

    private static byte[] Bungee() => Fixture("Color", "BungeeColor-Regular.ttf");

    private static byte[] Emoji() => Fixture("NotoColorEmoji.ttf");

    private static FontGeneratorOptions Options(
        RasterizerBackend backend, AntiAliasMode antiAlias = AntiAliasMode.Grayscale, bool packed = false) => new()
        {
            Size = 32,
            Characters = CharacterSet.FromChars(Chars),
            Backend = backend,
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

    private static CustomGlyph RgbaCustom() =>
        new(4, 4, Enumerable.Repeat((byte)200, 64).ToArray());

    private static CustomGlyph GrayCustom() =>
        new(4, 4, Enumerable.Repeat((byte)200, 16).ToArray(), PixelFormat.Grayscale8);

    private static AtlasTargetRegion Region() => new()
    {
        SourcePngData = new PngEncoder().Encode(new byte[256 * 256 * 4], 256, 256, PixelFormat.Rgba32),
        X = 0,
        Y = 0,
        Width = 256,
        Height = 256,
    };

    private static ChannelConfig CustomChannels() => new(
        Alpha: ChannelContent.Glyph,
        Red: ChannelContent.Glyph,
        Green: ChannelContent.Glyph,
        Blue: ChannelContent.Zero);

    private static int NonZeroBytes(BmFontResult result) =>
        result.Pages.Sum(p => p.PixelData.Count(b => b != 0));

    /// <summary>Both entry points refuse the options, with a message naming what conflicts.</summary>
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

    /// <summary>Generation draws ink and a size query agrees that the options are valid.</summary>
    private static BmFontResult ShouldGenerate(byte[] font, FontGeneratorOptions options)
    {
        var result = BmFont.Generate(font, options);
        NonZeroBytes(result).ShouldBeGreaterThan(0);
        BmFont.QueryAtlasSize(font, options).GlyphCount.ShouldBeGreaterThan(0);
        return result;
    }

    // ---- Channel packing + fill color ----

    [Theory]
    [InlineData(254, 255, 255, 255)]
    [InlineData(255, 254, 255, 255)]
    [InlineData(255, 255, 254, 255)]
    [InlineData(255, 255, 255, 254)]
    public void ChannelPacking_WithAnyNonDefaultFill_IsRejected(byte r, byte g, byte b, byte a)
    {
        ShouldReject(Roboto(), Tinted(Options(Backend, packed: true), r, g, b, a), "Channel packing", "fill color");
    }

    [Fact]
    public void ChannelPacking_SingleChannelSdfWithFill_IsRejected()
    {
        // Unlike LCD, a distance field does not escape this one: the fill composites it to RGBA.
        var options = Tinted(Options(Backend, packed: true));
        options.Sdf = true;

        ShouldReject(Roboto(), options, "Channel packing", "fill color");
    }

    [Fact]
    public void ChannelPacking_WithWhiteFill_KeepsPacking()
    {
        ShouldGenerate(Roboto(), Tinted(Options(Backend, packed: true), 255, 255, 255, 255));
    }

    [Fact]
    public void ChannelPacking_FillWithCustomChannels_NeverReachesThePacker()
    {
        var options = Tinted(Options(Backend, packed: true));
        options.Channels = CustomChannels();

        ShouldGenerate(Roboto(), options);
    }

    [Fact]
    public void ChannelPacking_FillWithTargetRegion_NeverReachesThePacker()
    {
        var options = Tinted(Options(Backend, packed: true));
        options.TargetRegion = Region();

        ShouldGenerate(Roboto(), options);
    }

    // ---- Channel packing + custom glyphs ----

    [Fact]
    public void ChannelPacking_WithRgbaCustomGlyph_IsRejected()
    {
        var options = Options(Backend, packed: true);
        options.CustomGlyphs = new() { ['A'] = RgbaCustom() };

        ShouldReject(Roboto(), options, "Channel packing", "custom glyph", "U+0041");
    }

    [Fact]
    public void ChannelPacking_WithGrayscaleCustomGlyph_PacksItsPixels()
    {
        var options = Options(Backend, packed: true);
        options.Characters = CharacterSet.FromChars("A");
        options.CustomGlyphs = new() { ['A'] = GrayCustom() };

        var result = ShouldGenerate(Roboto(), options);

        result.Pages[0].PixelData.Count(b => b == 200).ShouldBe(16);
    }

    [Fact]
    public void ChannelPacking_WithEmptyRgbaCustomGlyph_KeepsPacking()
    {
        // The packer has no pixels to lose here.
        var options = Options(Backend, packed: true);
        options.CustomGlyphs = new() { ['A'] = new CustomGlyph(0, 0, []) };

        ShouldGenerate(Roboto(), options);
    }

    [Fact]
    public void ChannelPacking_RgbaCustomGlyphWithCustomChannels_NeverReachesThePacker()
    {
        var options = Options(Backend, packed: true);
        options.Channels = CustomChannels();
        options.CustomGlyphs = new() { ['A'] = RgbaCustom() };

        ShouldGenerate(Roboto(), options);
    }

    [Fact]
    public void ChannelPacking_RgbaCustomGlyphWithTargetRegion_NeverReachesThePacker()
    {
        var options = Options(Backend, packed: true);
        options.TargetRegion = Region();
        options.CustomGlyphs = new() { ['A'] = RgbaCustom() };

        ShouldGenerate(Roboto(), options);
    }

    // ---- LCD + fill color ----

    [Theory]
    [InlineData(200, 255, 255, 255)]
    [InlineData(255, 255, 255, 128)]
    public void Lcd_WithFill_IsRejected(byte r, byte g, byte b, byte a)
    {
        ShouldReject(Roboto(), Tinted(Options(Backend, AntiAliasMode.Lcd), r, g, b, a), "fill color", "LCD");
    }

    [Fact]
    public void Lcd_WithFillAndCustomChannels_IsRejected()
    {
        // Only the packing guards depend on the page builder; the tint is lost before it runs.
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Channels = CustomChannels();

        ShouldReject(Roboto(), options, "fill color", "LCD");
    }

    [Fact]
    public void Lcd_WithWhiteFill_KeepsLcd()
    {
        ShouldGenerate(Roboto(), Tinted(Options(Backend, AntiAliasMode.Lcd), 255, 255, 255, 255));
    }

    [Theory]
    [InlineData(RasterizerBackend.StbTrueType)]
    [InlineData(RasterizerBackend.Gdi)]
    [InlineData(RasterizerBackend.DirectWrite)]
    public void Lcd_BackendThatNegotiatesItDown_KeepsTheTint(RasterizerBackend backend)
    {
        Assert.SkipUnless(RasterizerFactory.IsRegistered(backend), $"{backend} is not available in this build.");

        var degraded = ShouldGenerate(Roboto(), Tinted(Options(backend, AntiAliasMode.Lcd)));
        var grayscale = BmFont.Generate(Roboto(), Tinted(Options(backend)));

        degraded.Pages[0].PixelData.ShouldBe(grayscale.Pages[0].PixelData);
    }

    [Fact]
    public void Lcd_DistanceFieldWins_KeepsTheFill()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Sdf = true;

        ShouldGenerate(Roboto(), options);
    }

    [Fact]
    public void Lcd_BitmapOnlyFaceStaysGrayscale_KeepsTheFill()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x1F601 });

        ShouldGenerate(Emoji(), options);
    }

    [Fact]
    public void Lcd_FillOnCustomGlyphsOnly_IsAllowed()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Characters = CharacterSet.FromChars("AB");
        options.CustomGlyphs = new() { ['A'] = GrayCustom(), ['B'] = RgbaCustom() };

        ShouldGenerate(Roboto(), options);
    }

    [Fact]
    public void Lcd_FillWithCustomGlyphBesideARealLcdGlyph_IsRejected()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Characters = CharacterSet.FromChars("AB");
        options.CustomGlyphs = new() { ['A'] = GrayCustom() };

        ShouldReject(Roboto(), options, "fill color", "LCD");
    }

    [Fact]
    public void Lcd_FillUnderAGradient_IsIgnoredAsDocumented()
    {
        // The gradient is the body layer, so the fill never reaches a pixel at any setting.
        FontGeneratorOptions Gradient(FontGeneratorOptions options)
        {
            options.GradientStartR = 255;
            options.GradientEndR = 0;
            options.GradientEndB = 255;
            return options;
        }

        var tinted = ShouldGenerate(Roboto(), Gradient(Tinted(Options(Backend, AntiAliasMode.Lcd))));
        var white = BmFont.Generate(Roboto(), Gradient(Options(Backend, AntiAliasMode.Lcd)));

        tinted.Pages[0].PixelData.ShouldBe(white.Pages[0].PixelData);
    }

    // ---- LCD + fill color + ColorFont: a color glyph is Rgba32 too, but is not LCD ink ----

    [Fact]
    public void Lcd_FillOnAllColorColrGlyphs_KeepsTheirColors()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.ColorFont = true;
        var white = Options(Backend, AntiAliasMode.Lcd);
        white.ColorFont = true;

        var tinted = ShouldGenerate(Bungee(), options).Pages[0].PixelData;
        var plain = BmFont.Generate(Bungee(), white).Pages[0].PixelData;

        // The fill leaves a color glyph's colors alone; compositing it only re-rounds edges.
        tinted.Length.ShouldBe(plain.Length);
        tinted.Zip(plain, (x, y) => Math.Abs(x - y)).Max().ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Lcd_FillOnColorBitmapGlyphs_IsAllowed()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.ColorFont = true;
        options.Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x1F601 });

        ShouldGenerate(Emoji(), options);
    }

    [Fact]
    public void Lcd_FillWithColorFontOnAFaceWithoutColor_IsRejected()
    {
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.ColorFont = true;

        ShouldReject(Roboto(), options, "fill color", "LCD");
    }

    [Fact]
    public void Lcd_FillOnAScriptedMixedBackend_TellsColorFromLcd()
    {
        var mixed = Tinted(Options(RasterizerBackend.FreeType, AntiAliasMode.Lcd));
        mixed.ColorFont = true;
        mixed.Rasterizer = new ScriptedRasterizer { ColorCodepoints = ['A', 'B'] };
        ShouldReject(Roboto(), mixed, "fill color", "LCD");

        var allColor = Tinted(Options(RasterizerBackend.FreeType, AntiAliasMode.Lcd));
        allColor.ColorFont = true;
        allColor.Characters = CharacterSet.FromChars("AB");
        allColor.Rasterizer = new ScriptedRasterizer { ColorCodepoints = ['A', 'B'] };
        ShouldGenerate(Roboto(), allColor);
    }

    [Fact]
    public void IncrementalSession_LcdWithFill_RejectsTheAddAndKeepsTheSessionEmpty()
    {
        using var session = BmFont.BeginIncremental(Roboto(), Tinted(Options(Backend, AntiAliasMode.Lcd)));

        Should.Throw<InvalidOperationException>(() => session.AddGlyphs("AB"))
            .Message.ShouldContain("fill color", Case.Sensitive);

        session.PageCount.ShouldBe(0);
    }

    // ---- The size query stays cheap ----

    [Fact]
    public void QueryAtlasSize_LcdWithoutFill_RendersNothing()
    {
        var rasterizer = new ScriptedRasterizer();
        var options = Options(RasterizerBackend.FreeType, AntiAliasMode.Lcd);
        options.Rasterizer = rasterizer;

        BmFont.QueryAtlasSize(Roboto(), options);

        rasterizer.RenderCalls.ShouldBe(0);
    }

    [Fact]
    public void QueryAtlasSize_FillWithoutLcd_RendersNothing()
    {
        var rasterizer = new ScriptedRasterizer();
        var options = Tinted(Options(RasterizerBackend.FreeType));
        options.Rasterizer = rasterizer;

        BmFont.QueryAtlasSize(Roboto(), options);

        rasterizer.RenderCalls.ShouldBe(0);
    }

    [Fact]
    public void QueryAtlasSize_LcdWithFill_StopsAtTheFirstLcdGlyph()
    {
        var rasterizer = new ScriptedRasterizer();
        var options = Tinted(Options(RasterizerBackend.FreeType, AntiAliasMode.Lcd));
        options.Rasterizer = rasterizer;

        Should.Throw<InvalidOperationException>(() => BmFont.QueryAtlasSize(Roboto(), options));

        rasterizer.RenderCalls.ShouldBe(1);
    }

    [Fact]
    public void Lcd_FillOnABackendThatNegotiatesLcdDown_IsNotCheckedEvenForRgbaGlyphs()
    {
        // The negotiated mode decides, so RGBA from a backend without LCD is not LCD ink.
        AntiAliasMode[] noLcd = [AntiAliasMode.None, AntiAliasMode.Grayscale];
        var measuring = new ScriptedRasterizer { AntiAliasModes = noLcd };
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Rasterizer = measuring;

        BmFont.QueryAtlasSize(Roboto(), options);
        measuring.RenderCalls.ShouldBe(0);

        options.Rasterizer = new ScriptedRasterizer { AntiAliasModes = noLcd };
        NonZeroBytes(BmFont.Generate(Roboto(), options)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Lcd_FillUnderADistanceField_IsNotCheckedEvenForRgbaGlyphs()
    {
        // A distance field wins over LCD, so the render is not LCD whatever its format.
        var measuring = new ScriptedRasterizer();
        var options = Tinted(Options(Backend, AntiAliasMode.Lcd));
        options.Sdf = true;
        options.Rasterizer = measuring;

        BmFont.QueryAtlasSize(Roboto(), options);
        measuring.RenderCalls.ShouldBe(0);

        options.Rasterizer = new ScriptedRasterizer();
        NonZeroBytes(BmFont.Generate(Roboto(), options)).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Claims every anti-alias mode unless told otherwise. With color on,
    /// <see cref="ColorCodepoints"/> draw solid red; every other glyph, and every glyph with color
    /// off, draws the same LCD-style pixels.
    /// </summary>
    private sealed class ScriptedRasterizer : IRasterizer
    {
        public HashSet<int> ColorCodepoints { get; init; } = [];

        public IReadOnlyList<AntiAliasMode> AntiAliasModes { get; init; } = Enum.GetValues<AntiAliasMode>();

        public int RenderCalls { get; private set; }

        public IRasterizerCapabilities Capabilities => new Caps(AntiAliasModes);

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            RenderCalls++;
            byte[] pixel = options.ColorFont && ColorCodepoints.Contains(codepoint)
                ? [255, 0, 0, 255]
                : [40, 120, 200, 200];
            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = codepoint,
                BitmapData = Enumerable.Repeat(pixel, 64).SelectMany(p => p).ToArray(),
                Width = 8,
                Height = 8,
                Pitch = 32,
                Metrics = new GlyphMetrics(0, 8, 9, 8, 8),
                Format = PixelFormat.Rgba32,
            };
        }

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) =>
            codepoints.Select(cp => RasterizeGlyph(cp, options)!).ToList();

        public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options) => new GlyphMetrics(0, 8, 9, 8, 8);

        public void Dispose()
        {
        }

        private sealed class Caps(IReadOnlyList<AntiAliasMode> modes) : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => true;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => true;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes => modes;
        }
    }
}
