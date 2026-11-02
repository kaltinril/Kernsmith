using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>The outcome of a <c>Normal</c> fit: the fitted pixel-space outline and the metrics rebuilt from it.</summary>
/// <param name="Outline">The fitted outline, in pixels, Y up, pen at x = 0.</param>
/// <param name="Advance">The advance rebuilt from the fitted outline, in whole pixels.</param>
/// <param name="LeftDelta">How far the left side bearing grew, in pixels (negative: shrank).</param>
/// <param name="RightDelta">How far the right side bearing grew, in pixels (negative: shrank).</param>
/// <param name="FontLeft">The font's own left side bearing at this size, in pixels (unhinted).</param>
/// <param name="FontRight">The font's own right side bearing at this size, in pixels (unhinted).</param>
/// <param name="LeftStem">True when the ink's left edge is a straight vertical stem (<see cref="StemEdges"/>).</param>
/// <param name="RightStem">True when the ink's right edge is a straight vertical stem.</param>
/// <param name="XFitted">True when the x axis was fitted and the ink placed by side; false past the
/// x taper or for a face with no x axis to fit, where the spacing is the font's own.</param>
internal readonly record struct NormalHintResult(
    GlyphOutline Outline, int Advance, float LeftDelta, float RightDelta, float FontLeft, float FontRight, bool LeftStem, bool RightStem, bool XFitted = true);

/// <summary>
/// Native's both-axis auto-hinter for <c>HintLevel.Normal</c>: the y axis is fitted against the
/// font's blue zones and standard horizontal stroke, then the x axis against its standard
/// vertical stem with a per-glyph sub-pixel phase, and the advance and side bearings are
/// rebuilt from the fitted outline. Both passes are <see cref="StemFitter"/>; the x pass runs
/// on the transposed outline.
/// </summary>
internal static class NormalHinter
{
    // The hinter's tunable constants live in HintTuning (MaxShift, MinCounter, TaperStartPpem,
    // TaperEndPpem, OriginNudge, SlopeToleranceX, SlopeToleranceY, LeftBearingSlack), so a
    // tuning tool can vary them per run. Their meaning:
    //
    // MaxShift — the furthest, in pixels, a placed edge may move from its unhinted position at
    // full strength.
    //
    // MinCounter — a gap between features at least this wide keeps a whole pixel.
    //
    // SlopeToleranceY / SlopeToleranceX — the largest slope off the axis the y pass and the x
    // pass take as an edge along it. The y pass is wider (about 7° against 4°): serif faces cut
    // their serif tops and feet at a slight slope, and a top missed there never snaps to its zone
    // and is carried off it by whatever stroke below it moved (the 'u' a row taller than the 'n').
    // The x pass keeps the edge finder's 4°: widening it takes slightly off-vertical cuts for
    // stems and costs Roboto and Noto Sans similarity to FreeType for no legibility gain.
    //
    // TaperStartPpem / XTaperEndPpem / TaperEndPpem — hinting is at full strength up to the
    // start size (pixels per em) and tapers off linearly to nothing: text that large no longer
    // needs the grid, and snapping it would only distort shapes. The shape-changing x axis (stems
    // into columns, width classes, the by-side placement) fades first, to nothing at the x end;
    // the y axis (zones, baseline, x-height and stroke alignment) last, to nothing at the end.
    // Measured on Roboto, Noto Sans and Source Serif at full strength against unhinted with the
    // hint-parity legibility score: the grayscale gain is +0.09 to +0.11 at 9 ppem, still up to
    // +0.05 at 15-18 ppem, and Source Serif loses 0.04-0.06 from 20 ppem on, so the x axis is
    // gone by 24 and the y axis, which costs nothing measurable, by 40. The strength is the
    // furthest an edge may move, so a weakened fit still snaps what is already close and leaves
    // the rest alone, never half-snaps. Both strengths fall monotonically with size, and the x
    // strength never exceeds the y strength.
    //
    // OriginNudge — how far, in pixels, fitted ink may cross the pen origin before the glyph is
    // moved a whole pixel right instead of nudged back to it: a sliver past the origin nudged
    // onto it keeps the fit, while a pixel of translation keeps the fit but costs a pixel of
    // advance.
    //
    // LeftBearingSlack — how far, in pixels, a left side bearing may end up above the font's
    // rather than take the placement a pixel to the left: a stem whose phase lands it 0.1 px
    // right of the font's bearing stays there instead of moving 0.9 px left of it.
    //
    // The advance is not a constant: it is the font's, rounded once, in both anti-alias modes.
    // Following the fitted ink's width instead drifts a line systematically — bilevel stems all
    // widen (Roboto 8 px +21%, Source Serif 8 px +12% over a pangram) and grayscale pitches
    // round down as often as up (Noto Sans 8 px -9%) — where the owner's bound is ±3%; the
    // pairs that then touch are the kerning bake's to open, per pair.
    //
    /// <summary>
    /// The font gap under which two facing stems fuse: a pair whose unhinted white is less than
    /// this reads as one stroke once both stems sit in whole columns, so without the kerning
    /// bake to open that pair the right-hand stem's glyph gives it a column of advance. A glyph
    /// knows only its own side of the pair, and stem-sided bearings come in like pairs (Roboto's
    /// 'n' at 9 ppem: 0.62 and 0.61), so a right bearing under half of this stands for the pair.
    /// </summary>
    private const float FusingGap = 0.5f;

    /// <summary>The fraction of full y-axis hinting strength at a size: 1 at text sizes, 0 above the taper's end.</summary>
    public static float Strength(float ppem) => Strength(ppem, HintTuning.Current);

    /// <summary>The fraction of full y-axis hinting strength at a size under the given tuning.</summary>
    public static float Strength(float ppem, HintTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        return Math.Clamp((tuning.TaperEndPpem - ppem) / (tuning.TaperEndPpem - tuning.TaperStartPpem), 0f, 1f);
    }

    /// <summary>The fraction of full x-axis hinting strength at a size: 1 at text sizes, 0 above the x taper's end.</summary>
    public static float XStrength(float ppem) => XStrength(ppem, HintTuning.Current);

    /// <summary>The fraction of full x-axis hinting strength at a size under the given tuning; never above <see cref="Strength(float, HintTuning)"/>.</summary>
    public static float XStrength(float ppem, HintTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        float x = Math.Clamp((tuning.XTaperEndPpem - ppem) / (tuning.XTaperEndPpem - tuning.TaperStartPpem), 0f, 1f);
        return MathF.Min(x, Strength(ppem, tuning));
    }

    /// <summary>
    /// Fits a pixel-space outline in both axes.
    /// </summary>
    /// <param name="outline">The outline in pixels, Y up, baseline at y = 0, pen at x = 0.</param>
    /// <param name="advance">The unhinted advance, in pixels (not rounded).</param>
    /// <param name="metrics">The font's measurements at this size.</param>
    /// <param name="mono">True for a bilevel (<c>AntiAliasMode.None</c>) render: stems take whole pixels. The
    /// placement and advance do not depend on it.</param>
    /// <param name="tuning">The hinter's constants; null for <see cref="HintTuning.Current"/>.</param>
    /// <param name="rebuild">How the advance and side bearings are rebuilt from the fitted outline.</param>
    /// <param name="padStemPairs">True when no kerning bake will open fused pairs: a glyph whose
    /// right edge is a stem with under half of <see cref="FusingGap"/> of white, and under a
    /// column of white once placed, gets a column of advance so that two such stems never
    /// touch. Off for a fixed-pitch face.</param>
    /// <returns>The fitted outline and metrics; null above the taper, where nothing is hinted,
    /// and for <see cref="AdvanceRebuild.FromRightEdge"/> also when nothing needed fitting.</returns>
    public static NormalHintResult? Fit(GlyphOutline outline, float advance, NormalHintMetrics metrics, bool mono, HintTuning? tuning = null,
        AdvanceRebuild rebuild = AdvanceRebuild.BySide, bool padStemPairs = false)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(metrics);
        tuning ??= HintTuning.Current;

        float strength = Strength(metrics.Ppem, tuning);
        if (strength <= 0f)
            return null;

        float xStrength = XStrength(metrics.Ppem, tuning);
        bool fitX = metrics.FitX && xStrength > 0f;
        var vertical = metrics.Vertical;

        // The hairline weight falls from its full value to nothing across HairlineFullPpem to
        // HairlineEndPpem, so a serif face's 0.4 px horizontals go from 0.6 px rows at 12 ppem to
        // their own coverage at 14 with no step between; a dot is one whose flats are under
        // DotLengthEm of the em, at this size.
        float hairline = tuning.HairlineWeight * Math.Clamp((tuning.HairlineEndPpem - metrics.Ppem) / (tuning.HairlineEndPpem - tuning.HairlineFullPpem), 0f, 1f);
        float dotLength = tuning.DotLengthEm * metrics.Ppem;
        var yParameters = new StemFitParameters(
            vertical.Zones, vertical.ZoneTolerance, vertical.MaxStem, vertical.StandardStem,
            Mono: mono, AlignPhase: false, tuning.MaxShift * strength, tuning.MinCounter, tuning, tuning.SlopeToleranceY, hairline, dotLength);
        var xParameters = new StemFitParameters(
            [], 0f, vertical.MaxStem, metrics.StandardVerticalStem,
            Mono: mono, AlignPhase: true, tuning.MaxShift * xStrength, tuning.MinCounter, tuning, tuning.SlopeToleranceX, hairline, dotLength);

        // Every bowl and arch extremum becomes a node first, so both passes see it as a round edge.
        var split = ExtremaSplitter.Split(outline);
        var stretched = metrics.VerticalStretch.Equals(1f) ? split
            : metrics.XHeight > 0f ? OutlineAxes.StretchBand(split, metrics.XHeight, metrics.VerticalStretch)
            : OutlineAxes.ScaleY(split, metrics.VerticalStretch);
        var fittedY = StemFitter.Fit(stretched, yParameters);
        var afterY = fittedY ?? stretched;
        var fittedX = fitX ? StemFitter.Fit(OutlineAxes.Transpose(afterY), xParameters) : null;

        // A glyph with nothing to snap (';', '.') is still placed and spaced like the others:
        // the by-side rebuild is a spacing policy, not a consequence of a stem having moved.
        // The right-edge rebuild keeps its original meaning, null when nothing was fitted.
        if (fittedY is null && fittedX is null && rebuild == AdvanceRebuild.FromRightEdge)
            return null;

        var fitted = fittedX is null ? afterY : OutlineAxes.Transpose(fittedX);
        var (leftStem, rightStem) = StemEdges.Detect(fitted, tuning.SlopeToleranceX);

        // A face with no x axis to fit (a script face), or a size past the x taper, keeps its x
        // as drawn: no placement, the rounded linear advance, and nothing for the kerning bake.
        if (!fitX)
        {
            var bounds = OutlineBounds.Tight(outline);
            return new NormalHintResult(fitted, RoundAdvance(advance), 0f, 0f, bounds.XMin, advance - bounds.XMax, leftStem, rightStem, XFitted: false);
        }

        return rebuild == AdvanceRebuild.FromRightEdge
            ? RebuildFromRightEdge(outline, fitted, advance, rebuildAdvance: strength >= 1f, tuning, leftStem, rightStem)
            : PlaceBySide(outline, fitted, advance, padStemPairs && rightStem ? xStrength : 0f, tuning, leftStem, rightStem);
    }

    /// <summary>The linear advance rounded to the nearest pixel, an exact half up (Native's advance rounding).</summary>
    private static int RoundAdvance(float advance) => (int)Math.Round(advance, MidpointRounding.AwayFromZero);

    /// <summary>
    /// <see cref="AdvanceRebuild.BySide"/>: the advance is the font's, rounded once, and the
    /// fitted outline is moved by whole pixels — the phase is the fit's — so the left side
    /// bearing is at or just below the font's (within <see cref="HintTuning.LeftBearingSlack"/>
    /// above it), with whatever the fit left over on the right, the same way in both anti-alias
    /// modes. The stems' phase is fixed, so the bearing can only be the font's less some fraction
    /// of a pixel, or that plus one; choosing the nearer of the two per glyph put the slack on
    /// the left for stem-sided glyphs ('n' at 9.1 ppem: 1.13 / 0.16) and on the right for round
    /// ones ('o': 0.18 / 1.17), so "q u" and "o n" set 2.3 px apart where "nn" set 1.3. With
    /// every glyph's slack on the right, the gap between any two is the left one's slack plus
    /// the right one's small shortfall, and the text is even. The glyph is kept off the pen
    /// origin where it was off it. <paramref name="padRight"/> is the x strength when a right-hand
    /// stem may be padded (no kerning bake), 0 otherwise.
    /// </summary>
    private static NormalHintResult PlaceBySide(GlyphOutline original, GlyphOutline fitted, float advance, float padRight, HintTuning tuning, bool leftStem, bool rightStem)
    {
        var before = OutlineBounds.Tight(original);
        var after = OutlineBounds.Tight(fitted);
        float lsb = before.XMin;
        float rsb = advance - before.XMax;

        // The largest whole-pixel shift that keeps the left bearing within the slack above the
        // font's; the pen-origin rule still holds for ink that was right of the pen.
        float shift = MathF.Floor(lsb + tuning.LeftBearingSlack - after.XMin);
        if (lsb >= 0f && after.XMin + shift < -tuning.OriginNudge)
            shift += 1f;

        // Ink that did not reach left of the pen must not start doing so: that costs a blank
        // column in the bitmap and a pixel of advance. A sliver past it is nudged back onto it.
        float crossing = after.XMin + shift;
        if (lsb >= 0f && crossing < 0f)
            shift -= crossing;
        if (shift != 0f)
        {
            fitted = OutlineAxes.TranslateX(fitted, shift);
            after = new OutlineBounds(after.XMin + shift, after.YMin, after.XMax + shift, after.YMax);
        }

        // Without the kerning bake, a right-hand stem whose font white is under the fusing gap
        // keeps a column after it: its pair with a left-hand stem would otherwise draw as one.
        // The gap it takes as fusing shrinks with the x strength, so the padded set fades with
        // the stems' snapping rather than switching off at the taper's end.
        int newAdvance = RoundAdvance(advance);
        if (padRight > 0f && rsb < padRight * FusingGap / 2f && newAdvance - after.XMax < 1f)
            newAdvance++;

        return new NormalHintResult(fitted, newAdvance, after.XMin - lsb, (newAdvance - after.XMax) - rsb, lsb, rsb, leftStem, rightStem);
    }

    /// <summary>
    /// <see cref="AdvanceRebuild.FromRightEdge"/>: places the fitted outline at the original
    /// left side bearing (to the nearest whole pixel, so the fitted phase survives) and rebuilds
    /// the advance from the fitted right edge and the original right side bearing.
    /// </summary>
    private static NormalHintResult RebuildFromRightEdge(GlyphOutline original, GlyphOutline fitted, float advance, bool rebuildAdvance, HintTuning tuning, bool leftStem, bool rightStem)
    {
        var before = OutlineBounds.Tight(original);
        var after = OutlineBounds.Tight(fitted);
        float lsb = before.XMin;
        float rsb = advance - before.XMax;

        float shift = GridMath.Round(lsb - after.XMin);

        // Ink that did not reach left of the pen must not start doing so: that costs a blank
        // column in the bitmap and a pixel of advance. A sliver past it is nudged back onto it.
        float crossing = after.XMin + shift;
        if (lsb >= 0f && crossing < 0f)
            shift += crossing >= -tuning.OriginNudge ? -crossing : 1f;
        if (shift != 0f)
        {
            fitted = OutlineAxes.TranslateX(fitted, shift);
            after = new OutlineBounds(after.XMin + shift, after.YMin, after.XMax + shift, after.YMax);
        }

        int newAdvance = rebuildAdvance
            ? (int)GridMath.Round(after.XMax + rsb)
            : RoundAdvance(advance);
        return new NormalHintResult(fitted, newAdvance, after.XMin - lsb, (newAdvance - after.XMax) - rsb, lsb, rsb, leftStem, rightStem);
    }
}

/// <summary>What the <c>Normal</c> hinter knows about a font at one pixel size.</summary>
/// <param name="Vertical">Zones and the standard horizontal stroke, for the y axis.</param>
/// <param name="StandardVerticalStem">The standard vertical stem width in pixels; 0 when unknown.</param>
/// <param name="Ppem">The size in pixels per em, which sets the hinting strength.</param>
/// <param name="VerticalStretch">The factor the x-height band (baseline to x-height) is scaled
/// by before fitting, so the x-height lands on its pixel line; 1 when the font has no x-height.</param>
/// <param name="XHeight">The unstretched x-height in pixels, the top of the stretched band;
/// 0 when unknown, in which case the whole axis is scaled about the baseline.</param>
/// <param name="FitX">False when the font has no straight vertical stems to fit (a script
/// face): only the y axis is fitted, and x is left as drawn.</param>
internal sealed record NormalHintMetrics(ScaledHintMetrics Vertical, float StandardVerticalStem, float Ppem, float VerticalStretch = 1f, float XHeight = 0f, bool FitX = true)
{
    /// <summary>No zones and no standard stems, at a text size (full strength).</summary>
    public static NormalHintMetrics WithoutZones(float maxStem) => new(ScaledHintMetrics.WithoutZones(maxStem), 0f, 12f);
}

/// <summary>How <see cref="NormalHinter"/> rebuilds a glyph's advance and side bearings after the x fit.</summary>
internal enum AdvanceRebuild
{
    /// <summary>
    /// The font's advance, rounded once, with the fitted ink placed by side: the left bearing
    /// at or just under the font's, the fit's slack on the right. Every glyph keeps the advance
    /// unhinted text has, so tabular digits and a fixed-pitch face keep their columns and a
    /// line keeps its length; the pairs that touch are the kerning bake's to open.
    /// </summary>
    BySide,

    /// <summary>
    /// The fitted ink is placed at the original left side bearing and the advance is the fitted
    /// right edge plus the original right side bearing. Follows the stems' phase, which is what
    /// FreeType's auto-hinter does on Roboto (its +1 px 'h'/'m'/'n' at 12 px), and what set "nn"
    /// 2.3 px apart against 0.35 px for "oo". Kept for comparison; the pipeline never uses it.
    /// </summary>
    FromRightEdge,
}
