using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class ConfigFormatDetectorTests
{
    [Fact]
    public void DetectFromContent_BmfcKeys_ReturnsBmfc()
    {
        // Act
        var format = ConfigFormatDetector.DetectFromContent("fontSize=32\nchars=32-126\n");

        // Assert
        format.ShouldBe(DetectedConfigFormat.Bmfc);
    }

    [Fact]
    public void DetectFromContent_AngelCodeHeader_ReturnsBmfc()
    {
        // Act
        var format = ConfigFormatDetector.DetectFromContent(
            "# AngelCode Bitmap Font Generator configuration file\n");

        // Assert
        format.ShouldBe(DetectedConfigFormat.Bmfc);
    }

    [Fact]
    public void DetectFromContent_HieroDottedKeys_ReturnsHiero()
    {
        // Act
        var format = ConfigFormatDetector.DetectFromContent("font.name=Arial\nfont.size=32\n");

        // Assert
        format.ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_RenderType_ReturnsHiero()
    {
        // Act
        var format = ConfigFormatDetector.DetectFromContent("render_type=0\n");

        // Assert
        format.ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_LibGdxEffectClass_ReturnsHiero()
    {
        // Act
        var format = ConfigFormatDetector.DetectFromContent(
            "effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.ColorEffect\n");

        // Assert
        format.ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_Empty_ReturnsUnknown()
    {
        // Act & Assert
        ConfigFormatDetector.DetectFromContent("").ShouldBe(DetectedConfigFormat.Unknown);
    }

    [Fact]
    public void DetectFromContent_NoSignals_ReturnsUnknown()
    {
        // Act
        var format = ConfigFormatDetector.DetectFromContent("random=value\nother=thing\n");

        // Assert
        format.ShouldBe(DetectedConfigFormat.Unknown);
    }
}
