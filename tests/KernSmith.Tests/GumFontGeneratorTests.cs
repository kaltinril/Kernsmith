using KernSmith.Gum;
using KernSmith.Output;
using RenderingLibrary.Graphics.Fonts;
using Shouldly;

namespace KernSmith.Tests;

public class GumFontGeneratorTests
{
    [Fact]
    public void BuildOptions_MapsBasicProperties()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        bmfcSave.FontSize = 24;
        bmfcSave.FontName = "Arial";
        bmfcSave.IsBold = true;
        bmfcSave.IsItalic = true;
        bmfcSave.UseSmoothing = true;
        bmfcSave.OutlineThickness = 0;
        bmfcSave.SpacingHorizontal = 2;
        bmfcSave.SpacingVertical = 3;
        bmfcSave.OutputWidth = 1024;
        bmfcSave.OutputHeight = 512;

        // Act
        FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);

        // Assert
        options.Size.ShouldBe(24);
        options.Bold.ShouldBeTrue();
        options.Italic.ShouldBeTrue();
        options.MatchCharHeight.ShouldBeTrue();
        options.AntiAlias.ShouldBe(AntiAliasMode.Grayscale);
        options.Outline.ShouldBe(0);
        options.Spacing.Horizontal.ShouldBe(2);
        options.Spacing.Vertical.ShouldBe(3);
        options.MaxTextureWidth.ShouldBe(1024);
        options.MaxTextureHeight.ShouldBe(512);
    }

    [Fact]
    public void BuildOptions_NoOutline_SetsGlyphAlphaAndWhiteRgb()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        bmfcSave.OutlineThickness = 0;

        // Act
        FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);

        // Assert
        ChannelConfig channels = options.Channels!;
        channels.Alpha.ShouldBe(ChannelContent.Glyph);
        channels.Red.ShouldBe(ChannelContent.One);
        channels.Green.ShouldBe(ChannelContent.One);
        channels.Blue.ShouldBe(ChannelContent.One);
    }

    [Fact]
    public void BuildOptions_NoSmoothing_SetsAntiAliasNone()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        bmfcSave.UseSmoothing = false;

        // Act
        FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);

        // Assert
        options.AntiAlias.ShouldBe(AntiAliasMode.None);
    }

    [Fact]
    public void BuildOptions_WithOutline_SetsOutlineAlphaAndGlyphRgb()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        bmfcSave.OutlineThickness = 2;

        // Act
        FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);

        // Assert
        ChannelConfig channels = options.Channels!;
        channels.Alpha.ShouldBe(ChannelContent.Outline);
        channels.Red.ShouldBe(ChannelContent.Glyph);
        channels.Green.ShouldBe(ChannelContent.Glyph);
        channels.Blue.ShouldBe(ChannelContent.Glyph);
    }

    [Fact]
    public void BuildOptions_DefaultRanges_ProducesExpectedCodepoints()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        // Default ranges: "32-126,160-255" = 95 + 96 = 191 codepoints

        // Act
        FontGeneratorOptions options = GumFontGenerator.BuildOptions(bmfcSave);

        // Assert
        options.Characters.ShouldNotBeNull();
        options.Characters.Count.ShouldBe(191);
    }

    [Fact]
    public void Generate_DefaultBmfcSave_ProducesValidResult()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        bmfcSave.FontName = "Arial";
        bmfcSave.FontSize = 20;

        // Act
        BmFontResult result = GumFontGenerator.Generate(bmfcSave);

        // Assert
        result.ShouldNotBeNull();
        result.FntText.ShouldNotBeNullOrWhiteSpace();
        result.FntText.ShouldContain("face=\"Arial\"");
        result.Pages.Count.ShouldBeGreaterThan(0);
        result.Pages[0].PixelData.Length.ShouldBeGreaterThan(0);
        result.Pages[0].Width.ShouldBeGreaterThan(0);
        result.Pages[0].Height.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithOutline_ProducesValidResult()
    {
        // Arrange
        BmfcSave bmfcSave = new BmfcSave();
        bmfcSave.FontName = "Arial";
        bmfcSave.FontSize = 20;
        bmfcSave.OutlineThickness = 2;

        // Act
        BmFontResult result = GumFontGenerator.Generate(bmfcSave);

        // Assert
        result.ShouldNotBeNull();
        result.FntText.ShouldNotBeNullOrWhiteSpace();
        result.FntText.ShouldContain("outline=2");
        result.Pages.Count.ShouldBeGreaterThan(0);
        result.Pages[0].PixelData.Length.ShouldBeGreaterThan(0);
    }
}
