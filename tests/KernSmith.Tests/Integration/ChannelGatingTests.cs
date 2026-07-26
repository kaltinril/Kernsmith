using KernSmith.Atlas;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Tests for the channel-content gate: a non-default <see cref="ChannelConfig"/> is honored
/// whenever it is present, regardless of whether the font has baked composite effects
/// (gradient, shadow, outline&gt;0). <see cref="ChannelCompositor"/> reads the actual RGBA
/// component bytes from the composited glyph for <see cref="ChannelContent.Glyph"/>, so any
/// baked color (e.g. a gradient) survives being routed through a channel that also reads
/// Glyph content — only channels the caller explicitly sets to <see cref="ChannelContent.Zero"/>
/// or <see cref="ChannelContent.One"/> are overwritten, which is the caller's intent (issue #169).
/// </summary>
[Collection("RasterizerFactory")]
public class ChannelGatingTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    /// <summary>
    /// A gradient font with a separated-channel layout (glyph-in-Red, One-in-Green/Blue) must
    /// APPLY the channel layout: the gradient's Red component survives via Glyph content
    /// (issue #169 — a non-default ChannelConfig must not be silently skipped just because an
    /// effect is active), while Green/Blue are overwritten to 255 as configured.
    /// </summary>
    [Fact]
    public void Gradient_WithSeparatedChannels_AppliesChannelConfig()
    {
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("O");

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

        var withChannels = BaseOptions();
        withChannels.Channels = new ChannelConfig(
            Alpha: ChannelContent.Glyph,
            Red: ChannelContent.Glyph,
            Green: ChannelContent.One,
            Blue: ChannelContent.One);
        var resultChanneled = BmFont.Generate(fontData, withChannels);

        // The gradient's Red component must survive (Glyph content reads the actual RGBA byte).
        var page = resultChanneled.Pages[0];
        var sawGradientRed = false;
        var sawForcedOne = false;
        for (var i = 0; i + 3 < page.PixelData.Length; i += 4)
        {
            var r = page.PixelData[i + 0];
            var g = page.PixelData[i + 1];
            var b = page.PixelData[i + 2];
            var a = page.PixelData[i + 3];
            if (a > 0 && r > 0) sawGradientRed = true;
            if (a > 0 && g == 255 && b == 255) sawForcedOne = true;
        }
        sawGradientRed.ShouldBeTrue("Glyph content in Red must preserve the gradient's Red component");
        sawForcedOne.ShouldBeTrue("Green/Blue configured as One must be forced to 255");

        // The .fnt common-block channel metadata must reflect the configured layout, not defaults.
        resultChanneled.Model.Common.AlphaChnl.ShouldBe((int)ChannelContent.Glyph);
        resultChanneled.Model.Common.RedChnl.ShouldBe((int)ChannelContent.Glyph);
        resultChanneled.Model.Common.GreenChnl.ShouldBe((int)ChannelContent.One);
        resultChanneled.Model.Common.BlueChnl.ShouldBe((int)ChannelContent.One);
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
