using System.Buffers.Binary;
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

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Mono_BlankGlyph_HasAnEmptyBox_LikeEveryOtherMode(bool hinting, bool bold, bool italic)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());

        foreach (var size in new[] { 13f, 24f, 37f })
        {
            var mono = new RasterOptions
            {
                Size = size, Dpi = 72, AntiAlias = AntiAliasMode.None, EnableHinting = hinting, Bold = bold, Italic = italic
            };
            var gray = mono with { AntiAlias = AntiAliasMode.Grayscale };

            var rendered = rasterizer.RasterizeGlyph(' ', mono)!;
            var reference = rasterizer.RasterizeGlyph(' ', gray)!;

            (rendered.Width, rendered.Height).ShouldBe((0, 0), $"size {size}");
            rendered.BitmapData.ShouldBeEmpty();
            (rendered.Metrics.BearingX, rendered.Metrics.BearingY, rendered.Metrics.Width, rendered.Metrics.Height)
                .ShouldBe((reference.Metrics.BearingX, reference.Metrics.BearingY, 0, 0), $"size {size}");
            rasterizer.GetGlyphMetrics(' ', mono).ShouldBe(rendered.Metrics, $"size {size}");
        }
    }

    [Fact]
    public void Mono_ColrGlyphWithAnEmptyBaseOutline_KeepsItsLayers()
    {
        // A COLR base glyph may have no outline of its own, so a blank-outline check must not
        // empty a glyph FreeType drew from its layers.
        var fontData = WithEmptyGlyph(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Color", "BungeeColor-Regular.ttf")), 'A');
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(fontData);
        var options = new RasterOptions { Size = 32, Dpi = 72, ColorFont = true, AntiAlias = AntiAliasMode.None };

        rasterizer.RasterizeGlyph('A', options with { ColorFont = false })!.Width.ShouldBe(0, "the base outline must be empty");
        var glyph = rasterizer.RasterizeGlyph('A', options)!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Width.ShouldBeGreaterThan(1);
        glyph.Height.ShouldBeGreaterThan(1);
        rasterizer.GetGlyphMetrics('A', options).ShouldBe(glyph.Metrics);
    }

    /// <summary>A copy of a TrueType font whose <c>loca</c> gives the codepoint's glyph no data.</summary>
    private static byte[] WithEmptyGlyph(byte[] font, int codepoint)
    {
        var copy = (byte[])font.Clone();
        var glyph = new KernSmith.Font.TtfParser(font).CmapTable[codepoint];

        int TableOffset(string tag)
        {
            int tables = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(4));
            for (var i = 0; i < tables; i++)
            {
                var record = 12 + (i * 16);
                if (System.Text.Encoding.ASCII.GetString(copy, record, 4) == tag)
                    return (int)BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(record + 8));
            }
            throw new InvalidOperationException($"no {tag} table");
        }

        var loca = TableOffset("loca");
        if (BinaryPrimitives.ReadInt16BigEndian(copy.AsSpan(TableOffset("head") + 50)) == 0)
            BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(loca + (glyph * 2)), BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(loca + ((glyph + 1) * 2))));
        else
            BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(loca + (glyph * 4)), BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(loca + ((glyph + 1) * 4))));
        return copy;
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
