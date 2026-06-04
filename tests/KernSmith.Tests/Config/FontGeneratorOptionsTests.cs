using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class FontGeneratorOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        // Act
        var options = new FontGeneratorOptions();

        // Assert
        options.Size.ShouldBe(32f);
        options.Characters.ShouldBeSameAs(CharacterSet.Ascii);
        options.AntiAlias.ShouldBe(AntiAliasMode.Grayscale);
        options.MaxTextureWidth.ShouldBe(1024);
        options.MaxTextureHeight.ShouldBe(1024);
        options.Padding.ShouldBe(new Padding(0, 0, 0, 0));
        options.Spacing.ShouldBe(new Spacing(1, 1));
        options.PackingAlgorithm.ShouldBe(PackingAlgorithm.MaxRects);
        options.Kerning.ShouldBeTrue();
        options.PowerOfTwo.ShouldBeTrue();
        options.Dpi.ShouldBe(72);
        options.SuperSampleLevel.ShouldBe(1);
        options.TextureFormat.ShouldBe(TextureFormat.Png);
        options.EnableHinting.ShouldBeTrue();
        options.HeightPercent.ShouldBe(100);
        options.GradientAngle.ShouldBe(90f);
        options.GradientMidpoint.ShouldBe(0.5f);
        options.ShadowOpacity.ShouldBe(1.0f);
        options.Backend.ShouldBe(RasterizerBackend.FreeType);
    }

    [Fact]
    public void MaxTextureSize_SetsBothWidthAndHeight()
    {
        // Arrange
        var options = new FontGeneratorOptions();

        // Act
        options.MaxTextureSize = 512;

        // Assert
        options.MaxTextureWidth.ShouldBe(512);
        options.MaxTextureHeight.ShouldBe(512);
    }

    [Fact]
    public void MaxTextureSize_ReadsBackWidth()
    {
        // Arrange
        var options = new FontGeneratorOptions { MaxTextureWidth = 256, MaxTextureHeight = 128 };

        // Act & Assert
        options.MaxTextureSize.ShouldBe(256);
    }

    [Fact]
    public void HasGradient_TrueWhenStartAndEndSet()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            GradientStartR = 10,
            GradientEndR = 20,
        };

        // Act & Assert
        options.HasGradient.ShouldBeTrue();
    }

    [Fact]
    public void HasGradient_FalseByDefault()
    {
        // Act & Assert
        new FontGeneratorOptions().HasGradient.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(2, 0, 0, true)]
    [InlineData(0, 3, 0, true)]
    [InlineData(0, 0, 1, true)]
    public void HasShadow_ReflectsOffsetsAndBlur(int offsetX, int offsetY, int blur, bool expected)
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            ShadowOffsetX = offsetX,
            ShadowOffsetY = offsetY,
            ShadowBlur = blur,
        };

        // Act & Assert
        options.HasShadow.ShouldBe(expected);
    }
}
