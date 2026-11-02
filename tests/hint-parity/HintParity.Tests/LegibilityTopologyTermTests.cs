using KernSmith.HintParity.Legibility;
using Shouldly;

namespace KernSmith.HintParity.Tests;

/// <summary>
/// The two read-cut topology terms added beside the contrast-band term: a broken ring is a
/// true counter a reader can still make out at the faint cut (64) but not at the read cut
/// (128) — a ring with a weak pixel — and the topology error counts every other feature wrong
/// at the read cut (merged or vanished strokes, counters gone at every cut, spurious holes).
/// A lost counter is charged in exactly one of the two, by its cause.
/// </summary>
public class LegibilityTopologyTermTests
{
    private static readonly Topology Ring = new(1, 1);

    [Fact]
    public void BrokenRings_IsACounterSeenAtTheFaintCut_ButNotAtTheReadCut()
    {
        // One side of the ring at 100: whole at 64, open at 128.
        var weakSide = FromRows(
        [
            "255 255 255",
            "100   0 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.BrokenRings(weakSide, Ring).ShouldBe(1);

        // The same side at 40: open at both cuts — not a broken ring, a vanished one.
        var goneSide = FromRows(
        [
            "255 255 255",
            " 40   0 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.BrokenRings(goneSide, Ring).ShouldBe(0);

        // A counter filled with 100: no hole at 64 (the fill is ink there) but a hole at 128 —
        // a muddy counter reads correctly at the read cut and is not a broken ring.
        var muddy = FromRows(
        [
            "255 255 255",
            "255 100 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.BrokenRings(muddy, Ring).ShouldBe(0);

        var solid = FromRows(
        [
            "255 255 255",
            "255   0 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.BrokenRings(solid, Ring).ShouldBe(0);
    }

    [Fact]
    public void BrokenRings_NeverExceedsTheIdealsCounters()
    {
        // A spurious faint hole (between two faint strokes) on a glyph the ideal gives no hole:
        // whatever happens between 64 and 128, there is no true counter to break.
        var faintPair = FromRows(
        [
            "100 100 100",
            "100   0 100",
            "100 100 100",
        ], 0, -3);
        LegibilityMetrics.BrokenRings(faintPair, new Topology(1, 0)).ShouldBe(0);
        LegibilityMetrics.BrokenRings(faintPair, Ring).ShouldBe(1);
    }

    [Fact]
    public void TopologyError_CountsStructuralErrorsAtTheReadCut_AndExcludesBrokenRings()
    {
        // An 'i' whose dot merged into the stem: one component where there should be two.
        var mergedDot = FromRows(["255", "255", "255", "255"], 0, -4);
        LegibilityMetrics.TopologyError(mergedDot, new Topology(2, 0)).ShouldBe(1);

        // A ring with a weak side is a broken ring, not a structural error.
        var weakSide = FromRows(
        [
            "255 255 255",
            "100   0 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.TopologyError(weakSide, Ring).ShouldBe(0);

        // A ring with a side gone at every cut is a structural error.
        var goneSide = FromRows(
        [
            "255 255 255",
            " 40   0 255",
            "255 255 255",
        ], 0, -3);
        LegibilityMetrics.TopologyError(goneSide, Ring).ShouldBe(1);

        // A 'B' with one counter filled solid: one hole where there should be two.
        var filledB = FromRows(
        [
            "255 255 255",
            "255   0 255",
            "255 255 255",
            "255 255 255",
            "255 255 255",
        ], 0, -5);
        LegibilityMetrics.TopologyError(filledB, new Topology(1, 2)).ShouldBe(1);

        // A stem shattered into two pieces: one component too many.
        var shattered = FromRows(["255", "0", "255"], 0, -3);
        LegibilityMetrics.TopologyError(shattered, new Topology(1, 0)).ShouldBe(1);

        LegibilityMetrics.TopologyError(FromRows(["255 255 255", "255 0 255", "255 255 255"], 0, -3), Ring).ShouldBe(0);
    }

    [Fact]
    public void Score_ChargesABrokenRing_InTheBrokenRingTerm_NotTheErrorTerm()
    {
        var crisp = CrispFont();
        var ideal = IdealFont.FromHighRes(Upscale(crisp, 4), 4);

        var weakRings = new Dictionary<int, GlyphBitmap>(crisp);
        foreach (char c in "oO0")
            weakRings[c] = FromRows(["255 255 255", "100 0 255", "100 0 255", "255 255 255"], 0, -4, advance: 4, id: c);

        var crispTerms = LegibilityScore.Measure(crisp, ideal, mono: false);
        var weakTerms = LegibilityScore.Measure(weakRings, ideal, mono: false);

        crispTerms.BrokenRings.ShouldBe(1.0, 1e-9);
        crispTerms.TopologyError.ShouldBe(1.0, 1e-9);
        crispTerms.BrokenRingCount.ShouldBe(0);
        weakTerms.BrokenRings.ShouldBeLessThan(1.0);
        weakTerms.BrokenRingCount.ShouldBe(3);
        weakTerms.TopologyError.ShouldBe(1.0, 1e-9);
        weakTerms.Score.ShouldBeLessThan(crispTerms.Score);

        // The same three rings with the weak side gone at every cut: the error term pays instead.
        var goneRings = new Dictionary<int, GlyphBitmap>(crisp);
        foreach (char c in "oO0")
            goneRings[c] = FromRows(["255 255 255", "40 0 255", "40 0 255", "255 255 255"], 0, -4, advance: 4, id: c);
        var goneTerms = LegibilityScore.Measure(goneRings, ideal, mono: false);
        goneTerms.BrokenRings.ShouldBe(1.0, 1e-9);
        goneTerms.TopologyError.ShouldBeLessThan(1.0);
        goneTerms.TopologyErrorCount.ShouldBe(3);
    }

    [Fact]
    public void Score_WeightsSumToOne_AndNameTheNineTerms()
    {
        LegibilityScore.Weights.Values.Sum().ShouldBe(1.0, 1e-9);
        LegibilityScore.Weights.Keys.ShouldBe(
            ["topology", "topologyError", "brokenRings", "fidelity", "concentration", "separability", "alignment", "stems", "spacing"],
            ignoreOrder: true);
        LegibilityScore.Weights["topology"].ShouldBe(0.10, 1e-9);
        LegibilityScore.Weights["topologyError"].ShouldBe(0.05, 1e-9);
        LegibilityScore.Weights["brokenRings"].ShouldBe(0.05, 1e-9);
    }

    // ---- helpers (the same synthetic font as LegibilityScoreTests) --------------------------------

    private static GlyphBitmap FromRows(string[] rows, int xOffset, int baselineTop, int advance = 0, int id = 0)
    {
        var parsed = rows.Select(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(byte.Parse).ToArray()).ToArray();
        int width = parsed[0].Length;
        var pixels = new byte[width * parsed.Length];
        for (int y = 0; y < parsed.Length; y++)
            Array.Copy(parsed[y], 0, pixels, y * width, width);
        return new GlyphBitmap(id, width, parsed.Length, xOffset, 0, baselineTop, advance == 0 ? width + 1 : advance, pixels);
    }

    internal static Dictionary<int, GlyphBitmap> CrispFont()
    {
        var f = new Dictionary<int, GlyphBitmap>();
        foreach (char c in "mnruxz")
            f[c] = FromRows(["255 255 255", "255 0 255", "255 0 255", "255 0 255"], 0, -4, advance: 4, id: c);
        foreach (char c in "ces")
            f[c] = FromRows(["255 255 255", "255 0 255", "255 0 255", "255 255 255"], 0, -4, advance: 4, id: c);
        foreach (char c in "hil")
            f[c] = FromRows(["255", "255", "255", "255", "255", "255"], 0, -6, advance: 2, id: c);
        foreach (char c in "EFHILTZ")
            f[c] = FromRows(["255 255 255", "255 0 0", "255 255 0", "255 0 0", "255 0 0", "255 255 255"], 0, -6, advance: 4, id: c);
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

    internal static Dictionary<int, GlyphBitmap> Upscale(Dictionary<int, GlyphBitmap> glyphs, int factor)
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
