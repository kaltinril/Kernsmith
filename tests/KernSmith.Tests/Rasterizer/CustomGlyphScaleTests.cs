using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// A <see cref="CustomGlyph"/> is supplied at the final size: render-scale options
/// (<see cref="FontGeneratorOptions.SuperSampleLevel"/>, <see cref="FontGeneratorOptions.SdfScale"/>)
/// enlarge only the font's own glyphs, so the custom image must not be downscaled with them.
/// </summary>
public sealed class CustomGlyphScaleTests
{
    private const int Star = 0x2605;

    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(1, 2)]
    public void CustomGlyph_KeepsItsSuppliedSizeAndPixels(int superSample, int sdfScale)
    {
        var pixels = Enumerable.Range(0, 6 * 4).Select(i => (byte)(i * 10)).ToArray();
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            Rasterizer = new StbTrueTypeRasterizer(),
            SuperSampleLevel = superSample,
            Sdf = sdfScale > 1,
            SdfScale = sdfScale,
            CustomGlyphs = new Dictionary<int, CustomGlyph>
            {
                [Star] = new CustomGlyph(6, 4, pixels, PixelFormat.Grayscale8, XAdvance: 8),
            },
        };

        using var prepared = PreparedRasterization.Prepare(Font(), options, systemFontName: null, subsetToCharacterSet: true);
        var glyph = prepared.RasterizeAndProcess(prepared.ResolveCodepoints(), equalizeSpan: null)
            .Glyphs.Single(g => g.Codepoint == Star);

        glyph.Width.ShouldBe(6);
        glyph.Height.ShouldBe(4);
        glyph.Metrics.Advance.ShouldBe(8);
        glyph.Metrics.BearingY.ShouldBe(4);
        glyph.BitmapData.ShouldBe(pixels);
    }
}
