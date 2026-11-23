using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// An LCD glyph is <see cref="PixelFormat.Rgba32"/>, which channel packing cannot store in one
/// channel. A render that really produces LCD glyphs is rejected; every path where the packed
/// glyphs stay single-channel, or the packing builder never runs, keeps generating.
/// </summary>
[Collection("RasterizerFactory")]
public class LcdChannelPackingTests
{
    private static byte[] LoadRoboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] LoadEmoji() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoColorEmoji.ttf"));

    private static FontGeneratorOptions Packed(RasterizerBackend backend) => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABg i"),
        Backend = backend,
        AntiAlias = AntiAliasMode.Lcd,
        ChannelPacking = true,
    };

    private static int NonZeroBytes(BmFontResult result) =>
        result.Pages.Sum(p => p.PixelData.Count(b => b != 0));

    private static void ShouldRejectLcdPacking(Func<BmFontResult> generate)
    {
        BmFontResult? result = null;
        var thrown = Record.Exception(() => result = generate());
        if (thrown is null)
            Assert.Fail($"Expected InvalidOperationException, but generation wrote an atlas with {NonZeroBytes(result!)} nonzero bytes.");

        var ex = thrown.ShouldBeOfType<InvalidOperationException>();
        ex.Message.ShouldContain("Channel packing", Case.Sensitive);
        ex.Message.ShouldContain("LCD", Case.Sensitive);
    }

    [Fact]
    public void Generate_FreeTypeLcd_IsRejected()
    {
        ShouldRejectLcdPacking(() => BmFont.Generate(LoadRoboto(), Packed(RasterizerBackend.FreeType)));
    }

    [Fact]
    public void QueryAtlasSize_FreeTypeLcd_IsRejectedLikeGeneration()
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => BmFont.QueryAtlasSize(LoadRoboto(), Packed(RasterizerBackend.FreeType)));
        ex.Message.ShouldContain("LCD", Case.Sensitive);
    }

    [Theory]
    [InlineData(RasterizerBackend.StbTrueType)]
    [InlineData(RasterizerBackend.Gdi)]
    [InlineData(RasterizerBackend.DirectWrite)]
    public void Generate_BackendThatDegradesLcd_KeepsPacking(RasterizerBackend backend)
    {
        Assert.SkipUnless(RasterizerFactory.IsRegistered(backend), $"{backend} is not available in this build.");

        var degraded = BmFont.Generate(LoadRoboto(), Packed(backend));
        var grayscale = Packed(backend);
        grayscale.AntiAlias = AntiAliasMode.Grayscale;

        NonZeroBytes(degraded).ShouldBeGreaterThan(0);
        degraded.Pages[0].PixelData.ShouldBe(BmFont.Generate(LoadRoboto(), grayscale).Pages[0].PixelData);
        BmFont.QueryAtlasSize(LoadRoboto(), Packed(backend)).GlyphCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_DistanceFieldWinsOverLcd_KeepsPacking()
    {
        var options = Packed(RasterizerBackend.FreeType);
        options.Sdf = true;

        NonZeroBytes(BmFont.Generate(LoadRoboto(), options)).ShouldBeGreaterThan(0);
        BmFont.QueryAtlasSize(LoadRoboto(), options).GlyphCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_FreeTypeBitmapOnlyFaceUnderLcd_RendersGrayAndKeepsPacking()
    {
        // Embedded bitmaps skip the LCD render, so nothing here is RGBA.
        var options = Packed(RasterizerBackend.FreeType);
        options.Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x1F601 });

        NonZeroBytes(BmFont.Generate(LoadEmoji(), options)).ShouldBeGreaterThan(0);
        BmFont.QueryAtlasSize(LoadEmoji(), options).GlyphCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_BackendAdvertisingLcdButReturningGray_KeepsPacking()
    {
        var options = Packed(RasterizerBackend.FreeType);
        options.Rasterizer = new GrayLcdRasterizer();

        NonZeroBytes(BmFont.Generate(LoadRoboto(), options)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_CustomChannelLayout_NeverReachesThePackerAndKeepsLcd()
    {
        var options = Packed(RasterizerBackend.FreeType);
        options.Channels = new ChannelConfig(
            Alpha: ChannelContent.Glyph,
            Red: ChannelContent.Glyph,
            Green: ChannelContent.Glyph,
            Blue: ChannelContent.Zero);

        NonZeroBytes(BmFont.Generate(LoadRoboto(), options)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_TargetRegion_NeverReachesThePackerAndKeepsLcd()
    {
        var source = new PngEncoder().Encode(new byte[256 * 256 * 4], 256, 256, PixelFormat.Rgba32);
        var options = Packed(RasterizerBackend.FreeType);
        options.TargetRegion = new AtlasTargetRegion { SourcePngData = source, X = 0, Y = 0, Width = 256, Height = 256 };

        NonZeroBytes(BmFont.Generate(LoadRoboto(), options)).ShouldBeGreaterThan(0);
    }

    /// <summary>Claims every anti-alias mode and always draws a solid grayscale box.</summary>
    private sealed class GrayLcdRasterizer : IRasterizer
    {
        public IRasterizerCapabilities Capabilities { get; } = new Caps();

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options) => new()
        {
            Codepoint = codepoint,
            GlyphIndex = codepoint,
            BitmapData = Enumerable.Repeat((byte)200, 64).ToArray(),
            Width = 8,
            Height = 8,
            Pitch = 8,
            Metrics = new GlyphMetrics(0, 8, 9, 8, 8),
            Format = PixelFormat.Grayscale8,
        };

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) =>
            codepoints.Select(cp => RasterizeGlyph(cp, options)!).ToList();

        public void Dispose()
        {
        }

        private sealed class Caps : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => false;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => false;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = Enum.GetValues<AntiAliasMode>();
        }
    }
}
