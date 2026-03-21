using KernSmith.Font;
using FluentAssertions;

namespace KernSmith.Tests.Font;

public class TtfFontReaderTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void ReadFont_ReturnsValidFontInfo()
    {
        // Arrange
        var reader = new TtfFontReader();
        var fontData = LoadTestFont();

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert
        fontInfo.FamilyName.Should().Be("Roboto", "font family should be Roboto");
        fontInfo.UnitsPerEm.Should().Be(2048, "Roboto has 2048 units per em");
        fontInfo.AvailableCodepoints.Count.Should().BeGreaterThan(0, "font should have available codepoints");
    }

    [Fact]
    public void ReadFont_MergesKerningPairs()
    {
        // Arrange
        var reader = new TtfFontReader();
        var fontData = LoadTestFont();

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert
        fontInfo.KerningPairs.Should().NotBeEmpty("Roboto Regular should have kerning pairs");
    }

    [Fact]
    public void ReadFont_AvailableCodepoints_ContainsAscii()
    {
        // Arrange
        var reader = new TtfFontReader();
        var fontData = LoadTestFont();

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert — printable ASCII range 32..126 should all be present
        var expectedCodepoints = Enumerable.Range(32, 95).ToList();
        fontInfo.AvailableCodepoints.Should().Contain(expectedCodepoints,
            "Roboto should contain all printable ASCII codepoints (U+0020 to U+007E)");
    }
}
