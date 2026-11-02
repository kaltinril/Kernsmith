using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="HintTuning"/> is the <c>Normal</c> hinter's constants as one record, so a tuning
/// tool can sweep them: the defaults are the shipped behaviour, an explicit record reaches the
/// fit, and <see cref="HintTuning.Current"/> is what the pipeline uses when none is passed.
/// </summary>
public class HintTuningTests
{
    private const float Advance = 5.011f;

    [Fact]
    public void Default_IsTheShippedConstants()
    {
        var d = HintTuning.Default;
        d.MinEdgeLength.ShouldBe(0.05f);
        d.MinLoneEdgeLength.ShouldBe(0.7f);
        d.MergeDistance.ShouldBe(0.3f);
        d.StandardStemTolerance.ShouldBe(0.1f);
        d.PhaseShiftPenalty.ShouldBe(0.25f);
        d.MaxShift.ShouldBe(1f);
        d.MinCounter.ShouldBe(0.5f);
        d.XHeightRoundUp.ShouldBe(2f / 3f);
        d.TaperStartPpem.ShouldBe(16f);
        d.TaperEndPpem.ShouldBe(40f);
        d.XTaperEndPpem.ShouldBe(24f);
        d.OriginNudge.ShouldBe(0.25f);
        d.SlopeToleranceX.ShouldBe(0.07f);
        d.SlopeToleranceY.ShouldBe(0.12f);
        d.LeftBearingSlack.ShouldBe(0.25f);
        d.HairlineMaxWidth.ShouldBe(0.5f);
        d.HairlineWeight.ShouldBe(0.6f);
        d.HairlineFullPpem.ShouldBe(12f);
        d.HairlineEndPpem.ShouldBe(14f);
        d.DotLengthEm.ShouldBe(0.08f);
        HintTuning.Current.ShouldBe(HintTuning.Default);
    }

    [Fact]
    public void Fit_UsesTheTuningItIsGiven_LeftBearingSlack()
    {
        // A stem at 1.95 whose fit centres it in column 2 (left edge 2.09, 0.14 above the font's
        // bearing): the shipped quarter-pixel slack keeps it there; with no slack the ink may not
        // sit above the font's bearing at all and moves a column left, to 1.09.
        var stem = Rectangle(1.95f, 0f, 2.77f, 6f);
        var metrics = NormalHintMetrics.WithoutZones(maxStem: 1.45f);
        var shipped = NormalHinter.Fit(stem, 4.72f, metrics, mono: false, HintTuning.Default);
        var tight = NormalHinter.Fit(stem, 4.72f, metrics, mono: false, HintTuning.Default with { LeftBearingSlack = 0f });

        shipped.ShouldNotBeNull();
        tight.ShouldNotBeNull();
        VerticalStems(shipped!.Value.Outline)[0].Left.ShouldBe(2.09f, 0.02f);
        VerticalStems(tight!.Value.Outline)[0].Left.ShouldBe(1.09f, 0.02f);
    }

    [Fact]
    public void Fit_UsesTheTuningItIsGiven_MaxShift()
    {
        // With no room to move, nothing snaps: the fit either declines or leaves every x where it was.
        var metrics = NormalHintMetrics.WithoutZones(maxStem: 1.45f);
        var frozen = NormalHinter.Fit(TwoStems(), Advance, metrics, mono: false, HintTuning.Default with { MaxShift = 0f });

        if (frozen is { } f)
        {
            foreach (var (left, right) in VerticalStems(f.Outline))
                (left is 0.63f or 3.58f).ShouldBeTrue($"stem at {left:F2}-{right:F2} moved although MaxShift is 0");
        }

        var free = NormalHinter.Fit(TwoStems(), Advance, metrics, mono: false, HintTuning.Default);
        free.ShouldNotBeNull();
        VerticalStems(free!.Value.Outline).ShouldContain(s => MathF.Floor(s.Left) == MathF.Floor(s.Right - 1e-3f), "the shipped tuning does snap the stems");
    }

    [Fact]
    public void Strength_FollowsTheTaperWindow()
    {
        NormalHinter.Strength(30f).ShouldBe(10f / 24f, 1e-5f);
        NormalHinter.Strength(30f, HintTuning.Default with { TaperStartPpem = 28f, TaperEndPpem = 32f }).ShouldBe(0.5f, 1e-5f);
        NormalHinter.Strength(16f, HintTuning.Default with { TaperStartPpem = 12f, TaperEndPpem = 14f }).ShouldBe(0f);
        NormalHinter.XStrength(20f, HintTuning.Default with { TaperStartPpem = 12f, XTaperEndPpem = 28f }).ShouldBe(0.5f, 1e-5f);
    }

    private static GlyphOutline TwoStems() =>
        Concat(Rectangle(0.63f, 0f, 1.45f, 6f), Rectangle(3.58f, 0f, 4.40f, 6f));

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

        stems.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return stems;
    }
}
