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

    /// <summary>How much a fractional x-height is pushed up before it is truncated: an x-height
    /// of 6.4 px becomes 7, which reads better at text sizes (the light fitter's rule).</summary>
    private const float XHeightRoundUp = 2f / 3f;

    /// <summary>
    /// The measurements at one pixel size. The whole y axis is stretched so the x-height lands
    /// on its rounded-up pixel line (<see cref="NormalHintMetrics.VerticalStretch"/>), and the
    /// other zones round to the nearest pixel at the stretched size: a 9.1 px ascender over a
    /// 6.4 px x-height becomes 10 rows over 7, keeping the proportion, instead of 9 over 7.
    /// An x-height that rounds down only snaps its zone; the glyph is never shrunk, since a
    /// smaller x-height helps nothing. The stretch tapers with the hinting strength.
    /// </summary>
    /// <param name="scale">Pixels per font unit.</param>
    /// <param name="unitsPerEm">The font's <c>head.unitsPerEm</c>.</param>
    public NormalHintMetrics Scale(float scale, int unitsPerEm)
    {
        float ppem = scale * unitsPerEm;
        float stretch = 1f;
        if (_vertical.Find(BlueZoneKind.XHeight) is { } xHeight && xHeight.Flat > 0f)
        {
            float pixels = xHeight.Flat * scale;
            stretch = MathF.Max(1f, MathF.Floor(pixels + XHeightRoundUp) / pixels);
            stretch = 1f + ((stretch - 1f) * NormalHinter.Strength(ppem));
        }

        var zones = new PixelZone[_vertical.Zones.Count];
        for (int i = 0; i < zones.Length; i++)
        {
            var zone = _vertical.Zones[i];
            float flat = zone.Flat * scale * stretch;
            float overshoot = zone.Overshoot * scale * stretch;
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
            stretch);
    }
}
