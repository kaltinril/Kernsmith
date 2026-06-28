using KernSmith.Atlas;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Tests for the channel-content gate: a non-default <see cref="ChannelConfig"/> is only
/// honored when channel-packing is on OR the font has no baked composite effects
/// (gradient, shadow, or outline&gt;0). When a font has baked effects, applying a
/// separated-channel layout would tear apart the baked Rgba32 composite (gradient color,
/// soft outline, drop shadow), so the channel layout is skipped and the default
/// single-composite path (and default .fnt channel metadata) is used instead.
/// </summary>
[Collection("RasterizerFactory")]
public class ChannelGatingTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    /// <summary>
    /// A gradient font with a separated-channel layout (glyph-in-RGB, outline-in-alpha)
    /// must SKIP the channel layout: the baked gradient survives in RGB (so RGB is not
    /// flattened to the coverage byte), and the result is identical to the no-Channels path.
    /// </summary>
    [Fact]
    public void Gradient_WithSeparatedChannels_SkipsChannelApplication_PreservesComposite()
    {
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("O");

        // Baseline: same gradient, no Channels (the default single-composite path).
        FontGeneratorOptions BaseOptions() => new()
        {
            Size = 48,
            Characters = chars,
            // Red -> gold vertical gradient (fire.bmfc style).
            GradientStartR = 0xFF,
            GradientStartG = 0x00,
            GradientStartB = 0x00,
            GradientEndR = 0xFF,
            GradientEndG = 0xD7,
            GradientEndB = 0x00,
        };

        var resultDefault = BmFont.Generate(fontData, BaseOptions());

        var withChannels = BaseOptions();
        // Separated layout that, if honored, would destroy the baked composite: force
        // Green/Blue to One (white), which flattens the gradient color away. Gating must
        // skip this so the gradient survives and the pixels equal the no-Channels path.
        withChannels.Channels = new ChannelConfig(
            Alpha: ChannelContent.Glyph,
            Red: ChannelContent.Glyph,
            Green: ChannelContent.One,
            Blue: ChannelContent.One);
        var resultChanneled = BmFont.Generate(fontData, withChannels);

        // The pixel output must equal the no-Channels path (composite preserved).
        resultChanneled.Pages.Count.ShouldBe(resultDefault.Pages.Count);
        resultChanneled.Pages[0].PixelData.ShouldBe(resultDefault.Pages[0].PixelData);

        // The gradient must survive: somewhere in the glyph, R != G (red-gold gradient),
        // proving RGB was not flattened to a single coverage value.
        var page = resultChanneled.Pages[0];
        var sawColor = false;
        for (var i = 0; i + 3 < page.PixelData.Length; i += 4)
        {
            var r = page.PixelData[i + 0];
            var g = page.PixelData[i + 1];
            var a = page.PixelData[i + 3];
            if (a > 0 && r != g) { sawColor = true; break; }
        }
        sawColor.ShouldBeTrue("gradient color (R != G) must survive — RGB must not be flattened to coverage");

        // The .fnt common-block channel metadata must be the DEFAULT (all 0 = glyph),
        // NOT the separated layout (alpha=1=outline). A skipped font writes default values.
        resultChanneled.Model.Common.AlphaChnl.ShouldBe(0);
        resultChanneled.Model.Common.RedChnl.ShouldBe(0);
        resultChanneled.Model.Common.GreenChnl.ShouldBe(0);
        resultChanneled.Model.Common.BlueChnl.ShouldBe(0);
    }

    /// <summary>
    /// A no-effects font with a non-default <see cref="ChannelConfig"/> STILL applies it.
    /// This guards against the gate being too aggressive. Mirrors Arial's layout:
    /// glyph in alpha, white (One) in RGB, no gradient/shadow/outline.
    /// </summary>
    [Fact]
    public void NoEffects_WithChannelConfig_StillApplies()
    {
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("O");

        var options = new FontGeneratorOptions
        {
            Size = 48,
            Characters = chars,
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Glyph,
                Red: ChannelContent.One,
                Green: ChannelContent.One,
                Blue: ChannelContent.One),
        };

        var result = BmFont.Generate(fontData, options);

        // White RGB fill (One = 255) must be present where the glyph is opaque.
        var page = result.Pages[0];
        var sawWhiteFill = false;
        for (var i = 0; i + 3 < page.PixelData.Length; i += 4)
        {
            var r = page.PixelData[i + 0];
            var g = page.PixelData[i + 1];
            var b = page.PixelData[i + 2];
            var a = page.PixelData[i + 3];
            if (a > 0 && r == 255 && g == 255 && b == 255) { sawWhiteFill = true; break; }
        }
        sawWhiteFill.ShouldBeTrue("no-effects font with One in RGB must apply the white fill");

        // The channel layout WAS honored, so the .fnt common block reflects it
        // (red/green/blue = 1 = One per BMFont's channel content encoding).
        result.Model.Common.RedChnl.ShouldBe((int)ChannelContent.One);
        result.Model.Common.GreenChnl.ShouldBe((int)ChannelContent.One);
        result.Model.Common.BlueChnl.ShouldBe((int)ChannelContent.One);
        result.Model.Common.AlphaChnl.ShouldBe((int)ChannelContent.Glyph);
    }
}
