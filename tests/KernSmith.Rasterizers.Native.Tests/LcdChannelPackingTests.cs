using KernSmith.Output;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Native renders LCD as RGBA, which channel packing cannot store in one channel. A render that
/// really produces LCD glyphs is rejected; a distance field or a bitmap-only face is single-channel
/// under the same request and keeps packing.
/// </summary>
public class LcdChannelPackingTests
{
    private static FontGeneratorOptions Packed() => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABg i"),
        Backend = RasterizerBackend.Native,
        AntiAlias = AntiAliasMode.Lcd,
        ChannelPacking = true,
    };

    private static int NonZeroBytes(BmFontResult result) =>
        result.Pages.Sum(p => p.PixelData.Count(b => b != 0));

    [Fact]
    public void Generate_Lcd_IsRejected()
    {
        BmFontResult? result = null;
        var thrown = Record.Exception(() => result = BmFont.Generate(TestFonts.RobotoRegularBytes(), Packed()));
        if (thrown is null)
            Assert.Fail($"Expected InvalidOperationException, but generation wrote an atlas with {NonZeroBytes(result!)} nonzero bytes.");

        var ex = thrown.ShouldBeOfType<InvalidOperationException>();
        ex.Message.ShouldContain("Channel packing", Case.Sensitive);
        ex.Message.ShouldContain("LCD", Case.Sensitive);
    }

    [Fact]
    public void QueryAtlasSize_Lcd_IsRejectedLikeGeneration()
    {
        Should.Throw<InvalidOperationException>(() => BmFont.QueryAtlasSize(TestFonts.RobotoRegularBytes(), Packed()))
            .Message.ShouldContain("LCD", Case.Sensitive);
    }

    [Fact]
    public void Generate_DistanceFieldWinsOverLcd_KeepsPacking()
    {
        var options = Packed();
        options.Sdf = true;

        NonZeroBytes(BmFont.Generate(TestFonts.RobotoRegularBytes(), options)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_BitmapOnlyFaceUnderLcd_RendersGrayAndKeepsPacking()
    {
        var options = Packed();
        options.Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x1F601 });

        NonZeroBytes(BmFont.Generate(TestFonts.NotoColorEmojiBytes(), options)).ShouldBeGreaterThan(0);
        BmFont.QueryAtlasSize(TestFonts.NotoColorEmojiBytes(), options).GlyphCount.ShouldBeGreaterThan(0);
    }
}
