using KernSmith.HintParity.Legibility;
using Shouldly;

namespace KernSmith.HintParity.Tests;

/// <summary>
/// The strip-level features the pairwise page's fit regresses on (<c>fit_bt.py</c>), computed
/// here from a glyph set exactly as that script computes them from a strip PNG, so a fitted
/// <c>weights.json</c> transfers; and the <c>--weights</c> file formats that override the
/// score's term weights.
/// </summary>
public class LegibilityFeaturesTests
{
    private static readonly string[] Lines = ["oo", "o l"];

    private static Dictionary<int, GlyphBitmap> RingsAndStem() => new()
    {
        ['o'] = FromRows(["255 255 255", "255 0 255", "255 255 255"], 0, 0, advance: 4, id: 'o'),
        ['l'] = FromRows(["255", "255", "255", "255"], 1, 0, advance: 3, id: 'l'),
        [' '] = new GlyphBitmap(' ', 0, 0, 0, 0, 0, 2, []),
    };

    [Fact]
    public void Features_FollowTheirDefinitions_OnASolidStrip()
    {
        var f = LegibilityFeatures.Measure(RingsAndStem(), lineHeight: 5, size: 5, Lines);

        f.InkConcentration.ShouldBe(1.0, 1e-9);
        f.MeanInk.ShouldBe(1.0, 1e-9);
        f.Sharpness.ShouldBeGreaterThan(0);
        f.Holes128.ShouldBe(3);
        f.BrokenRings.ShouldBe(0);
        f.TopologyError.ShouldBe(0);
        // Mid-x-height row: every run is one pixel wide (ring sides and the stem).
        f.StemWidthVar.ShouldBe(0.0, 1e-9);
        // "oo": ring ends at column 3, next ring starts at 4 -> gap 1. "o l": ring ends at 3,
        // the space advances 2, the stem sits at pen 6 + offset 1 = 7 -> gap 4 (under 0.6 x size? no:
        // 4 > 3, so the word space is excluded, as the script excludes it).
        f.GapMean.ShouldBe(1.0, 1e-9);
        f.GapVar.ShouldBe(0.0, 1e-9);
        f.TouchingPairs.ShouldBe(0);
        f.Named().Select(p => p.Name).ShouldBe(LegibilityFeatures.Names);
    }

    [Fact]
    public void Features_SeeABrokenRing_ATouchingPair_AndBlur()
    {
        var glyphs = RingsAndStem();
        glyphs['o'] = FromRows(["255 255 255", "100 0 255", "255 255 255"], 0, 0, advance: 3, id: 'o');

        var f = LegibilityFeatures.Measure(glyphs, lineHeight: 5, size: 5, Lines);

        // Every ring is whole at 64 and open at 128 — but the features are measured on the
        // composed strip, as the script measures them: with advance 3 the second ring's weak
        // left side abuts the first ring's solid right column, so its hole survives at 128.
        f.BrokenRings.ShouldBe(2);
        f.Holes128.ShouldBe(1);
        f.TouchingPairs.ShouldBe(1);
        f.InkConcentration.ShouldBeLessThan(1.0);
        f.MeanInk.ShouldBeLessThan(1.0);

        var crisp = LegibilityFeatures.Measure(RingsAndStem(), 5, 5, Lines);
        f.Sharpness.ShouldBeLessThan(crisp.Sharpness);
    }

    [Fact]
    public void Features_TopologyError_IsComponentsAgainstTheTextsExpectedCount()
    {
        // 'o' drawn as two halves: one component too many per 'o'; three on the strip.
        var glyphs = RingsAndStem();
        glyphs['o'] = FromRows(["255 0 255", "255 0 255", "255 0 255"], 0, 0, advance: 4, id: 'o');

        LegibilityFeatures.Measure(glyphs, 5, 5, Lines).TopologyError.ShouldBe(3);
        LegibilityFeatures.ExpectedComponents("i%:").ShouldBe(2 + 3 + 2);
    }

    [Fact]
    public void Weights_TermForm_NormalisesToOne_AndFillsMissingTermsWithZero()
    {
        var (weights, notes) = LegibilityWeights.Parse("""{"topology": 1, "fidelity": 3}""");

        weights["topology"].ShouldBe(0.25, 1e-9);
        weights["fidelity"].ShouldBe(0.75, 1e-9);
        weights["spacing"].ShouldBe(0.0);
        weights.Keys.ShouldBe(LegibilityScore.DefaultWeights.Keys, ignoreOrder: true);
        weights.Values.Sum().ShouldBe(1.0, 1e-9);
        notes.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("""{"topology": 1, "colour": 1}""", "colour")]
    [InlineData("""{"topology": -1, "fidelity": 2}""", "topology")]
    [InlineData("""{"topology": 0, "fidelity": 0}""", "sum")]
    [InlineData("""[1, 2]""", "object")]
    [InlineData("""{"topology": "high"}""", "topology")]
    public void Weights_TermForm_RejectsUnknownNegativeNonNumericAndAllZero(string json, string mention)
    {
        var e = Should.Throw<ArgumentException>(() => LegibilityWeights.Parse(json));
        e.Message.ShouldContain(mention, Case.Sensitive);
    }

    [Fact]
    public void Weights_FitForm_MapsEachFeatureToItsTerm_ByTheSignTheTermRewards()
    {
        // A fit_bt.py weights.json: positive means the owner preferred the feature higher.
        const string json = """
            {
              "note": "...",
              "weights_per_sd": {
                "ink_concentration": 0.8,
                "mean_ink": 0.2,
                "sharpness": 0.4,
                "broken_rings": -0.6,
                "holes_128": 0.3,
                "topology_error": -0.5,
                "stem_width_var": -0.2,
                "gap_var": -0.1,
                "gap_mean": 0.7,
                "touching_pairs": -0.3
              },
              "feature_sd": {}
            }
            """;

        var (weights, notes) = LegibilityWeights.Parse(json);

        // Magnitudes, per term: concentration 0.8, fidelity 0.2, alignment 0.4, brokenRings 0.6,
        // topology 0.3, topologyError 0.5, stems 0.2, spacing 0.1 + 0.3 = 0.4; separability has
        // no proxy (0); gap_mean has no term and is ignored. Total 3.4.
        weights["concentration"].ShouldBe(0.8 / 3.4, 1e-9);
        weights["brokenRings"].ShouldBe(0.6 / 3.4, 1e-9);
        weights["spacing"].ShouldBe(0.4 / 3.4, 1e-9);
        weights["separability"].ShouldBe(0.0);
        weights.Values.Sum().ShouldBe(1.0, 1e-9);
        notes.ShouldContain(n => n.Contains("separability", StringComparison.Ordinal));
        notes.ShouldContain(n => n.Contains("gap_mean", StringComparison.Ordinal));
    }

    [Fact]
    public void Weights_FitForm_DropsAFeatureWhoseSignContradictsItsTerm_AndRejectsAllContradictions()
    {
        // The owner "preferred" more broken rings: the term rewards fewer, so the feature adds nothing.
        var (weights, notes) = LegibilityWeights.Parse("""{"weights_per_sd": {"broken_rings": 0.5, "ink_concentration": 0.5}}""");
        weights["brokenRings"].ShouldBe(0.0);
        weights["concentration"].ShouldBe(1.0, 1e-9);
        notes.ShouldContain(n => n.Contains("broken_rings", StringComparison.Ordinal));

        Should.Throw<ArgumentException>(() => LegibilityWeights.Parse("""{"weights_per_sd": {"broken_rings": 0.5}}"""));
        Should.Throw<ArgumentException>(() => LegibilityWeights.Parse("""{"weights_per_sd": {"not_a_feature": 0.5}}"""));
    }

    [Fact]
    public void Score_UsesTheWeightsItIsGiven()
    {
        var crisp = LegibilityTopologyTermTests.CrispFont();
        var ideal = IdealFont.FromHighRes(LegibilityTopologyTermTests.Upscale(crisp, 4), 4);
        var terms = LegibilityScore.Measure(crisp, ideal, mono: false);

        var (onlyStems, _) = LegibilityWeights.Parse("""{"stems": 2}""");
        terms.ScoreWith(onlyStems).ShouldBe(terms.Stems, 1e-9);
        terms.ScoreWith(LegibilityScore.DefaultWeights).ShouldBe(terms.Score, 1e-9);
    }

    private static GlyphBitmap FromRows(string[] rows, int xOffset, int yOffset, int advance, int id)
    {
        var parsed = rows.Select(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(byte.Parse).ToArray()).ToArray();
        int width = parsed[0].Length;
        var pixels = new byte[width * parsed.Length];
        for (int y = 0; y < parsed.Length; y++)
            Array.Copy(parsed[y], 0, pixels, y * width, width);
        // Features set lines by the .fnt yoffset; the baseline-relative top is not used by them.
        return new GlyphBitmap(id, width, parsed.Length, xOffset, yOffset, yOffset - 4, advance, pixels);
    }
}
