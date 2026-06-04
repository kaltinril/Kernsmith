using Shouldly;

namespace KernSmith.Tests.Config;

/// <summary>
/// Tests for the small Config value types: Padding, Spacing, ChannelConfig,
/// CustomGlyph, AtlasSizeConstraints, and AtlasTargetRegion.
/// </summary>
public sealed class ValueTypeTests
{
    [Fact]
    public void Padding_AllSidesConstructor_SetsEverySide()
    {
        // Act
        var padding = new Padding(5);

        // Assert
        padding.Up.ShouldBe(5);
        padding.Right.ShouldBe(5);
        padding.Down.ShouldBe(5);
        padding.Left.ShouldBe(5);
    }

    [Fact]
    public void Padding_Zero_IsAllZero()
    {
        // Act & Assert
        Padding.Zero.ShouldBe(new Padding(0, 0, 0, 0));
    }

    [Fact]
    public void Padding_RecordEquality_ComparesByValue()
    {
        // Act & Assert
        new Padding(1, 2, 3, 4).ShouldBe(new Padding(1, 2, 3, 4));
        new Padding(1, 2, 3, 4).ShouldNotBe(new Padding(4, 3, 2, 1));
    }

    [Fact]
    public void Spacing_BothConstructor_SetsBothDirections()
    {
        // Act
        var spacing = new Spacing(3);

        // Assert
        spacing.Horizontal.ShouldBe(3);
        spacing.Vertical.ShouldBe(3);
    }

    [Fact]
    public void Spacing_Zero_IsAllZero()
    {
        // Act & Assert
        Spacing.Zero.ShouldBe(new Spacing(0, 0));
    }

    [Fact]
    public void Spacing_RecordEquality_ComparesByValue()
    {
        // Act & Assert
        new Spacing(2, 4).ShouldBe(new Spacing(2, 4));
        new Spacing(2, 4).ShouldNotBe(new Spacing(4, 2));
    }

    [Fact]
    public void ChannelConfig_Default_IsDefault()
    {
        // Act & Assert
        new ChannelConfig().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void ChannelConfig_NonGlyphChannel_IsNotDefault()
    {
        // Act
        var config = new ChannelConfig(Alpha: ChannelContent.Outline);

        // Assert
        config.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void ChannelConfig_InvertedChannel_IsNotDefault()
    {
        // Act
        var config = new ChannelConfig(InvertAlpha: true);

        // Assert
        config.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void ChannelConfig_RecordEquality_ComparesByValue()
    {
        // Act & Assert
        new ChannelConfig(Red: ChannelContent.Zero)
            .ShouldBe(new ChannelConfig(Red: ChannelContent.Zero));
    }

    [Fact]
    public void CustomGlyph_DefaultsFormatToRgba32()
    {
        // Act
        var glyph = new CustomGlyph(2, 2, new byte[16]);

        // Assert
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.XAdvance.ShouldBeNull();
    }

    [Fact]
    public void CustomGlyph_StoresDimensionsAndAdvance()
    {
        // Act
        var glyph = new CustomGlyph(4, 8, new byte[128], PixelFormat.Rgba32, XAdvance: 6);

        // Assert
        glyph.Width.ShouldBe(4);
        glyph.Height.ShouldBe(8);
        glyph.XAdvance.ShouldBe(6);
    }

    [Fact]
    public void AtlasSizeConstraints_Defaults_AreFalseAndZero()
    {
        // Act
        var constraints = new AtlasSizeConstraints();

        // Assert
        constraints.ForceSquare.ShouldBeFalse();
        constraints.ForcePowerOfTwo.ShouldBeFalse();
        constraints.FixedWidth.ShouldBe(0);
    }

    [Fact]
    public void AtlasTargetRegion_Defaults_AreNullAndZero()
    {
        // Act
        var region = new AtlasTargetRegion();

        // Assert
        region.SourcePngPath.ShouldBeNull();
        region.SourcePngData.ShouldBeNull();
        region.X.ShouldBe(0);
        region.Y.ShouldBe(0);
        region.Width.ShouldBe(0);
        region.Height.ShouldBe(0);
    }
}
