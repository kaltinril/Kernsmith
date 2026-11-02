using KernSmith;

namespace KernSmith.HintParity;

/// <summary>
/// The Phase 188 parity bar, per cell: advances 100% identical; ≥ 98% of glyph boxes within
/// ±1 px; grayscale mean SSIM ≥ 0.98 and worst glyph ≥ 0.90; mono ≥ 95% of glyphs bit-identical.
/// </summary>
internal static class ParityBar
{
    public const double BoxWithin1Min = 0.98;
    public const double MeanSsimMin = 0.98;
    public const double WorstSsimMin = 0.90;
    public const double MonoIdenticalMin = 0.95;

    public static IReadOnlyList<string> Failures(CellResult r)
    {
        var failures = new List<string>();
        if (r.AdvanceMatchRatio < 1.0)
            failures.Add($"advances {r.AdvanceMatchRatio:P1} < 100%");
        if (r.BoxWithin1Ratio < BoxWithin1Min)
            failures.Add($"boxes within ±1 {r.BoxWithin1Ratio:P1} < {BoxWithin1Min:P0}");
        if (r.Mono)
        {
            if (r.MonoIdenticalRatio < MonoIdenticalMin)
                failures.Add($"mono identical {r.MonoIdenticalRatio:P1} < {MonoIdenticalMin:P0}");
        }
        else
        {
            if (r.MeanSsim < MeanSsimMin)
                failures.Add($"mean SSIM {r.MeanSsim:0.000} < {MeanSsimMin:0.00}");
            if (r.WorstSsim < WorstSsimMin)
                failures.Add($"worst SSIM {r.WorstSsim:0.000} < {WorstSsimMin:0.00}");
        }

        return failures;
    }
}

/// <summary>Everything measured for one cell, plus the per-glyph detail behind it.</summary>
internal sealed class CellResult
{
    public required Cell Cell { get; init; }
    public required FreeTypeEngine Engine { get; init; }
    public required GeneratedFont FreeType { get; init; }
    public required GeneratedFont Native { get; init; }
    public required IReadOnlyList<GlyphComparison> Glyphs { get; init; }
    public required QualityStats FreeTypeQuality { get; init; }
    public required QualityStats NativeQuality { get; init; }

    /// <summary>The legibility score of each side (<c>--legibility</c>); null when not measured.</summary>
    public Legibility.LegibilityTerms? FreeTypeLegibility { get; init; }
    public Legibility.LegibilityTerms? NativeLegibility { get; init; }

    public bool Mono => Cell.AntiAlias == AntiAliasMode.None;

    public int GlyphCount => Glyphs.Count;
    public bool LineMetricsEqual => FreeType.LineHeight == Native.LineHeight && FreeType.Base == Native.Base;

    public int AdvanceMatches => Glyphs.Count(g => g.AdvanceMatch);
    public double AdvanceMatchRatio => Ratio(AdvanceMatches, GlyphCount);
    public int AdvanceSignedSum => Glyphs.Sum(g => g.AdvanceB - g.AdvanceA);

    public int BoxIdentical => Glyphs.Count(g => g.BoxIdentical);
    public int BoxWithin1 => Glyphs.Count(g => g.BoxWithin1);
    public double BoxIdenticalRatio => Ratio(BoxIdentical, GlyphCount);
    public double BoxWithin1Ratio => Ratio(BoxWithin1, GlyphCount);

    /// <summary>Glyphs with ink on at least one side (both-empty glyphs carry no pixel statistics).</summary>
    public IReadOnlyList<GlyphComparison> Inked => _inked ??= Glyphs.Where(g => !g.BothEmpty).ToArray();
    private IReadOnlyList<GlyphComparison>? _inked;

    public int PixelIdentical => Inked.Count(g => g.PixelIdentical);
    public double PixelIdenticalRatio => Ratio(PixelIdentical, Inked.Count);
    public double MeanAbsDiff => Inked.Count == 0 ? 0 : Inked.Average(g => g.MeanAbsDiff);
    public int MaxAbsDiff => Inked.Count == 0 ? 0 : Inked.Max(g => g.MaxAbsDiff);
    public double MeanSsim => Inked.Count == 0 ? 1 : Inked.Average(g => g.Ssim);
    public double WorstSsim => Inked.Count == 0 ? 1 : Inked.Min(g => g.Ssim);
    public int WorstSsimGlyph => Inked.Count == 0 ? 0 : Inked.MinBy(g => g.Ssim)!.Id;
    public int MaxDiffLe16 => Inked.Count(g => g.MaxAbsDiff <= 16);
    public double MaxDiffLe16Ratio => Ratio(MaxDiffLe16, Inked.Count);

    public int MonoIdentical => Inked.Count(g => g.XorPixels == 0 && g.BoxIdentical);
    public double MonoIdenticalRatio => Ratio(MonoIdentical, Inked.Count);
    public int XorTotal => Inked.Sum(g => g.XorPixels);
    public int WorstXor => Inked.Count == 0 ? 0 : Inked.Max(g => g.XorPixels);

    public IReadOnlyList<string> BarFailures => _failures ??= ParityBar.Failures(this);
    private IReadOnlyList<string>? _failures;
    public bool BarPassed => BarFailures.Count == 0;

    private static double Ratio(int n, int d) => d == 0 ? 1.0 : (double)n / d;
}
