using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>The outcome of a <c>Normal</c> fit: the fitted pixel-space outline and the metrics rebuilt from it.</summary>
/// <param name="Outline">The fitted outline, in pixels, Y up, pen at x = 0.</param>
/// <param name="Advance">The advance rebuilt from the fitted outline, in whole pixels.</param>
/// <param name="LeftDelta">How far the left side bearing grew, in pixels (negative: shrank).</param>
/// <param name="RightDelta">How far the right side bearing grew, in pixels (negative: shrank).</param>
internal readonly record struct NormalHintResult(GlyphOutline Outline, int Advance, float LeftDelta, float RightDelta);

/// <summary>
/// Native's both-axis auto-hinter for <c>HintLevel.Normal</c>: the y axis is fitted against the
/// font's blue zones and standard horizontal stroke, then the x axis against its standard
/// vertical stem with a per-glyph sub-pixel phase, and the advance and side bearings are
/// rebuilt from the fitted outline. Both passes are <see cref="StemFitter"/>; the x pass runs
/// on the transposed outline.
/// </summary>
internal static class NormalHinter
{
    /// <summary>The furthest, in pixels, a placed edge may move from its unhinted position at full strength.</summary>
    private const float MaxShift = 1f;

    /// <summary>A gap between features at least this wide keeps a whole pixel.</summary>
    private const float MinCounter = 0.5f;

    /// <summary>
    /// Hinting is at full strength up to this size (pixels per em) and tapers off to nothing at
    /// <see cref="TaperEndPpem"/>: text that large no longer needs the grid, and snapping it
    /// would only distort shapes. The strength is the furthest an edge may move, so a weakened
    /// fit still snaps what is already close and leaves the rest alone, never half-snaps.
    /// </summary>
    private const float TaperStartPpem = 24f;

    /// <summary>Above this size nothing is fitted and the advance is the rounded linear one.</summary>
    private const float TaperEndPpem = 40f;

    /// <summary>
    /// How far, in pixels, fitted ink may cross the pen origin before the glyph is moved a
    /// whole pixel right instead of nudged back to it: a sliver past the origin nudged onto it
    /// keeps the fit, while a pixel of translation keeps the fit but costs a pixel of advance.
    /// </summary>
    private const float OriginNudge = 0.25f;

    /// <summary>
    /// Added to the rebuilt mono advance before rounding, in pixels. Mono stems widen to whole
    /// pixels, and a glyph whose ink grew by half a pixel on each side needs the room: measured
    /// against FreeType's mono advances on Roboto at 12 and 16 px, this bias matches the most
    /// glyphs (64 and 80 of 94) with no net drift; without it mono text sets 12-22 px tighter
    /// over ASCII than FreeType's. Grayscale needs none (74 and 75 of 94 without it).
    /// </summary>
    private const float MonoAdvanceBias = 0.25f;

    /// <summary>The fraction of full hinting strength at a size: 1 at text sizes, 0 above <see cref="TaperEndPpem"/>.</summary>
    public static float Strength(float ppem) =>
        Math.Clamp((TaperEndPpem - ppem) / (TaperEndPpem - TaperStartPpem), 0f, 1f);

    /// <summary>
    /// Fits a pixel-space outline in both axes.
    /// </summary>
    /// <param name="outline">The outline in pixels, Y up, baseline at y = 0, pen at x = 0.</param>
    /// <param name="advance">The unhinted advance, in pixels (not rounded).</param>
    /// <param name="metrics">The font's measurements at this size.</param>
    /// <param name="mono">True for a 50%-threshold render: stems take whole pixels.</param>
    /// <returns>The fitted outline and metrics, or null when nothing needed fitting.</returns>
    public static NormalHintResult? Fit(GlyphOutline outline, float advance, NormalHintMetrics metrics, bool mono)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(metrics);

        float strength = Strength(metrics.Ppem);
        if (strength <= 0f)
            return null;

        float maxShift = MaxShift * strength;
        var vertical = metrics.Vertical;
        var yParameters = new StemFitParameters(
            vertical.Zones, vertical.ZoneTolerance, vertical.MaxStem, vertical.StandardStem,
            Mono: mono, AlignPhase: false, maxShift, MinCounter);
        var xParameters = new StemFitParameters(
            [], 0f, vertical.MaxStem, metrics.StandardVerticalStem,
            Mono: mono, AlignPhase: true, maxShift, MinCounter);

        var stretched = metrics.VerticalStretch.Equals(1f) ? outline : OutlineAxes.ScaleY(outline, metrics.VerticalStretch);
        var fittedY = StemFitter.Fit(stretched, yParameters);
        var afterY = fittedY ?? stretched;
        var fittedX = StemFitter.Fit(OutlineAxes.Transpose(afterY), xParameters);
        if (fittedY is null && fittedX is null)
            return null;

        var fitted = fittedX is null ? afterY : OutlineAxes.Transpose(fittedX);
        return Rebuild(outline, fitted, advance, rebuildAdvance: strength >= 1f, mono ? MonoAdvanceBias : 0f);
    }

    /// <summary>
    /// Places the fitted outline at the original left side bearing (to the nearest whole pixel,
    /// so the fitted phase survives) and rebuilds the advance from the fitted right edge and the
    /// original right side bearing.
    /// </summary>
    private static NormalHintResult Rebuild(GlyphOutline original, GlyphOutline fitted, float advance, bool rebuildAdvance, float bias)
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
            shift += crossing >= -OriginNudge ? -crossing : 1f;
        if (shift != 0f)
        {
            fitted = OutlineAxes.TranslateX(fitted, shift);
            after = new OutlineBounds(after.XMin + shift, after.YMin, after.XMax + shift, after.YMax);
        }

        int newAdvance = rebuildAdvance
            ? (int)GridMath.Round(after.XMax + rsb + bias)
            : (int)Math.Round(advance, MidpointRounding.AwayFromZero);
        return new NormalHintResult(fitted, newAdvance, after.XMin - lsb, (newAdvance - after.XMax) - rsb);
    }
}

/// <summary>What the <c>Normal</c> hinter knows about a font at one pixel size.</summary>
/// <param name="Vertical">Zones and the standard horizontal stroke, for the y axis.</param>
/// <param name="StandardVerticalStem">The standard vertical stem width in pixels; 0 when unknown.</param>
/// <param name="Ppem">The size in pixels per em, which sets the hinting strength.</param>
/// <param name="VerticalStretch">The factor the y axis is scaled by before fitting, so the
/// x-height lands on its pixel line; 1 when the font has no x-height.</param>
internal sealed record NormalHintMetrics(ScaledHintMetrics Vertical, float StandardVerticalStem, float Ppem, float VerticalStretch = 1f)
{
    /// <summary>No zones and no standard stems, at a text size (full strength).</summary>
    public static NormalHintMetrics WithoutZones(float maxStem) => new(ScaledHintMetrics.WithoutZones(maxStem), 0f, 12f);
}
