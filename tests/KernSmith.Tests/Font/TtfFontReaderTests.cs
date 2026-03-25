using KernSmith.Font;
using Shouldly;

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
        fontInfo.FamilyName.ShouldBe("Roboto", "font family should be Roboto");
        fontInfo.UnitsPerEm.ShouldBe(2048);
        fontInfo.AvailableCodepoints.Count.ShouldBeGreaterThan(0);
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
        fontInfo.KerningPairs.ShouldNotBeEmpty();
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
        foreach (var cp in expectedCodepoints)
            fontInfo.AvailableCodepoints.ShouldContain(cp);
    }
}
