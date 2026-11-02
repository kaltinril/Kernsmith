namespace KernSmith.HintParity.Legibility;

/// <summary>
/// The strip-level proxies the pairwise page's fit regresses on, computed from a glyph set the
/// way the page's <c>fit_bt.py</c> computes them from a strip PNG: the two sample lines set with
/// each glyph's own advance (no kerning), on a <see cref="Pad"/>-pixel margin, one
/// <paramref name="LineHeight"/>-tall band per line. The names and definitions are the
/// script's, so its <c>weights.json</c> transfers through <see cref="LegibilityWeights"/>; the
/// one difference is that a glyph missing from the set is skipped without advancing the pen,
/// as the script skips it, and is not counted in the expected components either.
/// </summary>
/// <param name="InkConcentration">Lit pixels (≥ 32) that are strong (≥ 192), as a share.</param>
/// <param name="MeanInk">Mean value of the lit pixels, 0..1.</param>
/// <param name="Sharpness">Mean gradient magnitude (central differences, 0..255 units) over lit pixels.</param>
/// <param name="BrokenRings">Per line, holes at 64 minus holes at 128 (floored at 0), summed.</param>
/// <param name="Holes128">Holes at the read cut, summed over the lines.</param>
/// <param name="TopologyError">Per line, |components at 128 − expected components of the text|, summed.</param>
/// <param name="StemWidthVar">Population variance of the ink-run widths on each line's busiest row in its lower 65%.</param>
/// <param name="GapVar">Population variance of the ink gaps between successive glyphs (word spaces excluded).</param>
/// <param name="GapMean">Mean of those gaps.</param>
/// <param name="TouchingPairs">Gaps at or under zero.</param>
internal sealed record LegibilityFeatures(
    double InkConcentration,
    double MeanInk,
    double Sharpness,
    double BrokenRings,
    double Holes128,
    double TopologyError,
    double StemWidthVar,
    double GapVar,
    double GapMean,
    double TouchingPairs)
{
    /// <summary>The script's feature names, in its order.</summary>
    public static readonly string[] Names =
    [
        "ink_concentration", "mean_ink", "sharpness", "broken_rings", "holes_128",
        "topology_error", "stem_width_var", "gap_var", "gap_mean", "touching_pairs",
    ];

    /// <summary>The pairwise page's sample lines (<c>make_pairs.py</c> <c>TEXT</c>).</summary>
    public static readonly string[] DefaultText =
    [
        "The quick brown fox jumps over 13 lazy dogs.",
        "Hamburgefonstiv 0123456789 %@&",
    ];

    /// <summary>The strip's margin on every side (<c>make_pairs.py</c> <c>PAD</c>).</summary>
    public const int Pad = 3;

    public IEnumerable<(string Name, double Value)> Named()
    {
        yield return ("ink_concentration", InkConcentration);
        yield return ("mean_ink", MeanInk);
        yield return ("sharpness", Sharpness);
        yield return ("broken_rings", BrokenRings);
        yield return ("holes_128", Holes128);
        yield return ("topology_error", TopologyError);
        yield return ("stem_width_var", StemWidthVar);
        yield return ("gap_var", GapVar);
        yield return ("gap_mean", GapMean);
        yield return ("touching_pairs", TouchingPairs);
    }

    /// <summary>Glyphs the script counts as more than one piece of ink.</summary>
    private static readonly Dictionary<char, int> ExtraComponents = new()
    {
        ['i'] = 1, ['j'] = 1, ['%'] = 2, [':'] = 1, [';'] = 1, ['='] = 1, ['!'] = 1, ['?'] = 1, ['"'] = 1,
    };

    /// <summary>The script's expected component count of a line: one per non-space character plus the extras.</summary>
    public static int ExpectedComponents(string text)
    {
        int n = 0;
        foreach (char c in text)
        {
            if (c == ' ')
                continue;
            n += 1 + (ExtraComponents.TryGetValue(c, out int extra) ? extra : 0);
        }

        return n;
    }

    /// <summary>
    /// Measures the features of a glyph set at one size.
    /// </summary>
    /// <param name="glyphs">The rendering, with <c>.fnt</c> offsets and advances.</param>
    /// <param name="lineHeight">The <c>.fnt</c> <c>lineHeight</c>: each line's band.</param>
    /// <param name="size">The requested pixel size; gaps over 0.6 × it are word spaces and ignored.</param>
    /// <param name="text">The lines to set; the page's by default.</param>
    public static LegibilityFeatures Measure(IReadOnlyDictionary<int, GlyphBitmap> glyphs, int lineHeight, int size, IReadOnlyList<string>? text = null)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        text ??= DefaultText;

        var strip = Strip.Render(glyphs, lineHeight, text);
        var img = strip.Pixels;
        int w = strip.Width, h = strip.Height;

        // Whole-strip statistics.
        long lit = 0, strong = 0;
        double inkSum = 0, gradientSum = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int v = img[(y * w) + x];
                if (v < 32)
                    continue;
                lit++;
                inkSum += v;
                if (v >= 192)
                    strong++;
                double gx = Gradient(img, w, h, x, y, dx: true), gy = Gradient(img, w, h, x, y, dx: false);
                gradientSum += Math.Sqrt((gx * gx) + (gy * gy));
            }
        }

        // Per line: topology at the two cuts, the busiest row's run widths, the glyph gaps.
        double broken = 0, holes128 = 0, topologyError = 0;
        var widths = new List<double>();
        var gaps = new List<double>();
        double wordSpace = size * 0.6;
        for (int li = 0; li < text.Count; li++)
        {
            var band = strip.Band(li);
            var at64 = LegibilityMetrics.TopologyOf(band, LegibilityMetrics.FaintThreshold);
            var at128 = LegibilityMetrics.TopologyOf(band, LegibilityMetrics.ReadThreshold);
            broken += Math.Max(0, at64.Holes - at128.Holes);
            holes128 += at128.Holes;
            topologyError += Math.Abs(at128.Components - ExpectedComponents(strip.Present(li)));

            int bestRow = -1, bestRuns = -1;
            for (int r = (int)(lineHeight * 0.35); r < lineHeight; r++)
            {
                int runs = 0;
                bool previous = false;
                for (int x = 0; x < band.Width; x++)
                {
                    bool on = band.At(x, r) >= LegibilityMetrics.ReadThreshold;
                    if (on && !previous)
                        runs++;
                    previous = on;
                }

                if (runs > bestRuns)
                {
                    bestRuns = runs;
                    bestRow = r;
                }
            }

            if (bestRow >= 0)
            {
                int current = 0;
                for (int x = 0; x <= band.Width; x++)
                {
                    bool on = x < band.Width && band.At(x, bestRow) >= LegibilityMetrics.ReadThreshold;
                    if (on)
                        current++;
                    else if (current > 0)
                    {
                        widths.Add(current);
                        current = 0;
                    }
                }
            }

            int? previousRight = null;
            foreach (var (x0, x1) in strip.Boxes[li])
            {
                int first = -1, last = -1;
                for (int x = x0; x < x1; x++)
                {
                    bool any = false;
                    for (int y = 0; y < lineHeight && !any; y++)
                        any = band.At(x, y) >= LegibilityMetrics.ReadThreshold;
                    if (!any)
                        continue;
                    if (first < 0) first = x;
                    last = x;
                }

                if (first < 0)
                {
                    previousRight = null;
                    continue;
                }

                if (previousRight is { } right)
                {
                    int gap = first - right;
                    if (gap >= -2 && gap <= wordSpace)
                        gaps.Add(gap);
                }

                previousRight = last + 1;
            }
        }

        return new LegibilityFeatures(
            InkConcentration: lit == 0 ? 0 : (double)strong / lit,
            MeanInk: lit == 0 ? 0 : inkSum / lit / 255.0,
            Sharpness: lit == 0 ? 0 : gradientSum / lit,
            BrokenRings: broken,
            Holes128: holes128,
            TopologyError: topologyError,
            StemWidthVar: Variance(widths),
            GapVar: Variance(gaps),
            GapMean: gaps.Count == 0 ? 0 : gaps.Average(),
            TouchingPairs: gaps.Count(g => g <= 0));
    }

    /// <summary>numpy.gradient: central differences inside, one-sided at the edges.</summary>
    private static double Gradient(byte[] img, int w, int h, int x, int y, bool dx)
    {
        int n = dx ? w : h, i = dx ? x : y;
        if (n < 2)
            return 0;
        int Sample(int k) => img[dx ? (y * w) + k : (k * w) + x];
        if (i == 0)
            return Sample(1) - Sample(0);
        if (i == n - 1)
            return Sample(n - 1) - Sample(n - 2);
        return (Sample(i + 1) - Sample(i - 1)) / 2.0;
    }

    private static double Variance(List<double> values)
    {
        if (values.Count == 0)
            return 0;
        double mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / values.Count;
    }

    /// <summary>The page's strip: white glyphs on black, lines stacked <c>lineHeight</c> apart inside a <see cref="Pad"/> margin.</summary>
    private sealed class Strip
    {
        public required byte[] Pixels { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int LineHeight { get; init; }

        /// <summary>Per line, each set non-space glyph's bitmap box [x0, x1) in strip columns, in text order.</summary>
        public required List<List<(int X0, int X1)>> Boxes { get; init; }

        /// <summary>Per line, the characters that had a glyph (the script counts every character; a set lacking one would be charged a component it never drew).</summary>
        public required List<string> PresentText { get; init; }

        public string Present(int line) => PresentText[line];

        /// <summary>One line's band as a glyph bitmap, for the topology functions.</summary>
        public GlyphBitmap Band(int line)
        {
            var pixels = new byte[Width * LineHeight];
            Array.Copy(Pixels, (Pad + (line * LineHeight)) * Width, pixels, 0, pixels.Length);
            return new GlyphBitmap(0, Width, LineHeight, 0, 0, 0, 0, pixels);
        }

        public static Strip Render(IReadOnlyDictionary<int, GlyphBitmap> glyphs, int lineHeight, IReadOnlyList<string> text)
        {
            int width = 0;
            foreach (var line in text)
            {
                int x = 0;
                foreach (char c in line)
                    if (glyphs.TryGetValue(c, out var g))
                        x += g.XAdvance;
                width = Math.Max(width, x);
            }

            int w = width + (2 * Pad), h = (lineHeight * text.Count) + (2 * Pad);
            var pixels = new byte[w * h];
            var boxes = new List<List<(int, int)>>();
            var present = new List<string>();
            for (int li = 0; li < text.Count; li++)
            {
                int x = Pad, y = Pad + (li * lineHeight);
                var lineBoxes = new List<(int, int)>();
                var chars = new System.Text.StringBuilder();
                foreach (char c in text[li])
                {
                    if (!glyphs.TryGetValue(c, out var g))
                        continue;
                    chars.Append(c);
                    if (!g.IsEmpty)
                    {
                        int px = x + g.XOffset, py = y + g.YOffset;
                        for (int gy = 0; gy < g.Height; gy++)
                        {
                            int ty = py + gy;
                            if (ty < 0 || ty >= h)
                                continue;
                            for (int gx = 0; gx < g.Width; gx++)
                            {
                                int tx = px + gx;
                                if (tx < 0 || tx >= w)
                                    continue;
                                // The script composites white over what is there by the glyph's alpha.
                                int a = g.At(gx, gy), under = pixels[(ty * w) + tx];
                                pixels[(ty * w) + tx] = (byte)Math.Clamp((int)Math.Round(a + under - (under * a / 255.0)), 0, 255);
                            }
                        }

                        if (c != ' ')
                            lineBoxes.Add((px, px + g.Width));
                    }

                    x += g.XAdvance;
                }

                boxes.Add(lineBoxes);
                present.Add(chars.ToString());
            }

            return new Strip { Pixels = pixels, Width = w, Height = h, LineHeight = lineHeight, Boxes = boxes, PresentText = present };
        }
    }
}
