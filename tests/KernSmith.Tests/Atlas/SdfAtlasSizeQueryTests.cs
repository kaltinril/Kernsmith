using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// Regression for #262: <see cref="BmFont.QueryAtlasSize(byte[], FontGeneratorOptions?)"/> sizes
/// each cell from <see cref="IRasterizer.GetGlyphMetrics"/>, so for SDF those metrics must include
/// the distance field's padding (the spread on every side), as the rasterized glyph does.
/// </summary>
[Collection("RasterizerFactory")]
public class SdfAtlasSizeQueryTests
{
    private static readonly byte[] FontData =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    public static TheoryData<string, bool, int> Cases() => new()
    {
        { "FreeType", false, 8 },
        { "FreeType", true, 8 },
        { "FreeType", false, 4 },
        { "StbTrueType", false, 8 },
        { "StbTrueType", true, 8 },
        { "StbTrueType", false, 4 },
    };

    private static IRasterizer Create(string backend) => backend switch
    {
        "FreeType" => new FreeTypeRasterizer(),
        _ => new StbTrueTypeRasterizer(),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void GetGlyphMetrics_Sdf_MatchesRasterizedGlyphBox(string backend, bool bold, int spread) =>
        AssertMetricsMatchGlyphs(backend, new RasterOptions { Size = 32, Sdf = true, SdfSpread = spread, Bold = bold });

    // #276: FreeType's synthetic bold box grew by twice the embolden strength, though
    // FT_Outline_Embolden grows the outline by the strength in all.
    [Theory]
    [InlineData("FreeType", 32f)]
    [InlineData("FreeType", 12f)]
    [InlineData("FreeType", 72f)]
    [InlineData("StbTrueType", 32f)]
    public void GetGlyphMetrics_SyntheticBold_MatchesRasterizedGlyphBox(string backend, float size) =>
        AssertMetricsMatchGlyphs(backend, new RasterOptions { Size = size, Bold = true });

    private static void AssertMetricsMatchGlyphs(string backend, RasterOptions options)
    {
        using var rasterizer = Create(backend);
        rasterizer.LoadFont(FontData);

        var mismatches = new List<string>();
        for (int cp = 33; cp <= 126; cp++)
        {
            var metrics = rasterizer.GetGlyphMetrics(cp, options);
            var glyph = rasterizer.RasterizeGlyph(cp, options);
            if (metrics is null || glyph is null)
                continue;

            var m = metrics.Value;
            var g = glyph.Metrics;
            // Rounding only: the metrics come from the outline, the glyph from its bitmap box.
            if (Math.Abs(m.Width - g.Width) > 1 || Math.Abs(m.Height - g.Height) > 1
                || Math.Abs(m.BearingX - g.BearingX) > 1 || Math.Abs(m.BearingY - g.BearingY) > 1)
            {
                mismatches.Add($"'{(char)cp}': metrics {m.Width}x{m.Height} @({m.BearingX},{m.BearingY}), " +
                               $"glyph {g.Width}x{g.Height} @({g.BearingX},{g.BearingY})");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
    public void QueryAtlasSize_Sdf_GlyphAreaMatchesGeneration(RasterizerBackend backend)
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Sdf = true,
            Backend = backend,
            // Em-square sizing: the query does not apply the cell-height ppem conversion
            // generation uses, a separate gap this test keeps out of the comparison.
            MatchCharHeight = true,
        };

        var query = BmFont.QueryAtlasSize(FontData, options);
        var result = BmFont.Generate(FontData, options);

        // The query reports glyph area / atlas area; recover the glyph area it packed.
        double queriedArea = (double)query.EstimatedEfficiency * query.Width * query.Height;
        double actualArea = result.Model.Characters
            .Where(c => c.Width > 0 && c.Height > 0)
            .Sum(c => (double)(c.Width + options.Padding.Left + options.Padding.Right + options.Spacing.Horizontal)
                      * (c.Height + options.Padding.Up + options.Padding.Down + options.Spacing.Vertical));

        (queriedArea / actualArea).ShouldBeInRange(0.95, 1.05);
    }
}
