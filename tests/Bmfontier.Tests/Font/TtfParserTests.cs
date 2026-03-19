using Bmfontier.Font;
using FluentAssertions;

namespace Bmfontier.Tests.Font;

public class TtfParserTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static TtfParser CreateParser() => new(LoadTestFont());

    [Fact]
    public void Head_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Head.Should().NotBeNull();
        parser.Head!.UnitsPerEm.Should().Be(2048);
    }

    [Fact]
    public void Hhea_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Hhea.Should().NotBeNull();
        parser.Hhea!.Ascender.Should().BePositive("hhea ascender should be a positive value");
        parser.Hhea!.Descender.Should().BeNegative("hhea descender should be a negative value");
        parser.Hhea!.NumberOfHMetrics.Should().BeGreaterThan(0, "font should have at least one horizontal metric");
    }

    [Fact]
    public void Os2_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Os2.Should().NotBeNull();
        parser.Os2!.WeightClass.Should().Be(400, "Roboto Regular has weight class 400");
        parser.Os2!.Panose.Should().HaveCount(10, "PANOSE classification is always 10 bytes");
    }

    [Fact]
    public void Name_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Names.Should().NotBeNull();
        parser.Names!.FontFamily.Should().Be("Roboto", "font family should be Roboto");
        parser.Names!.FontSubfamily.Should().Be("Regular", "font subfamily should be Regular");
    }

    [Fact]
    public void Cmap_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.CmapTable.Should().NotBeEmpty("cmap table should contain character mappings");
        parser.CmapTable.Should().ContainKey(65, "cmap should contain mapping for 'A' (U+0041)");
        parser.CmapTable.Should().ContainKey(97, "cmap should contain mapping for 'a' (U+0061)");
        parser.CmapTable.Should().ContainKey(32, "cmap should contain mapping for space (U+0020)");
    }

    [Fact]
    public void Cmap_DoesNotContainNegativeCodepoints()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert — no codepoints should be negative
        parser.CmapTable.Keys.Should().OnlyContain(cp => cp >= 0, "cmap should not contain negative codepoints");
    }

    [Fact]
    public void InvalidFont_ThrowsFontParsingException()
    {
        // Arrange
        var invalidData = new byte[] { 0, 0, 0, 0 };

        // Act
        var act = () => new TtfParser(invalidData);

        // Assert
        act.Should().Throw<FontParsingException>("invalid font data should cause a parsing exception");
    }
}
