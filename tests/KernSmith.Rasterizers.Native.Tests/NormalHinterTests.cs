using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 188 — the <c>Normal</c> auto-hinter's building blocks on synthetic pixel-space
/// outlines: horizontal phase alignment of vertical stems, stem-width classes, counters, the
/// monotone x map and the by-side placement within the font's advance.
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
        // The longer stem anchors the glyph and moves less than a pixel (into the column its
        // phase chose, placed so the left bearing is not above the font's); the other follows
        // it so the 2.95 px pitch becomes exactly 3 (a 2 px counter, as FreeType renders 'h'),
        // rather than each stem rounding on its own into a 2 px pitch.
        var h = TwoStems();

        var result = NormalHinter.Fit(h, Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        result.ShouldNotBeNull();
        var stems = VerticalStems(result!.Value.Outline);
        float leftCentre = (stems[0].Left + stems[0].Right) / 2f;
        float rightCentre = (stems[1].Left + stems[1].Right) / 2f;
        MathF.Abs(leftCentre - (LeftStem + (StemWidth / 2f))).ShouldBeLessThan(1f);
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
    public void Fit_Mono_KeepsAColumnBetweenTwoThinStems_ByChoosingThePhaseThatHasRoom()
    {
        // Roboto's '"' at 9.1 ppem: two 0.63 px strokes 0.57 px apart. With the first rounded
        // into column 1 the second's whole-pixel slot is column 3 — a 1.2 px move, past the
        // cap — so it was left between columns and the two drew as one block. The phase that
        // puts the first stroke in column 0 leaves column 2 within the second's reach: both
        // land on whole pixels with a column of white between.
        var quote = Concat(Rectangle(0.61f, 4.63f, 1.24f, 6.82f), Rectangle(1.81f, 4.63f, 2.43f, 6.82f));

        var result = NormalHinter.Fit(quote, 3.04f, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: true);

        result.ShouldNotBeNull();
        var stems = VerticalStems(result!.Value.Outline);
        stems.Count.ShouldBe(2);
        foreach (var (left, right) in stems)
        {
            left.ShouldBe(MathF.Round(left), 1e-3f, $"stem {left:F2}-{right:F2} is off the grid");
            (right - left).ShouldBe(1f, 1e-3f);
        }

        (stems[1].Left - stems[0].Right).ShouldBe(1f, 1e-3f, "a column of white between the strokes");
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
    public void Fit_KeepsTheFontsAdvance()
    {
        // 'h': the stems move into whole columns but the advance is the linear 5.01 rounded, 5,
        // in grayscale and in bilevel alike — not the 6 that following the phase-shifted right
        // edge gave, and not the ink's growth (in bilevel both stems widen to a pixel). The side
        // bearings report the split: the left at or just under the font's, the rest right.
        foreach (bool mono in new[] { false, true })
        {
            var result = NormalHinter.Fit(TwoStems(), Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono);

            result.ShouldNotBeNull();
            result!.Value.Advance.ShouldBe(5, $"mono {mono}");
            var bounds = OutlineBounds.Tight(result.Value.Outline);
            bounds.XMin.ShouldBeLessThanOrEqualTo(LeftStem + 0.25f + 1e-3f, $"mono {mono}: the left bearing is at or just under the font's");
            result.Value.LeftDelta.ShouldBe(bounds.XMin - LeftStem, 1e-3f);
            result.Value.RightDelta.ShouldBe((5f - bounds.XMax) - (Advance - RightStem - StemWidth), 1e-3f);
        }
    }

    [Fact]
    public void Fit_GivesTheSameAdvance_ToTheSameStem_AtAnyPhase()
    {
        // Roboto 'i' and 'l' at a 10 px cell: a 0.72 px stem with a 1.84 px advance. Centred in
        // a column the stem's right edge lands at 0.86 or 1.86 depending on which column its
        // phase picked; an advance rebuilt from that edge came out 1 for one phase and 2 for
        // another, and an advance of 1 leaves 0.28 px between two 'l's. The advance is the
        // linear one (2) whatever the phase.
        var metrics = NormalHintMetrics.WithoutZones(maxStem: 1.45f);

        var early = NormalHinter.Fit(Rectangle(0.52f, 0f, 1.24f, 7f), 1.84f, metrics, mono: false);
        var late = NormalHinter.Fit(Rectangle(0.60f, 0f, 1.32f, 7f), 1.84f, metrics, mono: false);

        early.ShouldNotBeNull();
        late.ShouldNotBeNull();
        early!.Value.Advance.ShouldBe(2);
        late!.Value.Advance.ShouldBe(2);
    }

    [Fact]
    public void Fit_PlacesBilevel_LikeGrayscale()
    {
        // Bilevel bearings are not rounded up to columns: a 1.3 px bearing on each side of a
        // stem keeps the font's advance (3.4 -> 3) and the left bearing at or just under 1.3,
        // the same placement grayscale gets. Two glyphs that then touch are the kerning bake's.
        var metrics = NormalHintMetrics.WithoutZones(maxStem: 1.45f);

        var gray = NormalHinter.Fit(Rectangle(1.3f, 0f, 2.12f, 7f), 2.12f + 1.3f, metrics, mono: false);
        var bilevel = NormalHinter.Fit(Rectangle(1.3f, 0f, 2.12f, 7f), 2.12f + 1.3f, metrics, mono: true);

        gray.ShouldNotBeNull();
        bilevel.ShouldNotBeNull();
        gray!.Value.Advance.ShouldBe(3);
        bilevel!.Value.Advance.ShouldBe(3);
        OutlineBounds.Tight(bilevel.Value.Outline).XMin.ShouldBe(1f, 1e-3f);
        OutlineBounds.Tight(gray.Value.Outline).XMin.ShouldBeLessThanOrEqualTo(1.55f);
    }

    [Fact]
    public void Fit_PadsARightHandStem_OnlyWithoutTheBake_AndOnlyUnderHalfAPixelOfWhite()
    {
        // A stem at 0.9 with 0.2 px of white after it: the fit centres it in column 1 (1.09 to
        // 1.91), 0.09 px short of its 2 px advance, and against a left-hand stem the pair fuses.
        // Without the kerning bake the glyph gets a column of advance; with it the advance is
        // the font's and the pair is opened in the kerning. A stem with 0.6 px of white is
        // never padded, however the fit placed it.
        var metrics = NormalHintMetrics.WithoutZones(maxStem: 1.45f);
        var tight = Rectangle(0.9f, 0f, 1.72f, 5f);

        var padded = NormalHinter.Fit(tight, 1.72f + 0.2f, metrics, mono: false, padStemPairs: true);
        var baked = NormalHinter.Fit(tight, 1.72f + 0.2f, metrics, mono: false, padStemPairs: false);
        var roomy = NormalHinter.Fit(tight, 1.72f + 0.6f, metrics, mono: false, padStemPairs: true);

        padded.ShouldNotBeNull();
        baked.ShouldNotBeNull();
        roomy.ShouldNotBeNull();
        baked!.Value.Advance.ShouldBe(2);
        padded!.Value.Advance.ShouldBe(3);
        padded.Value.RightStem.ShouldBeTrue();
        roomy!.Value.Advance.ShouldBe(2);
    }

    [Fact]
    public void Fit_ReportsStemEdges()
    {
        var h = NormalHinter.Fit(TwoStems(), Advance, NormalHintMetrics.WithoutZones(maxStem: 1.45f), mono: false);

        h.ShouldNotBeNull();
        h!.Value.LeftStem.ShouldBeTrue();
        h.Value.RightStem.ShouldBeTrue();
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
    [InlineData(16f, 1f)]
    [InlineData(28f, 0.5f)]
    [InlineData(40f, 0f)]
    [InlineData(60f, 0f)]
    public void Strength_FallsFromFullAt16Ppem_ToNothingAt40(float ppem, float expected)
    {
        NormalHinter.Strength(ppem).ShouldBe(expected, 1e-4f);
    }

    [Theory]
    [InlineData(9f, 1f)]
    [InlineData(16f, 1f)]
    [InlineData(20f, 0.5f)]
    [InlineData(24f, 0f)]
    [InlineData(40f, 0f)]
    public void XStrength_FallsFromFullAt16Ppem_ToNothingAt24(float ppem, float expected)
    {
        // The shape-changing part — stems snapped to columns, width classes, the by-side
        // placement — fades first; the zones, baseline and x-height alignment last.
        NormalHinter.XStrength(ppem).ShouldBe(expected, 1e-4f);
    }

    [Fact]
    public void Strengths_NeverRiseWithSize_AndXFadesFirst()
    {
        float previousY = 1f, previousX = 1f;
        for (float ppem = 6f; ppem <= 64f; ppem += 0.25f)
        {
            float y = NormalHinter.Strength(ppem);
            float x = NormalHinter.XStrength(ppem);
            y.ShouldBeLessThanOrEqualTo(previousY, $"y at {ppem}");
            x.ShouldBeLessThanOrEqualTo(previousX, $"x at {ppem}");
            x.ShouldBeLessThanOrEqualTo(y, $"x against y at {ppem}");
            previousY = y;
            previousX = x;
        }
    }

    [Fact]
    public void Fit_LeavesXAlone_WhereOnlyTheYTaperIsLeft()
    {
        // At 30 ppem the x strength is 0 and the y strength 0.42: the stem's top and bottom,
        // within that of a row, still snap; its x does not move at all, and the advance is the
        // font's. The result is also placed nowhere: no side bearing changes.
        var stem = Rectangle(0.63f, 0.2f, 1.45f, 6.8f);
        var metrics = new NormalHintMetrics(ScaledHintMetrics.WithoutZones(1.45f), 0.82f, Ppem: 30f);

        var result = NormalHinter.Fit(stem, 2.1f, metrics, mono: false);

        result.ShouldNotBeNull();
        result!.Value.Outline.Commands.Select(c => c.EndX).ToArray().ShouldBe(stem.Commands.Select(c => c.EndX).ToArray());
        var bounds = OutlineBounds.Tight(result.Value.Outline);
        bounds.YMax.ShouldBe(7f, 1e-3f);
        bounds.YMin.ShouldBe(0f, 1e-3f);
        result.Value.Advance.ShouldBe(2);
        result.Value.LeftDelta.ShouldBe(0f);
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
