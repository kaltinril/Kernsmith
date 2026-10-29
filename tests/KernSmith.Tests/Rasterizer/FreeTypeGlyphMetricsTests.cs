using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// <see cref="FreeTypeRasterizer.GetGlyphMetrics"/> must describe exactly the bitmap
/// <see cref="FreeTypeRasterizer.RasterizeGlyph"/> produces for the same options — synthetic
/// italic and bold included, since both reshape the outline after FreeType fills in the slot
/// metrics.
/// </summary>
public class FreeTypeGlyphMetricsTests
{
    private static byte[] LoadRoboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData(AntiAliasMode.Grayscale, true, false, false)]
    [InlineData(AntiAliasMode.Grayscale, false, false, false)]
    [InlineData(AntiAliasMode.Grayscale, true, true, false)]
    [InlineData(AntiAliasMode.Grayscale, false, true, false)]
    [InlineData(AntiAliasMode.Grayscale, true, false, true)]
    [InlineData(AntiAliasMode.Grayscale, true, true, true)]
    [InlineData(AntiAliasMode.Light, true, true, false)]
    [InlineData(AntiAliasMode.None, true, false, false)]
    [InlineData(AntiAliasMode.None, true, true, false)]
    [InlineData(AntiAliasMode.Lcd, true, false, false)]
    [InlineData(AntiAliasMode.Lcd, true, true, false)]
    public void GetGlyphMetrics_MatchesRasterizedGlyph(AntiAliasMode antiAlias, bool hinting, bool italic, bool bold)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());

        var mismatches = new List<string>();
        foreach (var size in new[] { 13f, 24f, 37f })
        {
            var options = new RasterOptions
            {
                Size = size, Dpi = 72, AntiAlias = antiAlias, EnableHinting = hinting, Italic = italic, Bold = bold
            };
            for (var cp = 32; cp < 127; cp++)
            {
                var rendered = rasterizer.RasterizeGlyph(cp, options)!.Metrics;
                var measured = rasterizer.GetGlyphMetrics(cp, options);
                if (measured != rendered)
                    mismatches.Add($"{size}px '{(char)cp}': measured {measured}, rendered {rendered}");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetGlyphMetrics_Sdf_MatchesRasterizedGlyph(bool italic)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());
        var options = new RasterOptions { Size = 32, Dpi = 72, Sdf = true, Italic = italic };

        foreach (var cp in "AgW%!")
            rasterizer.GetGlyphMetrics(cp, options).ShouldBe(rasterizer.RasterizeGlyph(cp, options)!.Metrics, $"'{cp}'");
    }

    [Fact]
    public void GetGlyphMetrics_ColorBitmapGlyph_MatchesRasterizedGlyph()
    {
        var fontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoColorEmoji.ttf"));
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(fontData);
        var options = new RasterOptions { Size = 48, Dpi = 72, ColorFont = true };

        foreach (var cp in new[] { 0x1F600, 0x2764, 0x2B50 })
            rasterizer.GetGlyphMetrics(cp, options).ShouldBe(rasterizer.RasterizeGlyph(cp, options)!.Metrics, $"U+{cp:X}");
    }
}
