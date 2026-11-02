using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The <c>Normal</c> auto-hinter on the shapes that stems do not cover: round bowls whose
/// extrema fall mid-curve (found from the curve, not from a node), thin diagonals in mono, and
/// the x-height stretch that must not scale the caps and ascenders with it.
/// </summary>
public class NormalHinterShapeTests
{
    private const float Tolerance = 1f / 32f;

    // ---- Round edges from curvature ---------------------------------------------------------

    [Fact]
    public void Fit_Mono_WidensARoundBowl_WhoseExtremaHaveNoNodes_ToWholePixelStrokes()
    {
        // A ring whose four nodes sit at 45 degrees, so every extremum is in the middle of a
        // cubic and no node has a horizontal or vertical tangent — the bowl of a quadratic
        // font converted to cubics, or a CFF 'e' whose eye is drawn through the diagonals.
        // Its 0.5 px walls must still come out as whole-pixel strokes in mono on both axes,
        // or the bowl thresholds to a dot.
        var ring = Ring(2.1f, 2.3f, outer: 1.4f, inner: 0.9f);

        var result = NormalHinter.Fit(ring, 4.5f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        result.ShouldNotBeNull();
        var (outer, inner) = ContourBounds(result!.Value.Outline);
        (outer.YMax - inner.YMax).ShouldBe(1f, Tolerance, "top wall");
        (inner.YMin - outer.YMin).ShouldBe(1f, Tolerance, "bottom wall");
        (inner.XMin - outer.XMin).ShouldBe(1f, Tolerance, "left wall");
        (outer.XMax - inner.XMax).ShouldBe(1f, Tolerance, "right wall");
        foreach (float edge in new[] { outer.YMax, outer.YMin, outer.XMin, outer.XMax })
            edge.ShouldBe(MathF.Round(edge), Tolerance, "a mono wall edge sits on a pixel boundary");
    }

    [Fact]
    public void Fit_Grayscale_PlacesARoundBowlsWalls_InsideOneColumnAndRow()
    {
        var ring = Ring(2.1f, 2.3f, outer: 1.4f, inner: 0.9f);

        var result = NormalHinter.Fit(ring, 4.5f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        var (outer, inner) = ContourBounds(result!.Value.Outline);
        MathF.Floor(outer.XMin).ShouldBe(MathF.Floor(inner.XMin - 1e-3f), $"left wall {outer.XMin:F2}-{inner.XMin:F2} straddles a column");
        MathF.Floor(inner.XMax).ShouldBe(MathF.Floor(outer.XMax - 1e-3f), $"right wall {inner.XMax:F2}-{outer.XMax:F2} straddles a column");
        MathF.Floor(inner.YMax).ShouldBe(MathF.Floor(outer.YMax - 1e-3f), $"top wall {inner.YMax:F2}-{outer.YMax:F2} straddles a row");
        MathF.Floor(outer.YMin).ShouldBe(MathF.Floor(inner.YMin - 1e-3f), $"bottom wall {outer.YMin:F2}-{inner.YMin:F2} straddles a row");
        (inner.XMin - outer.XMin).ShouldBe(0.5f, Tolerance, "grayscale keeps the wall's thickness");
    }

    [Fact]
    public void Fit_KeepsABowlsCounter_AtLeastOnePixel_InMono()
    {
        // A tiny bowl: 0.5 px walls around a 1.2 px counter, 2.2 px across. Widened to a
        // pixel each, the walls would leave 0.2 px of counter; the counter keeps a pixel.
        var ring = Ring(2.1f, 2.3f, outer: 1.1f, inner: 0.6f);

        var result = NormalHinter.Fit(ring, 3f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        result.ShouldNotBeNull();
        var (_, inner) = ContourBounds(result!.Value.Outline);
        (inner.XMax - inner.XMin).ShouldBeGreaterThanOrEqualTo(1f - Tolerance, "counter width");
        (inner.YMax - inner.YMin).ShouldBeGreaterThanOrEqualTo(1f - Tolerance, "counter height");
    }

    // ---- Diagonals are left alone -------------------------------------------------------------

    [Fact]
    public void Fit_Mono_LeavesAThinDiagonalsThickness()
    {
        // A 0.5 px stroke at 53 degrees (the arm of a 'v'). Thickening it to a pixel was
        // measured and rejected: across the two rows left between a 'z''s bars, or at a 'v''s
        // point, more ink reads worse. Diagonals move only through the monotone maps.
        var slash = Diagonal(0.3f, 0f, 3.3f, 4f, thickness: 0.5f);

        var result = NormalHinter.Fit(slash, 4f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        float thickness = result is { } fitted ? DiagonalThickness(fitted.Outline) : 0.5f;
        thickness.ShouldBe(0.5f, 0.1f);
    }

    // ---- Sloped serifs and zones --------------------------------------------------------------

    [Fact]
    public void Fit_SnapsASlightlySlopedSerifTop_ToItsZone()
    {
        // Source Serif's serif tops slope about 5 degrees (0.094), past the light fitter's 4
        // degree edge tolerance. A stem whose top is such a cut must still snap to the x-height
        // zone it sits in; otherwise a stroke placed below it carries it off the zone — the 'u'
        // whose bowl bottom widened to a pixel gained a fifth row.
        var stem = GlyphOutline.FromCommands(TestOutlines.Polygon((1f, 0f), (1f, 4.21f), (1.8f, 4.14f), (1.8f, 0f)));
        var bowlBottom = GlyphOutline.FromCommands(TestOutlines.Rect(2.5f, 0.4f, 4f, 0.85f));
        var glyph = GlyphOutline.FromCommands([.. stem.Commands, .. bowlBottom.Commands]);
        var zones = new PixelZone[]
        {
            new(IsTop: false, Flat: 0f, Overshoot: -0.13f, SnappedFlat: 0f, SnappedOvershoot: 0f),
            new(IsTop: true, Flat: 4.16f, Overshoot: 4.27f, SnappedFlat: 4f, SnappedOvershoot: 4f),
        };
        var metrics = new NormalHintMetrics(new ScaledHintMetrics(zones, 0.4f, ZoneTolerance: 0.5f, MaxStem: 1.4f), 0.75f, Ppem: 8.75f);

        var result = NormalHinter.Fit(glyph, 4.5f, metrics, mono: true);

        result.ShouldNotBeNull();
        OutlineBounds.Tight(result!.Value.Outline).YMax.ShouldBe(4f, Tolerance);
    }

    // ---- Hintability ----------------------------------------------------------------------------

    [Fact]
    public void Scale_TurnsOffTheXAxis_WhenNoReferenceGlyphHasAStraightStem()
    {
        // A script face: every reference glyph is round, so no standard vertical stem can be
        // measured. Fitting x against nothing distorts its joins; only y is fitted.
        var metrics = NormalFontMetrics.Measure(RoundReferences, SerifOs2, 1000)!;

        var scaled = metrics.Scale(8.5f / 1000f, 1000);

        scaled.StandardVerticalStem.ShouldBe(0f);
        scaled.FitX.ShouldBeFalse();
        NormalFontMetrics.Measure(SerifReferences, SerifOs2, 1000)!.Scale(8.5f / 1000f, 1000).FitX.ShouldBeTrue();
    }

    [Fact]
    public void Fit_LeavesXAlone_WhenTheFontIsNotFittableInX()
    {
        var stems = GlyphOutline.FromCommands([.. TestOutlines.Rect(0.63f, 0f, 1.45f, 7f), .. TestOutlines.Rect(3.58f, 0f, 4.40f, 5.3f)]);
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(1.45f), 0f, Ppem: 9f, FitX: false);

        var result = NormalHinter.Fit(stems, 5f, metrics, mono: false);

        result.ShouldNotBeNull();
        var before = stems.Commands.Select(c => c.EndX).ToArray();
        var after = result!.Value.Outline.Commands.Select(c => c.EndX).ToArray();
        after.ShouldBe(before);
        result.Value.Advance.ShouldBe(5);
    }

    // ---- The x-height stretch -----------------------------------------------------------------

    [Fact]
    public void Scale_StretchesTheXHeightBandOnly_SoTheCapHeightRoundsFromItsOwnSize()
    {
        // Source Serif at a 10 px cell (7.29 ppem): x-height 475 units = 3.46 px, stretched to
        // 4; cap height 670 units = 4.88 px. Scaling the whole axis by the same 15% would put the
        // cap at 5.64 and round it to 6 rows; shifting everything above the x-height by the
        // 0.54 px the x-height grew keeps it at 5.42, 5 rows, as a 4.88 px cap should be.
        var metrics = NormalFontMetrics.Measure(SerifReferences, SerifOs2, 1000)!;

        var scaled = metrics.Scale(7.29f / 1000f, 1000);

        scaled.VerticalStretch.ShouldBe(4f / 3.4628f, 1e-3f);
        // Top zones in height order: x-height, cap, ascender.
        var cap = scaled.Vertical.Zones.Where(z => z.IsTop).OrderBy(z => z.Flat).ElementAt(1);
        cap.SnappedFlat.ShouldBe(5f);
        cap.Flat.ShouldBe(5.42f, 0.02f);
        scaled.XHeight.ShouldBe(3.4628f, 1e-3f);
    }

    [Fact]
    public void Fit_ShiftsInkAboveTheXHeight_RatherThanScalingIt()
    {
        // A cap stem 0 to 4.88 px under a 15.5% x-height stretch (x-height 3.46 px): its top
        // lands on row 5, not row 6.
        var stem = GlyphOutline.FromCommands(TestOutlines.Rect(1f, 0f, 1.8f, 4.88f));
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(1.45f), 0.8f, Ppem: 7.29f, VerticalStretch: 1.155f, XHeight: 3.46f);

        var result = NormalHinter.Fit(stem, 3f, metrics, mono: false);

        result.ShouldNotBeNull();
        var bounds = OutlineBounds.Tight(result!.Value.Outline);
        bounds.YMax.ShouldBe(5f, Tolerance);
        bounds.YMin.ShouldBe(0f, Tolerance);
    }

    [Fact]
    public void Fit_DoesNotStretchDescenders()
    {
        var stem = GlyphOutline.FromCommands(TestOutlines.Rect(1f, -2.2f, 1.8f, 3.46f));
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(1.45f), 0.8f, Ppem: 7.29f, VerticalStretch: 1.155f, XHeight: 3.46f);

        var result = NormalHinter.Fit(stem, 3f, metrics, mono: false);

        result.ShouldNotBeNull();
        var bounds = OutlineBounds.Tight(result!.Value.Outline);
        bounds.YMax.ShouldBe(4f, Tolerance, "the x-height top is stretched onto row 4");
        bounds.YMin.ShouldBe(-2f, Tolerance, "the descender rounds from its own 2.2 px, not 2.54");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static readonly Os2Table SerifOs2 = new(Version: 2, TypoAscender: 1036, TypoDescender: -335, WinAscent: 1036, WinDescent: 335, XHeight: 475, CapHeight: 670);

    private static GlyphOutline? RoundReferences(int codepoint) => (char)codepoint switch
    {
        'x' or 'o' => Ring(250f, 237f, outer: 237f, inner: 180f),
        'H' or 'O' => Ring(300f, 335f, outer: 335f, inner: 270f),
        _ => null,
    };

    private static GlyphOutline? SerifReferences(int codepoint) => (char)codepoint switch
    {
        // Heights set the zones; the 80-unit widths are straight stems the standard is measured from.
        'x' => GlyphOutline.FromCommands(TestOutlines.Rect(50f, 0f, 450f, 475f)),
        'H' => GlyphOutline.FromCommands(TestOutlines.Rect(50f, 0f, 130f, 670f)),
        'd' => GlyphOutline.FromCommands(TestOutlines.Rect(50f, 0f, 130f, 730f)),
        'p' => GlyphOutline.FromCommands(TestOutlines.Rect(50f, -270f, 130f, 475f)),
        _ => null,
    };

    /// <summary>
    /// A ring of two circles whose nodes sit at 45, 135, 225 and 315 degrees, so the extrema
    /// are mid-curve. Outer contour clockwise (TrueType), inner counter-clockwise.
    /// </summary>
    private static GlyphOutline Ring(float cx, float cy, float outer, float inner) =>
        GlyphOutline.FromCommands([.. Circle45(cx, cy, outer, clockwise: true), .. Circle45(cx, cy, inner, clockwise: false)]);

    private static OutlineCommand[] Circle45(float cx, float cy, float r, bool clockwise)
    {
        const float k = 0.5522847498f;
        float step = clockwise ? -MathF.PI / 2f : MathF.PI / 2f;
        float a = MathF.PI / 4f;
        var commands = new List<OutlineCommand> { OutlineCommand.Move(cx + (r * MathF.Cos(a)), cy + (r * MathF.Sin(a))) };
        for (int i = 0; i < 4; i++)
        {
            float b = a + step;
            // Tangent direction along increasing angle is (-sin, cos); reversed when clockwise.
            float sign = clockwise ? -1f : 1f;
            float c1x = cx + (r * MathF.Cos(a)) + (sign * k * r * -MathF.Sin(a));
            float c1y = cy + (r * MathF.Sin(a)) + (sign * k * r * MathF.Cos(a));
            float c2x = cx + (r * MathF.Cos(b)) - (sign * k * r * -MathF.Sin(b));
            float c2y = cy + (r * MathF.Sin(b)) - (sign * k * r * MathF.Cos(b));
            float ex = i == 3 ? commands[0].EndX : cx + (r * MathF.Cos(b));
            float ey = i == 3 ? commands[0].EndY : cy + (r * MathF.Sin(b));
            commands.Add(OutlineCommand.Cubic(c1x, c1y, c2x, c2y, ex, ey));
            a = b;
        }

        commands.Add(OutlineCommand.CloseAt(commands[0].EndX, commands[0].EndY));
        return [.. commands];
    }

    /// <summary>The tight bounds of the first and second contours.</summary>
    private static (OutlineBounds Outer, OutlineBounds Inner) ContourBounds(GlyphOutline outline)
    {
        var contours = new List<List<OutlineCommand>>();
        foreach (var c in outline.Commands)
        {
            if (c.Type == OutlineCommandType.MoveTo)
                contours.Add([]);
            contours[^1].Add(c);
        }

        contours.Count.ShouldBe(2);
        return (OutlineBounds.Tight(GlyphOutline.FromCommands([.. contours[0]])), OutlineBounds.Tight(GlyphOutline.FromCommands([.. contours[1]])));
    }

    /// <summary>A straight stroke from (x0, y0) to (x1, y1) with the given thickness along its normal, clockwise.</summary>
    private static GlyphOutline Diagonal(float x0, float y0, float x1, float y1, float thickness)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        // Offset along the x axis that gives the thickness along the normal.
        float w = thickness * length / dy;
        return GlyphOutline.FromCommands(TestOutlines.Polygon((x0, y0), (x1, y1), (x1 + w, y1), (x0 + w, y0)));
    }

    /// <summary>The distance between the two long edges of a <see cref="Diagonal"/> after fitting.</summary>
    private static float DiagonalThickness(GlyphOutline outline)
    {
        var p = outline.Commands.Where(c => c.Type != OutlineCommandType.Close).Select(c => (c.EndX, c.EndY)).ToArray();
        p.Length.ShouldBe(4);
        float dx = p[1].EndX - p[0].EndX, dy = p[1].EndY - p[0].EndY;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        float nx = dy / length, ny = -dx / length;
        float d2 = MathF.Abs(((p[2].EndX - p[0].EndX) * nx) + ((p[2].EndY - p[0].EndY) * ny));
        float d3 = MathF.Abs(((p[3].EndX - p[0].EndX) * nx) + ((p[3].EndY - p[0].EndY) * ny));
        return (d2 + d3) / 2f;
    }
}
