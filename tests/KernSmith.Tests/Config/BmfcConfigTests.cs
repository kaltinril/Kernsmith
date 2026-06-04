using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class BmfcConfigTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        // Act
        var config = new BmfcConfig();

        // Assert
        config.Options.ShouldNotBeNull();
        config.FontFile.ShouldBeNull();
        config.FontName.ShouldBeNull();
        config.OutputPath.ShouldBeNull();
        config.OutputFormat.ShouldBe(OutputFormat.Text);
    }

    [Fact]
    public void FromOptions_WrapsOptions()
    {
        // Arrange
        var options = new FontGeneratorOptions { Size = 48 };

        // Act
        var config = BmfcConfig.FromOptions(
            options,
            fontFile: "font.ttf",
            fontName: "Arial",
            outputPath: "out.fnt",
            outputFormat: OutputFormat.Xml);

        // Assert
        config.Options.ShouldBeSameAs(options);
        config.FontFile.ShouldBe("font.ttf");
        config.FontName.ShouldBe("Arial");
        config.OutputPath.ShouldBe("out.fnt");
        config.OutputFormat.ShouldBe(OutputFormat.Xml);
    }

    [Fact]
    public void FromOptions_DefaultsOutputFormatToText()
    {
        // Act
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions());

        // Assert
        config.OutputFormat.ShouldBe(OutputFormat.Text);
        config.FontFile.ShouldBeNull();
    }

    [Fact]
    public void FromOptions_NullOptions_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => BmfcConfig.FromOptions(null!));
    }
}
