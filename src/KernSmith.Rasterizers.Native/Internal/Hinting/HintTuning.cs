namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// The <c>Normal</c> auto-hinter's tunable constants as one record. The defaults are the shipped
/// behaviour; a tuning tool (the hint-parity sweep) hands the fitters another record to measure
/// what a constant does, and sets <see cref="Current"/> to tune a whole generation run.
/// </summary>
/// <remarks>
/// INVARIANT: <see cref="Current"/> is process-wide, not thread-safe, and exists for tuning
/// tools and tests only; library code never assigns it. The fitters take the record as a
/// parameter so a unit test never needs to touch the global.
/// </remarks>
/// <param name="MinEdgeLength">The shortest edge, in pixels, that counts as an edge at all (<see cref="StemFitter"/>).</param>
/// <param name="MinLoneEdgeLength">A straight edge shorter than this is placed only as part of a stroke.</param>
/// <param name="MergeDistance">Same-side edges this close, in pixels, are fitted as one.</param>
/// <param name="StandardStemTolerance">How far, as a fraction of the standard thickness, a stroke may be from it and still be fitted as the standard.</param>
/// <param name="PhaseShiftPenalty">Penalty per pixel of global x phase shift, in pixels of weighted stroke movement.</param>
/// <param name="MaxShift">The furthest, in pixels, a placed edge may move at full strength.</param>
/// <param name="MinCounter">A gap between features at least this wide, in pixels, keeps a whole pixel.</param>
/// <param name="XHeightRoundUp">How much a fractional x-height is pushed up before it is truncated.</param>
/// <param name="TaperStartPpem">Hinting is at full strength up to this size (pixels per em).</param>
/// <param name="TaperEndPpem">Above this size nothing is fitted: the y axis (zones, baseline, x-height, strokes)
/// fades from <paramref name="TaperStartPpem"/> to nothing here.</param>
/// <param name="XTaperEndPpem">Above this size the x axis is left as drawn: stem snapping, width classes and the
/// by-side placement fade from <paramref name="TaperStartPpem"/> to nothing here, before the y axis does.</param>
/// <param name="OriginNudge">How far fitted ink may cross the pen origin before the glyph moves a whole pixel right instead.</param>
/// <param name="SlopeToleranceX">The largest slope off vertical the x pass takes as a vertical edge (about 4°, the edge finder's own).</param>
/// <param name="SlopeToleranceY">The largest |dy/dx| the y pass takes as a horizontal edge (about 7°), wider than
/// the x pass's: serif faces cut their serif tops and feet at a slight slope (Source Serif's are 0.094), and a top
/// missed there never snaps to its zone and is carried off it by whatever stroke below it moved (the 'u' a row taller
/// than the 'n'). Widening the x pass the same way takes slightly off-vertical cuts for stems and costs Roboto and
/// Noto Sans similarity to FreeType for no legibility gain.</param>
/// <param name="LeftBearingSlack">How far, in pixels, a left side bearing may end up above the font's rather than
/// take the placement a pixel to the left.</param>
internal sealed record HintTuning(
    float MinEdgeLength = 0.05f,
    float MinLoneEdgeLength = 0.7f,
    float MergeDistance = 0.3f,
    float StandardStemTolerance = 0.1f,
    float PhaseShiftPenalty = 0.25f,
    float MaxShift = 1f,
    float MinCounter = 0.5f,
    float XHeightRoundUp = 2f / 3f,
    float TaperStartPpem = 16f,
    float TaperEndPpem = 40f,
    float XTaperEndPpem = 24f,
    float OriginNudge = 0.25f,
    float SlopeToleranceX = 0.07f,
    float SlopeToleranceY = 0.12f,
    float LeftBearingSlack = 0.25f)
{
    /// <summary>The shipped constants.</summary>
    public static HintTuning Default { get; } = new();

    /// <summary>The tuning the pipeline uses when a fit is not handed one explicitly.</summary>
    public static HintTuning Current { get; set; } = Default;
}
