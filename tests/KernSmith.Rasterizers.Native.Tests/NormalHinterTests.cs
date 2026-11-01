using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 188 — the <c>Normal</c> auto-hinter's building blocks on synthetic pixel-space
/// outlines: horizontal phase alignment of vertical stems, stem-width classes, counters, the
/// monotone x map and the advance rebuild.
/// </summary>
public class NormalHinterTests
{
    // Roboto 'h' at a 12px cell (9.1 ppem): two 0.82px stems whose ink straddles two columns
    // each (0.63-1.45 and 3.58-4.40), advance 5.01px.
    private const float StemWidth = 0.82f;
    private const float LeftStem = 0.63f;
    private const float RightStem = 3.58f;
    private const float Advance = 5.011f;

    // ---- Step 1: x phase alignment ----------------------------------------------------------

    [Fact]
    public void Fit_PutsEachThinStemInsideOneColumn_Grayscale()
    {
        var h = TwoStems();

        var result = NormalHinter.Fit(h, Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        var stems = VerticalStems(result!.Value.Outline);
        stems.Count.ShouldBe(2);
        foreach (var (left, right) in stems)
        {
            // Inside one column: the floor of the left edge is the floor of the right edge's
            // last covered point. The width is kept: grayscale centres a thin stem, it does not
            // widen it, so the column's coverage is the stem's true ink.
            MathF.Floor(left).ShouldBe(MathF.Floor(right - 1e-3f), $"stem {left:F2}-{right:F2} straddles a column boundary");
            (right - left).ShouldBe(StemWidth, 1f / 32f);
        }
    }

    [Fact]
    public void Fit_KeepsTheStemPitch_ToTheNearestPixel()
    {
        // The longer stem anchors the glyph and moves at most half a pixel; the other follows
        // it so the 2.95 px pitch becomes exactly 3 (a 2 px counter, as FreeType renders 'h'),
        // rather than each stem rounding on its own into a 2 px pitch.
        var h = TwoStems();

        var result = NormalHinter.Fit(h, Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        var stems = VerticalStems(result!.Value.Outline);
        float leftCentre = (stems[0].Left + stems[0].Right) / 2f;
        float rightCentre = (stems[1].Left + stems[1].Right) / 2f;
        MathF.Abs(leftCentre - (LeftStem + (StemWidth / 2f))).ShouldBeLessThanOrEqualTo(0.5f);
        (rightCentre - leftCentre).ShouldBe(3f, 1e-3f);
    }

    // ---- Step 2: stem widths and classes ----------------------------------------------------

    [Fact]
    public void Fit_Mono_WidensThinStemsToWholePixels_OnPixelBoundaries()
    {
        var h = TwoStems();

        var result = NormalHinter.Fit(h, Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        result.ShouldNotBeNull();
        foreach (var (left, right) in VerticalStems(result!.Value.Outline))
        {
            left.ShouldBe(MathF.Round(left), 1e-3f, "a mono stem edge sits on a pixel boundary");
            (right - left).ShouldBe(1f, 1e-3f, "a sub-pixel stem is exactly one pixel in mono");
        }
    }

    [Fact]
    public void Fit_SnapsStemsNearTheStandardWidth_ToTheStandard()
    {
        // Roboto's 'h' stems are 185 and 186 units; at 9 ppem that is 0.80 and 0.84 px around
        // a standard of 0.82. Both come out exactly the standard width, so equal stems stay equal.
        var stems = Concat(
            Rectangle(0.63f, 0f, 0.63f + 0.80f, 7f),
            Rectangle(3.58f, 0f, 3.58f + 0.84f, 5f));
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(1.45f), StandardVerticalStem: 0.82f, Ppem: 9f);

        var result = NormalHinter.Fit(stems, Advance, metrics, mono: false);

        result.ShouldNotBeNull();
        foreach (var (left, right) in VerticalStems(result!.Value.Outline))
            (right - left).ShouldBe(0.82f, 1f / 32f);
    }

    [Fact]
    public void Fit_KeepsAStemFarFromTheStandard_AtItsOwnWidth()
    {
        var stems = Rectangle(0.6f, 0f, 2.3f, 7f);
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(2.5f), StandardVerticalStem: 0.82f, Ppem: 9f);

        var result = NormalHinter.Fit(stems, 3f, metrics, mono: false);

        result.ShouldNotBeNull();
        var (left, right) = VerticalStems(result!.Value.Outline)[0];
        (right - left).ShouldBe(1.7f, 1f / 32f);
    }

    [Fact]
    public void Fit_Mono_KeepsACounterAtLeastOnePixel()
    {
        // Two 0.9 px stems 0.7 px apart (an 'm' at a tiny size): widened to a pixel each they
        // would touch, so the second moves over and the counter stays a white column.
        var stems = Concat(
            Rectangle(0.3f, 0f, 1.2f, 5f),
            Rectangle(1.9f, 0f, 2.8f, 5f));

        var result = NormalHinter.Fit(stems, 3.2f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        result.ShouldNotBeNull();
        var fitted = VerticalStems(result!.Value.Outline);
        (fitted[1].Left - fitted[0].Right).ShouldBeGreaterThanOrEqualTo(1f - 1e-3f);
    }

    [Fact]
    public void Fit_DetectsACurvedTerminal_AsAStem()
    {
        // A stroke whose inner side is straight and whose outer side is a curve arriving
        // vertically into a flat cut — the terminal of an 'S' or 'c'. Its outer extremum is a
        // node with one horizontal neighbour, which only half-tangent detection sees. Fitted,
        // the whole 0.84 px stroke lands inside one column.
        var terminal = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0.36f, 1f),
            OutlineCommand.Line(1.2f, 1f),
            OutlineCommand.Line(1.2f, 3f),
            OutlineCommand.Cubic(0.8f, 3f, 0.36f, 1.8f, 0.36f, 1f),
            OutlineCommand.CloseAt(0.36f, 1f),
        ]);

        var result = NormalHinter.Fit(terminal, 2f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        var bounds = OutlineBounds.Tight(result!.Value.Outline);
        MathF.Floor(bounds.XMin).ShouldBe(MathF.Floor(bounds.XMax - 1e-3f), $"stroke {bounds.XMin:F2}-{bounds.XMax:F2} straddles a column boundary");
        (bounds.XMax - bounds.XMin).ShouldBe(0.84f, 1f / 32f);
    }

    // ---- Step 4: the x map and the advance --------------------------------------------------

    [Fact]
    public void Fit_NeverReordersPointsAlongX()
    {
        // The x map is monotone: however the stems move, every point keeps its order along x
        // with every other point, so the outline cannot cross itself.
        var glyph = Concat(
            Rectangle(0.63f, 0f, 1.45f, 7f),
            Rectangle(1.6f, 2.4f, 3.5f, 3.1f),
            Rectangle(3.58f, 0f, 4.40f, 5f),
            Rectangle(4.9f, 1.2f, 5.3f, 4.8f));

        var result = NormalHinter.Fit(glyph, 6f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        result.ShouldNotBeNull();
        var before = glyph.Commands.Select(c => c.EndX).ToArray();
        var after = result!.Value.Outline.Commands.Select(c => c.EndX).ToArray();
        for (int i = 0; i < before.Length; i++)
        {
            for (int j = 0; j < before.Length; j++)
            {
                if (before[i] < before[j])
                    after[i].ShouldBeLessThanOrEqualTo(after[j], $"points {i} ({before[i]}) and {j} ({before[j]}) swapped");
            }
        }
    }

    [Fact]
    public void Fit_RebuildsTheAdvance_FromTheFittedRightEdge()
    {
        // 'h': stems moved into columns 1 and 4 (ink to 4.92), original right side bearing
        // 0.60: 4.92 + 0.60 rounds to 6 where the linear 5.01 rounded to 5. The side bearing
        // deltas report how much each side grew.
        var result = NormalHinter.Fit(TwoStems(), Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        result!.Value.Advance.ShouldBe(6);
        result.Value.LeftDelta.ShouldBe(0.455f, 0.05f);
        result.Value.RightDelta.ShouldBe(1.085f - 0.60f, 0.05f);
    }

    [Fact]
    public void Fit_KeepsInkOffThePenOrigin_WhenItWasOff()
    {
        // A stem at 0.3 px would centre in column 0 at 0.09; a bowl whose extremum bulges past
        // the placed node must not end up left of the pen, costing a blank column.
        var stem = Rectangle(0.3f, 0f, 1.1f, 5f);

        var result = NormalHinter.Fit(stem, 1.5f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        OutlineBounds.Tight(result!.Value.Outline).XMin.ShouldBeGreaterThanOrEqualTo(0f);
    }

    // ---- Taper ------------------------------------------------------------------------------

    [Theory]
    [InlineData(9f, 1f)]
    [InlineData(24f, 1f)]
    [InlineData(32f, 0.5f)]
    [InlineData(40f, 0f)]
    [InlineData(60f, 0f)]
    public void Strength_FallsFromFullAt24Ppem_ToNothingAt40(float ppem, float expected)
    {
        NormalHinter.Strength(ppem).ShouldBe(expected, 1e-4f);
    }

    [Fact]
    public void Fit_DoesNothing_AboveTheTaper()
    {
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(8f), 0f, Ppem: 48f);

        NormalHinter.Fit(TwoStems(), Advance, metrics, mono: false).ShouldBeNull();
    }

    // ---- Helpers ----------------------------------------------------------------------------

    /// <summary>Two vertical stems joined by nothing: the 'h' skeleton, clockwise (TrueType).</summary>
    private static GlyphOutline TwoStems() =>
        Concat(
            Rectangle(LeftStem, 0f, LeftStem + StemWidth, 7f),
            Rectangle(RightStem, 0f, RightStem + StemWidth, 5f));

    private static GlyphOutline Concat(params GlyphOutline[] outlines) =>
        GlyphOutline.FromCommands([.. outlines.SelectMany(o => o.Commands)]);

    /// <summary>An axis-aligned rectangle wound clockwise (TrueType), Y up.</summary>
    private static GlyphOutline Rectangle(float left, float bottom, float right, float top) =>
        GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(left, bottom),
            OutlineCommand.Line(left, top),
            OutlineCommand.Line(right, top),
            OutlineCommand.Line(right, bottom),
            OutlineCommand.CloseAt(left, bottom),
        ]);

    /// <summary>The (left, right) x extents of each rectangle contour, left to right.</summary>
    private static List<(float Left, float Right)> VerticalStems(GlyphOutline outline)
    {
        var stems = new List<(float, float)>();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var c in outline.Commands)
        {
            if (c.Type == OutlineCommandType.MoveTo)
            {
                min = float.MaxValue;
                max = float.MinValue;
            }

            min = MathF.Min(min, c.EndX);
            max = MathF.Max(max, c.EndX);
            if (c.Type == OutlineCommandType.Close)
                stems.Add((min, max));
        }

        stems.Sort();
        return stems;
    }
}
