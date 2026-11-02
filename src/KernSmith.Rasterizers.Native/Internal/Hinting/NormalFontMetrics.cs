using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// The per-font measurements the <c>Normal</c> hinter needs, in font units: the light hinter's
/// zones and standard horizontal stroke (<see cref="FontHintMetrics"/>) plus the standard
/// vertical stem width.
/// </summary>
internal sealed class NormalFontMetrics
{
    /// <summary>The em size reference outlines are scaled to for stem measurement.</summary>
    private const float MeasureEmPixels = 100f;

    /// <summary>
    /// The glyphs whose straight vertical stems set the standard width: the lowercase and
    /// capital letters with plain stems. Their stems dominate running text, and a glyph with
    /// no straight stems simply contributes nothing.
    /// </summary>
    private const string StemReferences = "ilnmhudbpqrHILNMEFTBDPR";

    private readonly FontHintMetrics _vertical;

    private NormalFontMetrics(FontHintMetrics vertical, float standardVerticalStem)
    {
        _vertical = vertical;
        StandardVerticalStem = standardVerticalStem;
    }

    /// <summary>The standard vertical stem width in font units; 0 when none could be measured.</summary>
    public float StandardVerticalStem { get; }

    /// <summary>Measures a font; null when it has neither an x-height nor a cap height.</summary>
    /// <param name="outlineOf">The outline (font units, after any synthetic styling) drawn for a codepoint, or null when unmapped.</param>
    /// <param name="os2">The font's <c>OS/2</c> table.</param>
    /// <param name="unitsPerEm">The font's <c>head.unitsPerEm</c>.</param>
    public static NormalFontMetrics? Measure(Func<int, GlyphOutline?> outlineOf, Os2Table os2, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(outlineOf);

        var vertical = FontHintMetrics.Measure(outlineOf, os2, unitsPerEm);
        return vertical is null ? null : new NormalFontMetrics(vertical, StandardVerticalStemOf(outlineOf, unitsPerEm));
    }

    /// <summary>
    /// The length-weighted median width of the straight vertical stems in the reference
    /// glyphs, in font units — the width half the stem length lies on either side of — with
    /// widths outside half to double the plain median dropped first, so a crossbar end or a
    /// serif measured as a stem does not pull it. 0 when nothing could be measured.
    /// </summary>
    private static float StandardVerticalStemOf(Func<int, GlyphOutline?> outlineOf, int unitsPerEm)
    {
        float toPixels = MeasureEmPixels / unitsPerEm;
        float maxStem = FontHintMetrics.MaxStemEm * MeasureEmPixels;
        var stems = new List<(float Width, float Length)>();
        foreach (char c in StemReferences)
        {
            var outline = outlineOf(c);
            if (outline is not { IsEmpty: false })
                continue;

            var transposed = OutlineAxes.Transpose(GridMath.ScaleOutline(outline, toPixels));
            stems.AddRange(StemFitter.StraightStemWidths(transposed, maxStem));
        }

        if (stems.Count == 0)
            return 0f;

        stems.Sort((a, b) => a.Width.CompareTo(b.Width));
        float plain = stems[stems.Count / 2].Width;
        stems.RemoveAll(s => s.Width < 0.5f * plain || s.Width > 2f * plain);

        float total = 0f;
        foreach (var stem in stems)
            total += stem.Length;

        float seen = 0f;
        foreach (var stem in stems)
        {
            seen += stem.Length;
            if (seen >= total / 2f)
                return stem.Width / toPixels;
        }

        return plain / toPixels;
    }

    // XHeightRoundUp (in HintTuning): how much a fractional x-height is pushed up before it is
    // truncated: an x-height of 6.4 px becomes 7, which reads better at text sizes (the light
    // fitter's rule). A face hinted for integer ppem rounds to the nearest row instead
    // (HintingProfile.XHeightRoundUp), which Scale takes as an override.

    /// <summary>
    /// The measurements at one pixel size. The band from the baseline to the x-height is
    /// stretched so the x-height lands on its rounded-up pixel line
    /// (<see cref="NormalHintMetrics.VerticalStretch"/>, <see cref="OutlineAxes.StretchBand"/>);
    /// the zones above it shift up by the band's growth and round from there, so a cap or
    /// ascender keeps its own size: Source Serif at 7.3 ppem has a 3.46 px x-height stretched
    /// to 4 and a 4.88 px cap height, which rounds to 5 rows from its shifted 5.42 where scaling
    /// the whole axis would have made it 6. Descenders are not stretched. An x-height that
    /// rounds down only snaps its zone; the glyph is never shrunk, since a smaller x-height helps
    /// nothing. The stretch tapers with the hinting strength.
    /// </summary>
    /// <param name="scale">Pixels per font unit.</param>
    /// <param name="unitsPerEm">The font's <c>head.unitsPerEm</c>.</param>
    /// <param name="tuning">The hinter's constants; null for <see cref="HintTuning.Current"/>.</param>
    /// <param name="xHeightRoundUp">How far the fractional x-height is pushed up before it is
    /// truncated to a row (<see cref="HintingProfile.XHeightRoundUp"/>); null for the tuning's
    /// <see cref="HintTuning.XHeightRoundUp"/>.</param>
    public NormalHintMetrics Scale(float scale, int unitsPerEm, HintTuning? tuning = null, float? xHeightRoundUp = null)
    {
        tuning ??= HintTuning.Current;
        float ppem = scale * unitsPerEm;
        float stretch = 1f;
        float xHeightPixels = 0f;
        if (_vertical.Find(BlueZoneKind.XHeight) is { } xHeight && xHeight.Flat > 0f)
        {
            xHeightPixels = xHeight.Flat * scale;
            stretch = MathF.Max(1f, MathF.Floor(xHeightPixels + (xHeightRoundUp ?? tuning.XHeightRoundUp)) / xHeightPixels);
            stretch = 1f + ((stretch - 1f) * NormalHinter.Strength(ppem, tuning));
        }

        var zones = new PixelZone[_vertical.Zones.Count];
        for (int i = 0; i < zones.Length; i++)
        {
            var zone = _vertical.Zones[i];
            float flat = OutlineAxes.Band(zone.Flat * scale, xHeightPixels, stretch);
            float overshoot = OutlineAxes.Band(zone.Overshoot * scale, xHeightPixels, stretch);
            float snappedFlat = GridMath.Round(flat);
            float distance = MathF.Abs(overshoot - flat);
            float extra = distance < 0.5f ? 0f : GridMath.Round(distance);
            zones[i] = new PixelZone(zone.IsTop, flat, overshoot, snappedFlat, zone.IsTop ? snappedFlat + extra : snappedFlat - extra);
        }

        var light = _vertical.Scale(scale, unitsPerEm);
        return new NormalHintMetrics(
            light with { Zones = zones, StandardStem = light.StandardStem * stretch },
            StandardVerticalStem * scale,
            ppem,
            stretch,
            xHeightPixels,
            // No straight stem in any reference glyph: a script face, whose x axis has nothing
            // to fit and whose joins an x fit only distorts.
            FitX: StandardVerticalStem > 0f);
    }
}
