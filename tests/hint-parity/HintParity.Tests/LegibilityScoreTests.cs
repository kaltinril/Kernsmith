using KernSmith.HintParity.Legibility;
using Shouldly;

namespace KernSmith.HintParity.Tests;

/// <summary>
/// The legibility metric's terms on synthetic bitmaps: each term must reward what a reader
/// sees as clearer (solid ink, intact counters, separable letters, crisp alignment lines, even
/// stems and spacing) and must not reward a mere translation.
/// </summary>
public class LegibilityScoreTests
{
    // ---- ink concentration --------------------------------------------------------------------

    [Fact]
    public void InkConcentration_SolidInkScoresOne_HalfToneScoresHalf()
    {
        LegibilityMetrics.InkConcentration([Block(4, 4, 255)]).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.InkConcentration([Block(4, 4, 128)]).ShouldBe(128 / 255.0, 1e-9);
        LegibilityMetrics.InkConcentration([Empty()]).ShouldBe(0.0);
    }

    [Fact]
    public void InkConcentration_AStemSplitOverTwoColumns_ScoresBelowOneSolidColumn()
    {
        var solid = FromRows(["0 255 0"], 0, -1);
        var split = FromRows(["128 128 0"], 0, -1);
        LegibilityMetrics.InkConcentration([split]).ShouldBeLessThan(LegibilityMetrics.InkConcentration([solid]));
    }

    // ---- topology -------------------------------------------------------------------------------

    [Fact]
    public void Topology_CountsInkComponentsAndHoles()
    {
        var ring = FromRows(
        [
            "255 255 255",
            "255   0 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.TopologyOf(ring).ShouldBe(new Topology(1, 1));

        var brokenRing = FromRows(
        [
            "255 255 255",
            "255   0 255",
            "255   0 255",
        ], 0, -3);
        LegibilityMetrics.TopologyOf(brokenRing).ShouldBe(new Topology(1, 0));

        var dottedI = FromRows(
        [
            "255",
            "  0",
            "255",
            "255",
        ], 0, -4);
        LegibilityMetrics.TopologyOf(dottedI).ShouldBe(new Topology(2, 0));

        var eight = FromRows(
        [
            "255 255 255",
            "255   0 255",
            "255 255 255",
            "255   0 255",
            "255 255 255",
        ], 0, -5);
        LegibilityMetrics.TopologyOf(eight).ShouldBe(new Topology(1, 2));

        LegibilityMetrics.TopologyOf(Empty()).ShouldBe(new Topology(0, 0));
    }

    [Fact]
    public void Topology_AGrayFilledCounter_ReadsAsClosedAtTheThreshold()
    {
        // The 'e' eye that fills with 60% gray at 9 px is what a reader sees as a blob.
        var muddy = FromRows(
        [
            "255 255 255",
            "255 153 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.TopologyOf(muddy).ShouldBe(new Topology(1, 0));

        var faint = FromRows(
        [
            "255 255 255",
            "255  60 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.TopologyOf(faint).ShouldBe(new Topology(1, 1));
    }

    [Fact]
    public void TopologyPreservation_CreditsContrastMargin_NotJustTheMidThreshold()
    {
        // The true shape: a stroke with a dot above it ('i').
        var truth = new Topology(2, 0);
        var solidDot = FromRows(["255", "0", "255", "255"], 0, -4);
        var faintDot = FromRows(["110", "0", "255", "255"], 0, -4);
        var ghostDot = FromRows(["40", "0", "255", "255"], 0, -4);

        LegibilityMetrics.TopologyPreservation(solidDot, truth).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.TopologyPreservation(faintDot, truth).ShouldBe(1 / 3.0, 1e-9);
        LegibilityMetrics.TopologyPreservation(ghostDot, truth).ShouldBe(0.0, 1e-9);
    }

    // ---- separability ---------------------------------------------------------------------------

    [Fact]
    public void Separation_IdenticalIsZero_DisjointIsOne_TranslationIsForgiven()
    {
        var a = Block(2, 3, 255, xOffset: 0);
        LegibilityMetrics.Separation(a, a).ShouldBe(0.0, 1e-9);

        var shifted = Block(2, 3, 255, xOffset: 1);
        LegibilityMetrics.Separation(a, shifted).ShouldBe(0.0, 1e-9);

        var farAway = Block(2, 3, 255, xOffset: 10);
        LegibilityMetrics.Separation(a, farAway).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void Separation_ABarMakesACIntoAnE_AndBlurShrinksTheDifference()
    {
        var c = FromRows(
        [
            "255 255 255",
            "255   0   0",
            "255   0   0",
            "255 255 255",
        ], 0, -4);
        var e = FromRows(
        [
            "255 255 255",
            "255   0   0",
            "255 255 255",
            "255 255 255",
        ], 0, -4);
        var blurredC = FromRows(
        [
            "255 255 255",
            "255  64  64",
            "255  64  64",
            "255 255 255",
        ], 0, -4);
        var blurredE = FromRows(
        [
            "255 255 255",
            "255  64  64",
            "255 160 160",
            "255 255 255",
        ], 0, -4);

        double crisp = LegibilityMetrics.Separation(c, e);
        double blurred = LegibilityMetrics.Separation(blurredC, blurredE);
        crisp.ShouldBeGreaterThan(0.1);
        blurred.ShouldBeLessThan(crisp);
    }

    // ---- the high-resolution ideal ---------------------------------------------------------------

    [Fact]
    public void Downsample_BoxFiltersByAbsolutePixelPosition()
    {
        // An 8x8 solid block at 4x, left edge at 4x-pixel 2: at 1x it covers two columns at
        // half coverage each (2 of 4 subpixels), two rows fully.
        var big = Block(4, 8, 255, xOffset: 2, baselineTop: -8);
        var small = LegibilityMetrics.Downsample(big, 4);

        small.Width.ShouldBe(2);
        small.Height.ShouldBe(2);
        small.XOffset.ShouldBe(0);
        small.BaselineTop.ShouldBe(-2);
        small.Pixels.ShouldAllBe(v => v == 128);
    }

    [Fact]
    public void Downsample_HandlesNegativeCoordinates_WithFloorDivision()
    {
        // Ink from 4x-pixel -3 to 0 sits in 1x column -1, not column 0.
        var big = Block(3, 4, 255, xOffset: -3, baselineTop: -4);
        var small = LegibilityMetrics.Downsample(big, 4);

        small.XOffset.ShouldBe(-1);
        small.Width.ShouldBe(1);
        small.At(0, 0).ShouldBe((byte)191);
    }

    [Fact]
    public void Fidelity_IdenticalIsOne_OnePixelShiftIsForgiven_BlurIsNot()
    {
        var crisp = FromRows(
        [
            "0 255 0 0",
            "0 255 0 0",
            "0 255 0 0",
            "0 255 0 0",
        ], 0, -4);
        var moved = FromRows(
        [
            "0 0 255 0",
            "0 0 255 0",
            "0 0 255 0",
            "0 0 255 0",
        ], 0, -4);
        var blurred = FromRows(
        [
            "0 128 128 0",
            "0 128 128 0",
            "0 128 128 0",
            "0 128 128 0",
        ], 0, -4);

        LegibilityMetrics.Fidelity(crisp, crisp).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.Fidelity(moved, crisp).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.Fidelity(blurred, crisp).ShouldBeLessThan(0.95);
    }

    // ---- alignment -------------------------------------------------------------------------------

    [Fact]
    public void EdgeCrispness_IsTheTopRowsCoverageOverTheStrokeBelowIt()
    {
        var crisp = FromRows(["255 255", "255 255", "255 255"], 0, -3);
        var soft = FromRows(["102 102", "255 255", "255 255"], 0, -3);
        var softBottom = FromRows(["255 255", "255 255", "102 102"], 0, -3);

        LegibilityMetrics.EdgeCrispness(crisp, top: true).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.EdgeCrispness(soft, top: true).ShouldBe(0.4, 1e-9);
        LegibilityMetrics.EdgeCrispness(soft, top: false).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.EdgeCrispness(softBottom, top: false).ShouldBe(0.4, 1e-9);
    }

    [Fact]
    public void Alignment_PenalisesAnXHeightThatLandsOnDifferentRows()
    {
        // n, m, u, x, z flat-topped at the same row; o one row taller than the rest.
        var aligned = Lowercase(oTop: -4);
        var misaligned = Lowercase(oTop: -5);

        LegibilityMetrics.Alignment(aligned).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.Alignment(misaligned).ShouldBeLessThan(1.0);
    }

    // ---- stems and spacing -----------------------------------------------------------------------

    [Fact]
    public void StemUniformity_EqualStemsScoreOne_AStemThatSpreadsScoresLess()
    {
        var even = new Dictionary<int, GlyphBitmap>
        {
            ['i'] = Stem(1, [255]),
            ['l'] = Stem(1, [255]),
            ['I'] = Stem(1, [255]),
            ['H'] = FromRows(["255 0 0 255", "255 255 255 255", "255 0 0 255", "255 0 0 255"], 0, -4, advance: 5),
        };
        var uneven = new Dictionary<int, GlyphBitmap>(even) { ['l'] = Stem(2, [140, 140]) };

        LegibilityMetrics.StemUniformity(even).ShouldBe(1.0, 1e-9);
        LegibilityMetrics.StemUniformity(uneven).ShouldBeLessThan(1.0);
    }

    [Fact]
    public void Typeset_PlacesGlyphsByAdvance()
    {
        var glyphs = new Dictionary<int, GlyphBitmap> { ['n'] = FromRows(["255 0 255", "255 0 255"], 1, -2, advance: 5) };
        var nn = LegibilityMetrics.Typeset(glyphs, "nn");

        nn.ShouldNotBeNull();
        nn!.Width.ShouldBe(8);
        nn.XOffset.ShouldBe(1);
        // Columns: 1,3 lit from the first n; 6,8 from the second (advance 5 + offset 1).
        string row = string.Join(" ", Enumerable.Range(0, nn.Width).Select(x => nn.At(x, 0) > 0 ? "1" : "0"));
        row.ShouldBe("1 0 1 0 0 1 0 1");
    }

    [Fact]
    public void Spacing_EvenStemPitchScoresHigher_ThanJitteredPitch()
    {
        var even = new Dictionary<int, GlyphBitmap>
        {
            ['m'] = FromRows(["255 0 255 0 255", "255 0 255 0 255", "255 0 255 0 255"], 1, -3, advance: 7),
            ['i'] = FromRows(["255", "255", "255"], 1, -3, advance: 3),
            ['n'] = FromRows(["255 0 255", "255 0 255", "255 0 255"], 1, -3, advance: 5),
            ['u'] = FromRows(["255 0 255", "255 0 255", "255 255 255"], 1, -3, advance: 5),
            ['x'] = FromRows(["255 0 255", "0 255 0", "255 0 255"], 1, -3, advance: 5),
        };
        var jittered = new Dictionary<int, GlyphBitmap>(even)
        {
            ['n'] = FromRows(["255 0 0 255", "255 0 0 255", "255 0 0 255"], 1, -3, advance: 5),
            ['i'] = FromRows(["255", "255", "255"], 0, -3, advance: 3),
        };

        LegibilityMetrics.Spacing(even, even).ShouldBeGreaterThan(LegibilityMetrics.Spacing(jittered, even));
    }

    [Fact]
    public void Spacing_PenalisesLettersThatTouch_WhenTheIdealKeepsAGap()
    {
        var spaced = new Dictionary<int, GlyphBitmap>
        {
            ['m'] = FromRows(["255 0 255 0 255", "255 0 255 0 255", "255 0 255 0 255"], 1, -3, advance: 7),
            ['i'] = FromRows(["255", "255", "255"], 1, -3, advance: 3),
            ['n'] = FromRows(["255 0 255", "255 0 255", "255 0 255"], 1, -3, advance: 5),
            ['u'] = FromRows(["255 0 255", "255 0 255", "255 255 255"], 1, -3, advance: 5),
            ['x'] = FromRows(["255 0 255", "0 255 0", "255 0 255"], 1, -3, advance: 5),
        };
        var tight = spaced.ToDictionary(kv => kv.Key, kv => kv.Value with { XAdvance = kv.Value.XAdvance - 1 });

        LegibilityMetrics.Spacing(spaced, spaced).ShouldBeGreaterThan(LegibilityMetrics.Spacing(tight, spaced));
    }

    [Fact]
    public void Spacing_MeasuresGapsBetweenReadColumns_NotBitmapBoxes()
    {
        // A faint fringe column (64) widens the box but is not what a reader sees as the
        // letter's edge; two such glyphs whose boxes abut still have a one-pixel gap.
        var crisp = new Dictionary<int, GlyphBitmap>
        {
            ['m'] = FromRows(["255 0 255 0 255", "255 0 255 0 255", "255 0 255 0 255"], 1, -3, advance: 7),
            ['i'] = FromRows(["255", "255", "255"], 1, -3, advance: 3),
            ['n'] = FromRows(["255 0 255", "255 0 255", "255 0 255"], 1, -3, advance: 5),
            ['u'] = FromRows(["255 0 255", "255 0 255", "255 255 255"], 1, -3, advance: 5),
            ['x'] = FromRows(["255 0 255", "0 255 0", "255 0 255"], 1, -3, advance: 5),
        };
        var fringed = new Dictionary<int, GlyphBitmap>
        {
            ['m'] = FromRows(["64 255 0 255 0 255 64", "64 255 0 255 0 255 64", "64 255 0 255 0 255 64"], 0, -3, advance: 7),
            ['i'] = FromRows(["64 255 64", "64 255 64", "64 255 64"], 0, -3, advance: 3),
            ['n'] = FromRows(["64 255 0 255 64", "64 255 0 255 64", "64 255 0 255 64"], 0, -3, advance: 5),
            ['u'] = FromRows(["64 255 0 255 64", "64 255 0 255 64", "64 255 255 255 64"], 0, -3, advance: 5),
            ['x'] = FromRows(["64 255 0 255 64", "64 0 255 0 64", "64 255 0 255 64"], 0, -3, advance: 5),
        };

        var crispDetail = LegibilityMetrics.SpacingDetail(crisp, crisp, 1);
        var fringedDetail = LegibilityMetrics.SpacingDetail(fringed, crisp, 1);
        fringedDetail.Touches.ShouldBe(crispDetail.Touches);
        fringedDetail.GapTerm.ShouldBe(crispDetail.GapTerm, 1e-9);
    }

    // ---- the whole score -------------------------------------------------------------------------

    [Fact]
    public void Score_RanksCrispAboveBlurred_AgainstTheSameIdeal()
    {
        var crisp = CrispFont();
        var blurred = crisp.ToDictionary(kv => kv.Key, kv => Blur(kv.Value));
        var ideal = IdealFont.FromHighRes(Upscale(crisp, 4), 4);

        var crispTerms = LegibilityScore.Measure(crisp, ideal, mono: false);
        var blurredTerms = LegibilityScore.Measure(blurred, ideal, mono: false);

        crispTerms.Score.ShouldBeGreaterThan(blurredTerms.Score);
        crispTerms.Concentration.ShouldBeGreaterThan(blurredTerms.Concentration);
        crispTerms.Fidelity.ShouldBeGreaterThan(blurredTerms.Fidelity);
        crispTerms.Score.ShouldBeInRange(0.0, 1.0);
        blurredTerms.Score.ShouldBeInRange(0.0, 1.0);
    }

    [Fact]
    public void Score_EveryTermIsReported_UnderTheWeightNames()
    {
        var crisp = CrispFont();
        var ideal = IdealFont.FromHighRes(Upscale(crisp, 4), 4);
        var terms = LegibilityScore.Measure(crisp, ideal, mono: false);

        terms.Named().Select(t => t.Name).ShouldBe(LegibilityScore.Weights.Keys, ignoreOrder: true);
        terms.Features.ShouldBeNull();
        LegibilityScore.Measure(crisp, ideal, mono: false, new StripLayout(8, 6)).Features.ShouldNotBeNull();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static GlyphBitmap Empty() => new(0, 0, 0, 0, 0, 0, 0, []);

    private static GlyphBitmap Block(int width, int height, byte value, int xOffset = 0, int baselineTop = -1, int advance = 0)
    {
        var pixels = new byte[width * height];
        Array.Fill(pixels, value);
        return new GlyphBitmap(0, width, height, xOffset, 0, baselineTop, advance == 0 ? width + 1 : advance, pixels);
    }

    private static GlyphBitmap Stem(int width, byte[] columns) =>
        FromRows(Enumerable.Repeat(string.Join(" ", columns), 4).ToArray(), 0, -4, advance: width + 2);

    /// <summary>Rows of space-separated pixel values, top row first.</summary>
    private static GlyphBitmap FromRows(string[] rows, int xOffset, int baselineTop, int advance = 0, int id = 0)
    {
        var parsed = rows.Select(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(byte.Parse).ToArray()).ToArray();
        int width = parsed[0].Length;
        var pixels = new byte[width * parsed.Length];
        for (int y = 0; y < parsed.Length; y++)
            Array.Copy(parsed[y], 0, pixels, y * width, width);
        return new GlyphBitmap(id, width, parsed.Length, xOffset, 0, baselineTop, advance == 0 ? width + 1 : advance, pixels);
    }

    private static Dictionary<int, GlyphBitmap> Lowercase(int oTop)
    {
        var glyphs = new Dictionary<int, GlyphBitmap>();
        foreach (char c in "mnruxz")
            glyphs[c] = FromRows(["255 255 255", "255 0 255", "255 0 255", "255 0 255"], 0, -4, advance: 4, id: c);
        var oRows = Enumerable.Range(0, -oTop).Select(i => i == 0 || i == -oTop - 1 ? "255 255 255" : "255 0 255").ToArray();
        glyphs['o'] = FromRows(oRows, 0, oTop, advance: 4, id: 'o');
        foreach (char c in "ces")
            glyphs[c] = FromRows(["255 255 255", "255 0 255", "255 0 255", "255 255 255"], 0, -4, advance: 4, id: c);
        foreach (char c in "hil")
            glyphs[c] = FromRows(["255", "255", "255", "255", "255", "255"], 0, -6, advance: 2, id: c);
        foreach (char c in "EFHILTZ")
            glyphs[c] = FromRows(["255 255 255", "255 0 0", "255 255 0", "255 0 0", "255 0 0", "255 255 255"], 0, -6, advance: 4, id: c);
        return glyphs;
    }

    /// <summary>A tiny crisp pixel font with every glyph the score looks at.</summary>
    private static Dictionary<int, GlyphBitmap> CrispFont()
    {
        var f = Lowercase(oTop: -4);
        string[] box = ["255 255 255", "255 0 255", "255 0 255", "255 255 255"];
        foreach (char c in "oabdgpq0O86B9")
            f[c] = FromRows(box, 0, -4, advance: 4, id: c);
        foreach (char c in "1|jtfkvwy")
            f[c] = FromRows(["255", "255", "255", "255", "255", "255"], 1, -6, advance: 3, id: c);
        f['c'] = FromRows(["255 255 255", "255 0 0", "255 0 0", "255 255 255"], 0, -4, advance: 4, id: 'c');
        f['e'] = FromRows(["255 255 255", "255 0 0", "255 255 255", "255 255 255"], 0, -4, advance: 4, id: 'e');
        f['5'] = FromRows(["255 255 255", "255 0 0", "255 255 255", "0 0 255", "255 255 255"], 0, -5, advance: 4, id: '5');
        f['S'] = FromRows(["255 255 255", "255 0 0", "0 255 0", "0 0 255", "255 255 255"], 0, -5, advance: 4, id: 'S');
        f['2'] = FromRows(["255 255 255", "0 0 255", "0 255 0", "255 0 0", "255 255 255"], 0, -5, advance: 4, id: '2');
        f['Z'] = FromRows(["255 255 255", "0 0 255", "0 255 0", "255 0 0", "255 255 255"], 0, -5, advance: 4, id: 'Z');
        f['m'] = FromRows(["255 255 255 255 255", "255 0 255 0 255", "255 0 255 0 255", "255 0 255 0 255"], 0, -4, advance: 6, id: 'm');
        f['w'] = FromRows(["255 0 255 0 255", "255 0 255 0 255", "255 0 255 0 255", "255 255 255 255 255"], 0, -4, advance: 6, id: 'w');
        f['v'] = FromRows(["255 0 255", "255 0 255", "255 0 255", "0 255 0"], 0, -4, advance: 4, id: 'v');
        f[' '] = new GlyphBitmap(' ', 0, 0, 0, 0, 0, 2, []);
        return f;
    }

    private static GlyphBitmap Blur(GlyphBitmap g)
    {
        if (g.IsEmpty)
            return g;
        int width = g.Width + 1;
        var pixels = new byte[width * g.Height];
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                int half = g.At(x, y) / 2;
                pixels[(y * width) + x] = (byte)Math.Min(255, pixels[(y * width) + x] + half);
                pixels[(y * width) + x + 1] = (byte)Math.Min(255, pixels[(y * width) + x + 1] + half);
            }
        return g with { Width = width, Pixels = pixels };
    }

    private static Dictionary<int, GlyphBitmap> Upscale(Dictionary<int, GlyphBitmap> glyphs, int factor)
    {
        var result = new Dictionary<int, GlyphBitmap>();
        foreach (var (id, g) in glyphs)
        {
            var pixels = new byte[g.Width * factor * g.Height * factor];
            for (int y = 0; y < g.Height * factor; y++)
                for (int x = 0; x < g.Width * factor; x++)
                    pixels[(y * g.Width * factor) + x] = g.At(x / factor, y / factor);
            result[id] = g with
            {
                Width = g.Width * factor,
                Height = g.Height * factor,
                XOffset = g.XOffset * factor,
                BaselineTop = g.BaselineTop * factor,
                XAdvance = g.XAdvance * factor,
                Pixels = pixels,
            };
        }
        return result;
    }
}
