using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Super-sampled metrics are the high-resolution metrics divided by the level and rounded to the
/// nearest pixel, on every backend. Integer division truncated them: at Roboto 12px x4 a quarter
/// of the ASCII advances came out a pixel short ('#' 22/4 = 5.5 px gave 5), so super-sampled
/// text was cramped.
/// </summary>
public sealed class SuperSampleAdvanceTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static IRasterizer Backend(string name) => name switch
    {
        "StbTrueType" => new StbTrueTypeRasterizer(),
        "FreeType" => new FreeTypeRasterizer(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [InlineData("StbTrueType", 2)]
    [InlineData("StbTrueType", 4)]
    [InlineData("FreeType", 4)]
    public void SuperSampledAdvance_IsTheHighResAdvanceRoundedToTheNearestPixel(string backend, int level)
    {
        var codepoints = Enumerable.Range('!', '~' - '!' + 1).ToList();
        var options = new FontGeneratorOptions
        {
            Size = 12,
            Characters = CharacterSet.Ascii,
            Rasterizer = Backend(backend),
            SuperSampleLevel = level,
        };

        using var prepared = PreparedRasterization.Prepare(Font(), options, systemFontName: null, subsetToCharacterSet: true);
        var glyphs = prepared.RasterizeAndProcess(codepoints, equalizeSpan: null).Glyphs;

        using var reference = Backend(backend);
        reference.LoadFont(Font());
        var hiRes = RasterOptions.FromGeneratorOptions(options) with
        {
            Size = prepared.EffectiveSize * level,
            SuperSample = 1,
        };

        var failures = new List<string>();
        var roundUp = 0;
        foreach (var glyph in glyphs)
        {
            var hi = reference.GetGlyphMetrics(glyph.Codepoint, hiRes)!.Value;
            var expected = (int)Math.Round(hi.Advance / (double)level, MidpointRounding.AwayFromZero);
            if (hi.Advance % level * 2 >= level)
                roundUp++;
            if (glyph.Metrics.Advance != expected)
                failures.Add($"'{(char)glyph.Codepoint}': {hi.Advance}/{level} = {hi.Advance / (double)level:0.00} px gave {glyph.Metrics.Advance}, expected {expected}");
        }

        failures.ShouldBeEmpty();
        // Truncation and rounding agree on a glyph whose remainder is under half the level; the
        // set must hold glyphs where they differ, or this test would pass against truncation.
        roundUp.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("StbTrueType")]
    [InlineData("FreeType")]
    public void SuperSampledText_KeepsThePenWithinTwoPixelsOfTheUnsampledLayout(string backend)
    {
        // Typeset a sentence (unhinted, so FreeType's x1 advances are not the auto-hinter's
        // rebuilt ones) by accumulating advances at x1 and at x4. Each super-sampled advance and
        // bearing is the nearest pixel to its true value, as the x1 ones are; the two can still
        // differ by one on a glyph whose true advance is within 1/8 px of a half (the high-res
        // advance was rounded first: Roboto 'T' 5.4 px is 5 at x1, 22/4 = 5.5 -> 6 at x4), so the
        // pen may drift a pixel or two over a sentence (measured: 177 vs 179 px on both
        // backends), never more. With truncation it lost a pixel on a quarter of the glyphs and
        // was 4 px behind by the 14th glyph. Bearings stay within a pixel of the x1 ones.
        const string text = "The quick brown fox jumps over the lazy dog";
        var plain = Layout(backend, text, level: 1);
        var sampled = Layout(backend, text, level: 4);

        var failures = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            if (Math.Abs(sampled.Pen[i] - plain.Pen[i]) > 2)
                failures.Add($"pen before '{text[i]}' ({i}): x4 {sampled.Pen[i]} vs x1 {plain.Pen[i]}");
            if (Math.Abs(sampled.BearingX[i] - plain.BearingX[i]) > 1)
                failures.Add($"bearing of '{text[i]}' ({i}): x4 {sampled.BearingX[i]} vs x1 {plain.BearingX[i]}");
        }

        failures.ShouldBeEmpty();
        Math.Abs(sampled.Pen[text.Length] - plain.Pen[text.Length]).ShouldBeLessThanOrEqualTo(2, "sentence width");
    }

    /// <summary>Pen position before each glyph (and after the last) and each glyph's left bearing.</summary>
    private static (int[] Pen, int[] BearingX) Layout(string backend, string text, int level)
    {
        var options = new FontGeneratorOptions
        {
            Size = 12,
            Characters = CharacterSet.FromChars(text),
            Rasterizer = Backend(backend),
            SuperSampleLevel = level,
            EnableHinting = false,
        };

        using var prepared = PreparedRasterization.Prepare(Font(), options, systemFontName: null, subsetToCharacterSet: true);
        var glyphs = prepared.RasterizeAndProcess(text.Distinct().Select(c => (int)c).ToList(), equalizeSpan: null).Glyphs
            .ToDictionary(g => g.Codepoint, g => g.Metrics);

        var pen = new int[text.Length + 1];
        var bearing = new int[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var m = glyphs[text[i]];
            bearing[i] = m.BearingX;
            pen[i + 1] = pen[i] + m.Advance;
        }

        return (pen, bearing);
    }
}
