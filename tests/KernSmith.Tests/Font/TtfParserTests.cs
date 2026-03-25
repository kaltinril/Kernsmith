using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

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
        parser.Head.ShouldNotBeNull();
        parser.Head!.UnitsPerEm.ShouldBe(2048);
    }

    [Fact]
    public void Hhea_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Hhea.ShouldNotBeNull();
        parser.Hhea!.Ascender.ShouldBeGreaterThan(0);
        parser.Hhea!.Descender.ShouldBeLessThan(0, "hhea descender should be a negative value");
        parser.Hhea!.NumberOfHMetrics.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Os2_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Os2.ShouldNotBeNull();
        parser.Os2!.WeightClass.ShouldBe(400);
        parser.Os2!.Panose.Length.ShouldBe(10);
    }

    [Fact]
    public void Name_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.Names.ShouldNotBeNull();
        parser.Names!.FontFamily.ShouldBe("Roboto", "font family should be Roboto");
        parser.Names!.FontSubfamily.ShouldBe("Regular", "font subfamily should be Regular");
    }

    [Fact]
    public void Cmap_ParsesCorrectly()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert
        parser.CmapTable.ShouldNotBeEmpty();
        parser.CmapTable.ContainsKey(65).ShouldBeTrue();
        parser.CmapTable.ContainsKey(97).ShouldBeTrue();
        parser.CmapTable.ContainsKey(32).ShouldBeTrue();
    }

    [Fact]
    public void Cmap_DoesNotContainNegativeCodepoints()
    {
        // Arrange & Act
        var parser = CreateParser();

        // Assert — no codepoints should be negative
        parser.CmapTable.Keys.ShouldAllBe(cp => cp >= 0, "cmap should not contain negative codepoints");
    }

    [Fact]
    public void InvalidFont_ThrowsFontParsingException()
    {
        // Arrange
        var invalidData = new byte[] { 0, 0, 0, 0 };

        // Act
        var act = () => new TtfParser(invalidData);

        // Assert
        Should.Throw<FontParsingException>(act, "invalid font data should cause a parsing exception");
    }
}
