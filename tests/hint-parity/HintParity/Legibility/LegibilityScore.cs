namespace KernSmith.HintParity.Legibility;

/// <summary>One confusable pair and how far apart the rendering keeps it, relative to the ideal at the same size.</summary>
/// <param name="Left">The first string (one glyph, or a bigram set with its advances).</param>
/// <param name="Right">The second string.</param>
/// <param name="Rendered">Separation of the two in the rendering (0 identical .. 1 disjoint).</param>
/// <param name="Ideal">Separation of the two in the ideal 1× image.</param>
/// <param name="Ratio">min(1, Rendered / Ideal): 1 when the rendering keeps the pair as far apart as the ideal does.</param>
internal sealed record PairSeparation(string Left, string Right, double Rendered, double Ideal, double Ratio);

/// <summary>The <c>.fnt</c> line layout of a rendering, for the pairwise-page features.</summary>
/// <param name="LineHeight">The <c>.fnt</c> <c>lineHeight</c>.</param>
/// <param name="Size">The requested pixel size.</param>
internal sealed record StripLayout(int LineHeight, int Size);

/// <summary>The legibility score of one rendered glyph set, with every term reported.</summary>
internal sealed record LegibilityTerms(
    double Topology,
    double TopologyError,
    double BrokenRings,
    double Fidelity,
    double Concentration,
    double Separability,
    double Alignment,
    double Stems,
    double Spacing,
    int TopologyMatches,
    int TopologyGlyphs,
    int TopologyErrorCount,
    int BrokenRingCount,
    IReadOnlyList<string> TopologyBroken,
    IReadOnlyList<PairSeparation> Pairs,
    IReadOnlyList<string> StemWidths,
    double PitchStd,
    double IdealPitchStd,
    int Touches,
    int SpacingPairs,
    LegibilityFeatures? Features)
{
    /// <summary>The weighted sum under the weights in force (<see cref="LegibilityScore.Weights"/>).</summary>
    public double Score => ScoreWith(LegibilityScore.Weights);

    /// <summary>The weighted sum under the given weights (one per term name; a missing term weighs nothing).</summary>
    public double ScoreWith(IReadOnlyDictionary<string, double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        double score = 0;
        foreach (var (name, value) in Named())
            if (weights.TryGetValue(name, out double weight))
                score += weight * value;
        return score;
    }

    public IEnumerable<(string Name, double Value)> Named()
    {
        yield return ("topology", Topology);
        yield return ("topologyError", TopologyError);
        yield return ("brokenRings", BrokenRings);
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
/// of nine terms, each in 0..1, measured against the font's own high-resolution ideal
/// (<see cref="IdealFont"/>). Higher is more legible. Scores compare renderings of the same
/// font, size and anti-alias mode; across modes the terms mean different things (mono ink is
/// always concentrated, and a mono rendering has no broken rings).
/// </summary>
/// <remarks>
/// <para>The terms and their default weights:</para>
/// <list type="bullet">
/// <item><b>topology 0.10</b> — mean share of the contrast band (cuts at 80, 128 and 176) at
/// which each glyph keeps its true component and hole count: whether a feature survives with
/// contrast to spare. Binary per glyph and cut.</item>
/// <item><b>topologyError 0.05</b> — mean of 1/(1+e) where e is the glyph's structural error at
/// the read cut (128): components merged, vanished or shattered, counters gone at every cut or
/// appearing where the ideal has none. Graded where the band term is binary: a 'B' with both
/// counters lost scores below one with one.</item>
/// <item><b>brokenRings 0.05</b> — 1 minus the share of the font's true counters that are whole
/// at the faint cut (64) but open at the read cut: a ring with a weak pixel, the way
/// anti-aliased counters are lost. Its lever is ink weight (stem placement, darkening), where
/// the error term's is shape.</item>
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
/// <para>The three topology terms are one family, weighted together at the 0.20 the band term
/// carried alone. At the read cut a lost feature is charged once between topologyError and
/// brokenRings (by its cause: shape or ink weight) and again by the band term, which also sees
/// the two outer cuts; the band term is kept binary so its margin reading stays what it was.</para>
/// </remarks>
internal static class LegibilityScore
{
    /// <summary>The shipped weights.</summary>
    public static readonly IReadOnlyDictionary<string, double> DefaultWeights = new Dictionary<string, double>
    {
        ["topology"] = 0.10,
        ["topologyError"] = 0.05,
        ["brokenRings"] = 0.05,
        ["fidelity"] = 0.20,
        ["concentration"] = 0.15,
        ["separability"] = 0.15,
        ["alignment"] = 0.10,
        ["stems"] = 0.10,
        ["spacing"] = 0.10,
    };

    private static IReadOnlyDictionary<string, double> _weights = DefaultWeights;

    /// <summary>
    /// The weights <see cref="LegibilityTerms.Score"/> uses: the defaults, or a
    /// <c>--weights</c> file's (<see cref="LegibilityWeights"/>). Set once, before measuring.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Weights
    {
        get => _weights;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var name in value.Keys)
                if (!DefaultWeights.ContainsKey(name))
                    throw new ArgumentException($"Unknown term '{name}'.", nameof(value));
            _weights = value;
        }
    }

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

    /// <summary>Measures a rendering against its ideal.</summary>
    /// <param name="rendered">The glyphs, with <c>.fnt</c> offsets and advances.</param>
    /// <param name="ideal">The font's high-resolution ideal at the same size.</param>
    /// <param name="mono">Whether the rendering is bilevel (the ideal is then thresholded).</param>
    /// <param name="layout">The line layout; when given, the pairwise-page features are measured too.</param>
    public static LegibilityTerms Measure(IReadOnlyDictionary<int, GlyphBitmap> rendered, IdealFont ideal, bool mono, StripLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(ideal);

        var reference = ideal.Reference(mono);

        // Topology: the contrast band (a glyph counts fully only when its features survive at
        // every cut), the structural error and the broken rings at the read cut; the broken list
        // names every glyph not intact at the read cut.
        int topologyGlyphs = 0, topologyMatches = 0, errorCount = 0, brokenRingCount = 0, idealHoles = 0;
        double topologyPreserved = 0, errorScore = 0;
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

            int rings = LegibilityMetrics.BrokenRings(g, truth);
            int error = LegibilityMetrics.TopologyError(g, truth);
            brokenRingCount += rings;
            idealHoles += truth.Holes;
            errorCount += error;
            errorScore += 1.0 / (1 + error);

            var actual = LegibilityMetrics.TopologyOf(g);
            if (actual != truth)
                broken.Add($"{Label(id)}:{actual.Components}c{actual.Holes}h/{truth.Components}c{truth.Holes}h{(rings > 0 ? $" ring{rings}" : "")}");
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
            TopologyError: topologyGlyphs == 0 ? 1 : errorScore / topologyGlyphs,
            BrokenRings: idealHoles == 0 ? 1 : 1 - ((double)brokenRingCount / idealHoles),
            Fidelity: fidelities.Count == 0 ? 1 : fidelities.Average(),
            Concentration: LegibilityMetrics.InkConcentration(rendered.Values),
            Separability: pairs.Count == 0 ? 1 : pairs.Average(p => p.Ratio),
            Alignment: LegibilityMetrics.Alignment(rendered),
            Stems: LegibilityMetrics.StemUniformity(rendered),
            Spacing: (spacing.PitchTerm + (1 - spacing.TouchShare) + spacing.GapTerm) / 3,
            TopologyMatches: topologyMatches,
            TopologyGlyphs: topologyGlyphs,
            TopologyErrorCount: errorCount,
            BrokenRingCount: brokenRingCount,
            TopologyBroken: broken,
            Pairs: pairs,
            StemWidths: stems.Select(s => $"{s.Glyph}={s.ReadWidth}/{s.Coverage:0.00}").ToList(),
            PitchStd: spacing.PitchStd,
            IdealPitchStd: spacing.ReferencePitchStd,
            Touches: spacing.Touches,
            SpacingPairs: spacing.Pairs,
            Features: layout is null ? null : LegibilityFeatures.Measure(rendered, layout.LineHeight, layout.Size));
    }

    private static string Label(int id) => id is >= 0x21 and < 0x7F ? ((char)id).ToString() : $"U+{id:X4}";
}
