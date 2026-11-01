using Shouldly;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// Regression for #262: <see cref="BmFont.QueryAtlasSize(byte[], FontGeneratorOptions?)"/> must size
/// each cell as generation does — at the cell-height ppem generation converts <c>Size</c> to
/// (unless <c>MatchCharHeight</c>), grown by baked outline and shadow effects, and at the final
/// size after an <c>SdfScale</c> or super-sampling downscale.
/// </summary>
[Collection("RasterizerFactory")]
public class QueryAtlasSizeAccuracyTests
{
    private static readonly byte[] FontData =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    public static TheoryData<string, RasterizerBackend> Cases()
    {
        var data = new TheoryData<string, RasterizerBackend>();
        foreach (var backend in new[] { RasterizerBackend.FreeType, RasterizerBackend.StbTrueType })
        {
            foreach (var name in Configs.Keys)
                data.Add(name, backend);
        }
        return data;
    }

    private static readonly Dictionary<string, Action<FontGeneratorOptions>> Configs = new()
    {
        ["plain"] = _ => { },
        ["sdf"] = o => o.Sdf = true,
        ["sdf-scale-2"] = o => { o.Sdf = true; o.SdfScale = 2; },
        ["outline-3"] = o => o.Outline = 3,
        ["shadow"] = o => { o.ShadowOffsetX = 3; o.ShadowOffsetY = -2; o.ShadowBlur = 2; },
        ["outline-shadow"] = o => { o.Outline = 2; o.ShadowOffsetX = 2; o.ShadowOffsetY = 2; o.ShadowBlur = 1; },
        ["supersample-2"] = o => o.SuperSampleLevel = 2,
        ["supersample-4"] = o => o.SuperSampleLevel = 4,
        ["outline-shadow-supersample-2"] = o => { o.SuperSampleLevel = 2; o.Outline = 2; o.ShadowOffsetX = 3; o.ShadowOffsetY = 3; o.ShadowBlur = 1; },
        ["height-150"] = o => o.HeightPercent = 150,
        ["bold"] = o => o.Bold = true,
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void QueryAtlasSize_GlyphAreaMatchesGeneration(string config, RasterizerBackend backend)
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = backend,
        };
        Configs[config](options);

        var query = BmFont.QueryAtlasSize(FontData, options);
        var result = BmFont.Generate(FontData, options);

        // The query reports glyph area / atlas area; recover the glyph area it packed.
        double queriedArea = (double)query.EstimatedEfficiency * query.Width * query.Height;
        double actualArea = result.Model.Characters
            .Where(c => c.Width > 0 && c.Height > 0)
            .Sum(c => (double)(c.Width + options.Padding.Left + options.Padding.Right + options.Spacing.Horizontal)
                      * (c.Height + options.Padding.Up + options.Padding.Down + options.Spacing.Vertical));

        (queriedArea / actualArea).ShouldBeInRange(0.99, 1.01, $"{config} on {backend}");
    }
}
