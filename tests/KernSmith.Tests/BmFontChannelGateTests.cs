using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Regression tests for issue #169: <see cref="BmFont.ShouldApplyChannelConfig"/> must be
/// reachable when an effect (outline/shadow/gradient) is active, since the mutually-exclusive
/// guard in <see cref="BmFont.Generate"/> only rejects ChannelPacking+effects — not
/// per-channel ChannelConfig+effects, which route through <c>ChannelCompositor</c> instead.
/// </summary>
public class BmFontChannelGateTests
{
    [Fact]
    public void ShouldApplyChannelConfig_WithShadowAndCustomChannels_ReturnsTrue()
    {
        var options = new FontGeneratorOptions
        {
            ShadowOffsetX = 6,
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Shadow,
                Red: ChannelContent.Glyph,
                Green: ChannelContent.Glyph,
                Blue: ChannelContent.Glyph)
        };

        BmFont.ShouldApplyChannelConfig(options).ShouldBeTrue(
            "a non-default ChannelConfig must be honored even when an effect (shadow) is active — " +
            "ChannelPacking is a separate, mutually-exclusive mechanism and must not gate this");
    }

    [Fact]
    public void ShouldApplyChannelConfig_WithOutlineAndCustomChannels_ReturnsTrue()
    {
        var options = new FontGeneratorOptions
        {
            Outline = 2,
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Outline,
                Red: ChannelContent.Glyph,
                Green: ChannelContent.Glyph,
                Blue: ChannelContent.Glyph)
        };

        BmFont.ShouldApplyChannelConfig(options).ShouldBeTrue(
            "a non-default ChannelConfig must be honored even when an effect (outline) is active");
    }
}
