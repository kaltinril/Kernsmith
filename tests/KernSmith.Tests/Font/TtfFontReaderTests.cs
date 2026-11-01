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

    [Fact]
    public void ReadFont_DesignMetrics_HasEntryForEveryAvailableCodepoint()
    {
        // Arrange
        var reader = new TtfFontReader();
        var fontData = LoadTestFont();

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert
        fontInfo.DesignMetrics.Count.ShouldBe(fontInfo.AvailableCodepoints.Count);
        foreach (var cp in fontInfo.AvailableCodepoints)
            fontInfo.DesignMetrics.ContainsKey(cp).ShouldBeTrue($"codepoint {cp} should have design metrics");
    }

    [Fact]
    public void ReadFont_DesignMetrics_PlausibleAdvanceWidths()
    {
        // Arrange
        var reader = new TtfFontReader();
        var fontData = LoadTestFont();

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert — 'A', 'a', and space should have non-zero advance widths, well within
        // the unitsPerEm scale for this font.
        foreach (var cp in new[] { (int)'A', (int)'a', (int)' ' })
        {
            fontInfo.DesignMetrics.ContainsKey(cp).ShouldBeTrue($"codepoint {cp} should have design metrics");
            var metrics = fontInfo.DesignMetrics[cp];
            metrics.AdvanceWidth.ShouldBeGreaterThan(0, $"'{cp}' should have a non-zero advance width");
            metrics.AdvanceWidth.ShouldBeLessThan(fontInfo.UnitsPerEm, $"'{cp}' advance width should be less than unitsPerEm");
        }
    }

    [Fact]
    public void ReadFont_KerningCoversEveryCodepointOfASharedGlyph()
    {
        // Source Serif 4 maps U+02BC and U+2019 to one glyph (quoteright); fontTools gives
        // P quoteright = 20. Each codepoint of the glyph gets the pair, not just the lowest.
        var font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SourceSerif4-Regular.otf"));
        foreach (var requested in new HashSet<int>?[] { null, new() { 'P', 0x02BC, 0x2019 } })
        {
            var info = new TtfFontReader { RequestedCodepoints = requested }.ReadFont(font);
            int Kern(int left, int right) => info.KerningPairs
                .Where(p => p.LeftCodepoint == left && p.RightCodepoint == right)
                .Select(p => p.XAdvanceAdjustment).DefaultIfEmpty(0).Single();

            Kern('P', 0x02BC).ShouldBe(20, $"subset={requested?.Count}");
            Kern('P', 0x2019).ShouldBe(20, $"subset={requested?.Count}");
        }
    }
}
