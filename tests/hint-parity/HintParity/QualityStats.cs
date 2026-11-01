namespace KernSmith.HintParity;

/// <summary>
/// FreeType-independent quality statistics of one backend's output for a cell, used to tune
/// Native without a reference: how crisp edges are and how consistent vertical stems come out.
/// </summary>
/// <param name="EdgeSharpness">Mean gradient magnitude (central differences, 0..255 units) over lit
/// pixels. Hinted/mono output scores high (hard edges); unhinted small text scores low (blur).</param>
/// <param name="SolidFraction">Share of lit pixels that are fully opaque (≥ 250). A crisp 1 px stem
/// is one solid column; a blurred one is two half-covered columns.</param>
/// <param name="LitPixels">Lit pixel count over the cell's glyphs (the sample size behind the two above).</param>
/// <param name="StemGlyphs">Glyphs with a measurable stem, as "char=coverageWidth/thresholdWidth".</param>
/// <param name="StemMeanCoverage">Mean stem width by summed coverage (fractional pixels).</param>
/// <param name="StemVarianceCoverage">Population variance of the coverage stem width across the stem glyphs.</param>
/// <param name="StemMeanThreshold">Mean stem width counting pixels ≥ 128 (whole pixels).</param>
/// <param name="StemVarianceThreshold">Population variance of the threshold stem width.</param>
internal sealed record QualityStats(
    double EdgeSharpness,
    double SolidFraction,
    long LitPixels,
    IReadOnlyList<string> StemGlyphs,
    double StemMeanCoverage,
    double StemVarianceCoverage,
    double StemMeanThreshold,
    double StemVarianceThreshold)
{
    /// <summary>Glyphs with a clear vertical stem whose leftmost run at 75% height is a plain stem.</summary>
    public static readonly int[] StemCodepoints = ['i', 'l', 'I', '|', 'H', 'n', 'm'];

    public static QualityStats Measure(IReadOnlyDictionary<int, GlyphBitmap> glyphs)
    {
        double gradientSum = 0;
        long lit = 0;
        long solid = 0;

        foreach (var g in glyphs.Values)
        {
            if (g.IsEmpty)
                continue;

            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    int v = g.At(x, y);
                    if (v == 0)
                        continue;

                    lit++;
                    if (v >= 250)
                        solid++;

                    int gx = Sample(g, x + 1, y) - Sample(g, x - 1, y);
                    int gy = Sample(g, x, y + 1) - Sample(g, x, y - 1);
                    gradientSum += Math.Sqrt((gx * gx) + (gy * gy)) / 2.0;
                }
            }
        }

        var labels = new List<string>();
        var coverageWidths = new List<double>();
        var thresholdWidths = new List<double>();

        foreach (int cp in StemCodepoints)
        {
            if (!glyphs.TryGetValue(cp, out var g) || g.IsEmpty || g.Height < 2)
                continue;

            // 75% down the box: below H's crossbar, below n/m's arches, inside i's stem (the dot is above).
            int row = Math.Min(g.Height - 1, (int)(g.Height * 0.75));
            int start = -1;
            for (int x = 0; x < g.Width; x++)
            {
                if (g.At(x, row) >= 64)
                {
                    start = x;
                    break;
                }
            }

            if (start < 0)
                continue;

            double coverage = 0;
            int threshold = 0;
            int end = start;
            while (end < g.Width && g.At(end, row) >= 64)
            {
                coverage += g.At(end, row) / 255.0;
                if (g.At(end, row) >= 128)
                    threshold++;
                end++;
            }

            // Partial coverage on either side of the run belongs to the same stem.
            if (start > 0) coverage += g.At(start - 1, row) / 255.0;
            if (end < g.Width) coverage += g.At(end, row) / 255.0;

            labels.Add($"{(char)cp}={coverage:0.00}/{threshold}");
            coverageWidths.Add(coverage);
            thresholdWidths.Add(threshold);
        }

        return new QualityStats(
            EdgeSharpness: lit == 0 ? 0 : gradientSum / lit,
            SolidFraction: lit == 0 ? 0 : (double)solid / lit,
            LitPixels: lit,
            StemGlyphs: labels,
            StemMeanCoverage: Mean(coverageWidths),
            StemVarianceCoverage: Variance(coverageWidths),
            StemMeanThreshold: Mean(thresholdWidths),
            StemVarianceThreshold: Variance(thresholdWidths));
    }

    private static int Sample(GlyphBitmap g, int x, int y) =>
        x < 0 || y < 0 || x >= g.Width || y >= g.Height ? 0 : g.At(x, y);

    private static double Mean(List<double> values) => values.Count == 0 ? 0 : values.Average();

    private static double Variance(List<double> values)
    {
        if (values.Count == 0)
            return 0;
        double mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / values.Count;
    }
}
