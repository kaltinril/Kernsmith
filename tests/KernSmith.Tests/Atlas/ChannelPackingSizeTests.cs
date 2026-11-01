using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// <see cref="FontGeneratorOptions.ChannelPacking"/> packs glyph rects side by side exactly as an
/// unpacked atlas does (each then writes one channel), so its atlas must be sized like one — an
/// estimate of a quarter of the area spills glyphs onto extra pages.
/// </summary>
public sealed class ChannelPackingSizeTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ChannelPackedAtlas_IsSizedLikeTheUnpackedOne(bool autofit, bool equalize)
    {
        FontGeneratorOptions Options(bool channelPacking) => new()
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Rasterizer = new StbTrueTypeRasterizer(),
            ChannelPacking = channelPacking,
            AutofitTexture = autofit,
            EqualizeCellHeights = equalize,
        };

        var plain = BmFont.Generate(Font(), Options(channelPacking: false));
        var packed = BmFont.Generate(Font(), Options(channelPacking: true));

        plain.Pages.Count.ShouldBe(1);
        packed.Pages.Count.ShouldBe(1);
        (packed.Pages[0].Width, packed.Pages[0].Height).ShouldBe((plain.Pages[0].Width, plain.Pages[0].Height));
    }
}
