using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Core does the super sampling itself (render at Size x level, box-filter down once), so a
/// backend that would also honor <see cref="RasterOptions.SuperSample"/> must be told 1, or it
/// renders at level squared and rounds the bearings twice.
/// </summary>
public sealed class SuperSampleBackendTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void StbTrueType_SuperSampledGlyph_IsOneRenderAtTheLevelDownscaledOnce(int level)
    {
        const string chars = "AgW";
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars(chars),
            Rasterizer = new StbTrueTypeRasterizer(),
            SuperSampleLevel = level,
        };

        using var prepared = PreparedRasterization.Prepare(Font(), options, systemFontName: null, subsetToCharacterSet: true);
        var actual = prepared.RasterizeAndProcess(chars.Select(c => (int)c).ToList(), equalizeSpan: null).Glyphs;

        using var reference = new StbTrueTypeRasterizer();
        reference.LoadFont(Font());
        var renderOptions = RasterOptions.FromGeneratorOptions(options) with
        {
            Size = prepared.EffectiveSize * level,
            SuperSample = 1,
        };

        foreach (var glyph in actual)
        {
            var once = PreparedRasterization.SuperSampleDownscale(
                reference.RasterizeGlyph(glyph.Codepoint, renderOptions)!, level);

            var context = $"'{(char)glyph.Codepoint}' at {level}x";
            glyph.Width.ShouldBe(once.Width, context);
            glyph.Height.ShouldBe(once.Height, context);
            glyph.Metrics.ShouldBe(once.Metrics, context);
            glyph.BitmapData.ShouldBe(once.BitmapData, context);
        }
    }
}
