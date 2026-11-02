namespace KernSmith.HintParity.Legibility;

/// <summary>One confusable pair and how far apart the rendering keeps it, relative to the ideal at the same size.</summary>
/// <param name="Left">The first string (one glyph, or a bigram set with its advances).</param>
/// <param name="Right">The second string.</param>
/// <param name="Rendered">Separation of the two in the rendering (0 identical .. 1 disjoint).</param>
/// <param name="Ideal">Separation of the two in the ideal 1× image.</param>
/// <param name="Ratio">min(1, Rendered / Ideal): 1 when the rendering keeps the pair as far apart as the ideal does.</param>
internal sealed record PairSeparation(string Left, string Right, double Rendered, double Ideal, double Ratio);

/// <summary>The legibility score of one rendered glyph set, with every term reported.</summary>
internal sealed record LegibilityTerms(
    double Topology,
    double Fidelity,
    double Concentration,
    double Separability,
    double Alignment,
    double Stems,
    double Spacing,
    int TopologyMatches,
    int TopologyGlyphs,
    IReadOnlyList<string> TopologyBroken,
    IReadOnlyList<PairSeparation> Pairs,
    IReadOnlyList<string> StemWidths,
    double PitchStd,
    double IdealPitchStd,
    int Touches,
    int SpacingPairs)
{
    public double Score =>
        (LegibilityScore.Weights["topology"] * Topology)
        + (LegibilityScore.Weights["fidelity"] * Fidelity)
        + (LegibilityScore.Weights["concentration"] * Concentration)
        + (LegibilityScore.Weights["separability"] * Separability)
        + (LegibilityScore.Weights["alignment"] * Alignment)
        + (LegibilityScore.Weights["stems"] * Stems)
        + (LegibilityScore.Weights["spacing"] * Spacing);

    public IEnumerable<(string Name, double Value)> Named()
    {
        yield return ("topology", Topology);
        yield return ("fidelity", Fidelity);
        yield return ("concentration", Concentration);
        yield return ("separability", Separability);
        yield return ("alignment", Alignment);
        yield return ("stems", Stems);
        yield return ("spacing", Spacing);
    }
}

/// <summary>
/// A FreeType-independent legibility score for a rendered glyph set at one size: a weighted sum
/// of seven terms, each in 0..1, measured against the font's own high-resolution ideal
/// (<see cref="IdealFont"/>). Higher is more legible. Scores compare renderings of the same
/// font, size and anti-alias mode; across modes the terms mean different things (mono ink is
/// always concentrated).
/// </summary>
/// <remarks>
/// <para>The terms and their weights:</para>
/// <list type="bullet">
/// <item><b>topology 0.20</b> — mean share of the contrast band (cuts at 80, 128 and 176) at
/// which each glyph keeps its true component and hole count. A closed 'e', a filled 'a', a
/// broken 'B' or a vanished dot change which letter a reader sees, so this is the heaviest
/// term; the band means a feature earns full credit only with contrast to spare.</item>
/// <item><b>fidelity 0.20</b> — mean shift-tolerant SSIM to the box-filtered ideal. Hinting
/// earns the other terms by moving ink; this is the counterweight that charges it for
/// distorting the shape rather than merely moving it.</item>
/// <item><b>concentration 0.15</b> — Σv²/(255Σv): how much of the ink is in solid pixels.
/// Contrast is the strongest single driver of small-text legibility, and blur spends it.</item>
/// <item><b>separability 0.15</b> — mean over confusable pairs (c/e/o, l/I/1, 8/B, 6/b, O/0,
/// 5/S, 2/Z, h/b, n/h, a/o, u/v, rn/m, cl/d, vv/w) of how far apart the rendering keeps them,
/// as a fraction of how far apart the ideal keeps them at this size: a direct test of
/// confusability, normalised so the font's own design limits do not count against the renderer.</item>
/// <item><b>alignment 0.10</b> — crispness of flat tops and bottoms at the x-height, cap
/// height and baseline, and whether letters share the same first/last read row along each
/// line. Even reference lines give word shapes and let the eye track the line.</item>
/// <item><b>stems 0.10</b> — uniformity of stem width in read pixels across stem glyphs: the
/// evenness of typographic colour.</item>
/// <item><b>spacing 0.10</b> — stem-pitch jitter in "minimum" beyond the design's own, letters
/// that touch where the ideal keeps a gap, and gap error against the ideal's fractional spacing.</item>
/// </list>
/// </remarks>
internal static class LegibilityScore
{
    public static readonly IReadOnlyDictionary<string, double> Weights = new Dictionary<string, double>
    {
        ["topology"] = 0.20,
        ["fidelity"] = 0.20,
        ["concentration"] = 0.15,
        ["separability"] = 0.15,
        ["alignment"] = 0.10,
        ["stems"] = 0.10,
        ["spacing"] = 0.10,
    };

    /// <summary>Confusable pairs; a two-character side is a bigram set with the font's advances.</summary>
    public static readonly (string Left, string Right)[] ConfusablePairs =
    [
        ("c", "e"), ("c", "o"), ("e", "o"), ("a", "o"),
        ("l", "I"), ("l", "1"), ("I", "1"), ("i", "l"),
        ("8", "B"), ("6", "b"), ("O", "0"), ("5", "S"), ("2", "Z"),
        ("h", "b"), ("n", "h"), ("u", "v"),
        ("rn", "m"), ("cl", "d"), ("vv", "w"),
    ];

    /// <summary>A separation under this in the ideal means the pair is already indistinguishable by design; the ratio is not taken against it.</summary>
    private const double MinIdealSeparation = 0.02;

    public static LegibilityTerms Measure(IReadOnlyDictionary<int, GlyphBitmap> rendered, IdealFont ideal, bool mono)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(ideal);

        var reference = ideal.Reference(mono);

        // Topology, over the contrast band: a glyph counts fully only when its features survive
        // at every cut; the broken list names those not intact at the mid threshold.
        int topologyGlyphs = 0, topologyMatches = 0;
        double topologyPreserved = 0;
        var broken = new List<string>();
        foreach (var (id, truth) in ideal.Topology)
        {
            if (truth.Components == 0 || !rendered.TryGetValue(id, out var g))
                continue;
            topologyGlyphs++;
            double preserved = LegibilityMetrics.TopologyPreservation(g, truth);
            topologyPreserved += preserved;
            if (preserved >= 1)
                topologyMatches++;
            var actual = LegibilityMetrics.TopologyOf(g);
            if (actual != truth)
                broken.Add($"{Label(id)}:{actual.Components}c{actual.Holes}h/{truth.Components}c{truth.Holes}h");
        }

        // Fidelity.
        var fidelities = new List<double>();
        foreach (var (id, idealGlyph) in reference)
        {
            if (idealGlyph.IsEmpty || !rendered.TryGetValue(id, out var g))
                continue;
            fidelities.Add(LegibilityMetrics.Fidelity(g, idealGlyph));
        }

        // Separability.
        var pairs = new List<PairSeparation>();
        foreach (var (left, right) in ConfusablePairs)
        {
            var rl = LegibilityMetrics.Typeset(rendered, left);
            var rr = LegibilityMetrics.Typeset(rendered, right);
            var il = LegibilityMetrics.Typeset(reference, left);
            var ir = LegibilityMetrics.Typeset(reference, right);
            if (rl is null || rr is null || il is null || ir is null)
                continue;

            double renderedSeparation = LegibilityMetrics.Separation(rl, rr);
            double idealSeparation = LegibilityMetrics.Separation(il, ir);
            double ratio = idealSeparation < MinIdealSeparation ? 1 : Math.Min(1, renderedSeparation / idealSeparation);
            pairs.Add(new PairSeparation(left, right, renderedSeparation, idealSeparation, ratio));
        }

        var stems = LegibilityMetrics.StemReadWidths(rendered);
        var spacing = LegibilityMetrics.SpacingDetail(rendered, ideal.HighRes, ideal.Factor);

        return new LegibilityTerms(
            Topology: topologyGlyphs == 0 ? 1 : topologyPreserved / topologyGlyphs,
            Fidelity: fidelities.Count == 0 ? 1 : fidelities.Average(),
            Concentration: LegibilityMetrics.InkConcentration(rendered.Values),
            Separability: pairs.Count == 0 ? 1 : pairs.Average(p => p.Ratio),
            Alignment: LegibilityMetrics.Alignment(rendered),
            Stems: LegibilityMetrics.StemUniformity(rendered),
            Spacing: (spacing.PitchTerm + (1 - spacing.TouchShare) + spacing.GapTerm) / 3,
            TopologyMatches: topologyMatches,
            TopologyGlyphs: topologyGlyphs,
            TopologyBroken: broken,
            Pairs: pairs,
            StemWidths: stems.Select(s => $"{s.Glyph}={s.ReadWidth}/{s.Coverage:0.00}").ToList(),
            PitchStd: spacing.PitchStd,
            IdealPitchStd: spacing.ReferencePitchStd,
            Touches: spacing.Touches,
            SpacingPairs: spacing.Pairs);
    }

    private static string Label(int id) => id is >= 0x21 and < 0x7F ? ((char)id).ToString() : $"U+{id:X4}";
}
