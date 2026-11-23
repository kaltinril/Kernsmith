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
    [InlineData(HintLevel.Light, AntiAliasMode.Grayscale, false, false)]
    [InlineData(HintLevel.Light, AntiAliasMode.Grayscale, true, true)]
    [InlineData(HintLevel.Light, AntiAliasMode.None, false, false)]
    [InlineData(HintLevel.Light, AntiAliasMode.Lcd, false, false)]
    [InlineData(HintLevel.Full, AntiAliasMode.Light, false, false)]
    [InlineData(HintLevel.None, AntiAliasMode.Light, true, false)]
    [InlineData(HintLevel.None, AntiAliasMode.None, true, true)]
    [InlineData(HintLevel.None, AntiAliasMode.Lcd, true, true)]
    public void GetGlyphMetrics_HintLevels_MatchRasterizedGlyph(HintLevel level, AntiAliasMode antiAlias, bool italic, bool bold)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());
        var options = new RasterOptions
        {
            Size = 24, Dpi = 72, AntiAlias = antiAlias, HintLevel = level, Italic = italic, Bold = bold
        };

        var mismatches = new List<string>();
        for (var cp = 32; cp < 127; cp++)
        {
            var rendered = rasterizer.RasterizeGlyph(cp, options)!.Metrics;
            var measured = rasterizer.GetGlyphMetrics(cp, options);
            if (measured != rendered)
                mismatches.Add($"'{(char)cp}': measured {measured}, rendered {rendered}");
        }

        mismatches.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void GetGlyphMetrics_Sdf_MatchesRasterizedGlyph(bool italic, bool bold, bool hinting)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());
        var options = new RasterOptions
        {
            Size = 32, Dpi = 72, Sdf = true, Italic = italic, Bold = bold, EnableHinting = hinting
        };

        foreach (var cp in " AgW%!")
            rasterizer.GetGlyphMetrics(cp, options).ShouldBe(rasterizer.RasterizeGlyph(cp, options)!.Metrics, $"'{cp}'");
    }

    [Theory]
    [InlineData("SourceSerif4-Regular.otf")]
    [InlineData("SourceCodeVF-Upright.otf")]
    [InlineData("RobotoFlex-Variable.ttf")]
    [InlineData("DancingScript-Variable.ttf")]
    public void GetGlyphMetrics_OtherOutlineFormats_MatchRasterizedGlyph(string fixture)
    {
        // The grayscale, light and SDF boxes are computed from the control box instead of rendered.
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));

        var mismatches = new List<string>();
        foreach (var size in new[] { 12.5f, 24f })
        foreach (var italic in new[] { false, true })
        foreach (var (antiAlias, sdf) in new[] { (AntiAliasMode.Grayscale, false), (AntiAliasMode.Light, false), (AntiAliasMode.Grayscale, true) })
        {
            var options = new RasterOptions { Size = size, Dpi = 72, AntiAlias = antiAlias, Sdf = sdf, Italic = italic };
            for (var cp = 32; cp < 127; cp++)
            {
                var rendered = rasterizer.RasterizeGlyph(cp, options)?.Metrics;
                var measured = rasterizer.GetGlyphMetrics(cp, options);
                if (measured != rendered)
                    mismatches.Add($"{size}px {antiAlias} sdf={sdf} italic={italic} '{(char)cp}': measured {measured}, rendered {rendered}");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Lcd)]
    public void GetGlyphMetrics_ColrGlyph_MatchesRasterizedGlyph(AntiAliasMode antiAlias)
    {
        var fontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Color", "BungeeColor-Regular.ttf"));
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(fontData);
        var options = new RasterOptions { Size = 32, Dpi = 72, ColorFont = true, AntiAlias = antiAlias };

        foreach (var cp in " AgW%!")
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
