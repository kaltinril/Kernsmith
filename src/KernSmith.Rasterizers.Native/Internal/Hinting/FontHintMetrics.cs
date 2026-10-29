using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// The per-font measurements the auto-hinter needs, in font units: blue zones taken from
/// reference glyphs and the standard thickness of horizontal strokes.
/// </summary>
/// <remarks>
/// The reference glyphs are Latin ('x', 'o', 'H', 'O', 'd', 'p'). A font with neither an
/// x-height nor a cap height — no 'x'/'H' glyphs and no <c>OS/2</c> <c>sxHeight</c>/<c>sCapHeight</c> —
/// is not measured at all, so a non-Latin font renders unhinted rather than snapped to
/// alignment heights that do not describe it.
/// </remarks>
internal sealed class FontHintMetrics
{
    /// <summary>The thickest horizontal stroke measured as a stem, as a fraction of the em.</summary>
    public const float MaxStemEm = 0.16f;

    /// <summary>How far past its flat height an overshoot may sit before it is treated as unrelated.</summary>
    private const float MaxOvershootEm = 0.05f;

    /// <summary>Two measured heights closer than this are the same alignment.</summary>
    private const float SameHeightEm = 0.03f;

    /// <summary>How far outside a zone an edge may sit and still snap to it.</summary>
    private const float ZoneToleranceEm = 0.02f;

    /// <summary>How much a top zone's fractional pixel is pushed up before it is truncated.</summary>
    private const float TopZoneRoundUp = 2f / 3f;

    /// <summary>The em size reference outlines are scaled to for stem measurement.</summary>
    private const float MeasureEmPixels = 100f;

    private readonly BlueZone[] _zones;

    private FontHintMetrics(BlueZone[] zones, float standardHorizontalStem)
    {
        _zones = zones;
        StandardHorizontalStem = standardHorizontalStem;
    }

    /// <summary>The font's blue zones.</summary>
    public IReadOnlyList<BlueZone> Zones => _zones;

    /// <summary>
    /// The median thickness of the horizontal strokes in 'H', 'e' and 'o', in font units; 0 when
    /// none could be measured.
    /// </summary>
    public float StandardHorizontalStem { get; }

    /// <summary>The zone of the given kind, or null when the font has none.</summary>
    public BlueZone? Find(BlueZoneKind kind)
    {
        foreach (var zone in _zones)
        {
            if (zone.Kind == kind)
                return zone;
        }

        return null;
    }

    /// <summary>
    /// Measures a font.
    /// </summary>
    /// <param name="outlineOf">The outline (font units, after any synthetic styling) drawn for a
    /// codepoint, or null when the font does not map it.</param>
    /// <param name="os2">The font's <c>OS/2</c> table, the fallback for x-height and cap height.</param>
    /// <param name="unitsPerEm">The font's <c>head.unitsPerEm</c>.</param>
    /// <returns>The measurements, or null when the font has neither an x-height nor a cap height.</returns>
    public static FontHintMetrics? Measure(Func<int, GlyphOutline?> outlineOf, Os2Table os2, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(outlineOf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerEm);

        float em = unitsPerEm;
        var x = Bounds(outlineOf, "xz");
        var o = Bounds(outlineOf, "o");
        var capital = Bounds(outlineOf, "HEI");
        var round = Bounds(outlineOf, "O");
        var ascender = Bounds(outlineOf, "dlhkb");
        var descender = Bounds(outlineOf, "pq");

        float? xHeight = x?.YMax ?? (os2.XHeight > 0 ? os2.XHeight : null);
        float? capHeight = capital?.YMax ?? (os2.CapHeight > 0 ? os2.CapHeight : null);
        if (xHeight is null && capHeight is null)
            return null;

        var zones = new List<BlueZone>(5);

        float baseline = capital?.YMin ?? x?.YMin ?? 0f;
        if (MathF.Abs(baseline) > SameHeightEm * em)
            baseline = 0f;
        float? roundBottom = Min(o?.YMin, round?.YMin);
        zones.Add(new BlueZone(BlueZoneKind.Baseline, baseline, Overshoot(baseline, roundBottom, top: false, em)));

        if (xHeight is { } xh)
            zones.Add(new BlueZone(BlueZoneKind.XHeight, xh, Overshoot(xh, o?.YMax, top: true, em)));
        if (capHeight is { } cap)
            zones.Add(new BlueZone(BlueZoneKind.CapHeight, cap, Overshoot(cap, round?.YMax, top: true, em)));

        // An ascender level with the cap height (common) is already covered by the cap zone.
        float highest = capHeight ?? xHeight!.Value;
        if (ascender is { } asc && asc.YMax - highest > SameHeightEm * em)
            zones.Add(new BlueZone(BlueZoneKind.Ascender, asc.YMax, asc.YMax));

        if (descender is { } desc && desc.YMin < -SameHeightEm * em)
            zones.Add(new BlueZone(BlueZoneKind.Descender, desc.YMin, desc.YMin));

        return new FontHintMetrics([.. zones], StandardStem(outlineOf, unitsPerEm));
    }

    /// <summary>The font's zones and stem at one pixel size.</summary>
    /// <param name="scale">Pixels per font unit.</param>
    /// <param name="unitsPerEm">The font's <c>head.unitsPerEm</c>.</param>
    public ScaledHintMetrics Scale(float scale, int unitsPerEm)
    {
        var zones = new PixelZone[_zones.Length];
        for (int i = 0; i < _zones.Length; i++)
        {
            var zone = _zones[i];
            float flat = zone.Flat * scale;
            float overshoot = zone.Overshoot * scale;
            // Top zones round up from a third of a pixel rather than a half: a taller x-height
            // and cap height read better at text sizes, which is the conventional preference.
            float snappedFlat = zone.IsTop ? MathF.Floor(flat + TopZoneRoundUp) : GridMath.Round(flat);

            // An overshoot under half a pixel is suppressed: round glyphs line up with flat ones
            // instead of growing a faint extra row.
            float distance = MathF.Abs(overshoot - flat);
            float extra = distance < 0.5f ? 0f : GridMath.Round(distance);
            zones[i] = new PixelZone(zone.IsTop, flat, overshoot, snappedFlat, zone.IsTop ? snappedFlat + extra : snappedFlat - extra);
        }

        return new ScaledHintMetrics(
            zones,
            StandardHorizontalStem * scale,
            ZoneTolerance: unitsPerEm * ZoneToleranceEm * scale,
            MaxStem: unitsPerEm * MaxStemEm * scale);
    }

    private static float Overshoot(float flat, float? measured, bool top, float em)
    {
        if (measured is not { } value)
            return flat;

        float beyond = top ? value - flat : flat - value;
        return beyond > 0f && beyond <= MaxOvershootEm * em ? value : flat;
    }

    private static float? Min(float? a, float? b) =>
        a is null ? b : b is null ? a : MathF.Min(a.Value, b.Value);

    /// <summary>The tight bounds of the first candidate the font draws, or null.</summary>
    private static OutlineBounds? Bounds(Func<int, GlyphOutline?> outlineOf, string candidates)
    {
        foreach (char c in candidates)
        {
            var outline = outlineOf(c);
            if (outline is { IsEmpty: false })
                return OutlineBounds.Tight(outline);
        }

        return null;
    }

    /// <summary>The median horizontal stroke thickness of 'H', 'e' and 'o', in font units.</summary>
    private static float StandardStem(Func<int, GlyphOutline?> outlineOf, int unitsPerEm)
    {
        // The fitter's tolerances are in pixels, so measure at a size where they are fine-grained.
        float toPixels = MeasureEmPixels / unitsPerEm;
        var widths = new List<float>();
        foreach (char c in "Heo")
        {
            var outline = outlineOf(c);
            if (outline is { IsEmpty: false })
                widths.AddRange(GridFitter.HorizontalStemWidths(GridMath.ScaleOutline(outline, toPixels), MaxStemEm * MeasureEmPixels));
        }

        if (widths.Count == 0)
            return 0f;

        widths.Sort();
        return widths[widths.Count / 2] / toPixels;
    }
}

/// <summary>
/// <see cref="FontHintMetrics"/> at one pixel size, Y up, in pixels: what
/// <see cref="GridFitter"/> fits a glyph against.
/// </summary>
/// <param name="Zones">The blue zones.</param>
/// <param name="StandardStem">The standard horizontal stroke thickness; 0 when unknown.</param>
/// <param name="ZoneTolerance">How far outside a zone an edge may sit and still snap to it.</param>
/// <param name="MaxStem">The thickest pair of edges treated as one stroke.</param>
internal sealed record ScaledHintMetrics(IReadOnlyList<PixelZone> Zones, float StandardStem, float ZoneTolerance, float MaxStem)
{
    /// <summary>No zones and no standard stem: stems are still rounded to whole pixels.</summary>
    public static ScaledHintMetrics WithoutZones(float maxStem) => new([], 0f, 0f, maxStem);
}
