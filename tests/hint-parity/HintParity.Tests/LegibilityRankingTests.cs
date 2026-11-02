using KernSmith;
using KernSmith.HintParity.Legibility;
using Shouldly;

namespace KernSmith.HintParity.Tests;

/// <summary>
/// The score on real output must agree with what a reader sees: a hinted small text size beats
/// the unhinted one, and the same unhinted font is more legible at 24 px than at 10 px. The
/// FreeType-versus-Native-Normal order is deliberately not asserted here: the two score within
/// a few thousandths of each other at 12 px, and the hinter is under active tuning.
/// </summary>
public class LegibilityRankingTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(ParityMatrix.FixturesDirectory(), "Roboto-Regular.ttf"));

    private static double Score(byte[] font, int size, AntiAliasMode aa, HintLevel level, bool hinting)
    {
        var generated = GeneratedFont.Generate(font, new FontGeneratorOptions
        {
            Size = size,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            AntiAlias = aa,
            EnableHinting = hinting,
            HintLevel = level,
            Kerning = false,
        });
        var ideal = IdealFont.Render(font, size, CharacterSet.Ascii);
        return LegibilityScore.Measure(generated.Glyphs, ideal, aa == AntiAliasMode.None).Score;
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public void NormalHinting_ScoresAboveUnhinted_AtTextSizes_Grayscale(int size)
    {
        var font = Roboto();
        double normal = Score(font, size, AntiAliasMode.Grayscale, HintLevel.Normal, hinting: true);
        double unhinted = Score(font, size, AntiAliasMode.Grayscale, HintLevel.Default, hinting: false);
        normal.ShouldBeGreaterThan(unhinted + 0.02, $"Normal {normal:0.000} vs unhinted {unhinted:0.000} at {size} px");
    }

    [Theory]
    [InlineData("grayscale")]
    [InlineData("none")]
    public void Unhinted_ScoresHigherAt24Than10(string aa)
    {
        var font = Roboto();
        var mode = ParityMatrix.ParseAntiAlias(aa);
        double large = Score(font, 24, mode, HintLevel.Default, hinting: false);
        double small = Score(font, 10, mode, HintLevel.Default, hinting: false);
        large.ShouldBeGreaterThan(small + 0.05, $"24 px {large:0.000} vs 10 px {small:0.000}");
    }
}
