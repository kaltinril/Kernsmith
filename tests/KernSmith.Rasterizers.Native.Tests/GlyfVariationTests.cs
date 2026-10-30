using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 171 — how <see cref="GlyfTable"/> applies per-glyph variation deltas: to the raw points
/// of simple glyphs, to the placement offsets of composite components (before any scaled-offset
/// transform), and to the phantom points that carry the advance and the horizontal origin.
/// </summary>
public class GlyfVariationTests
{
    [Fact]
    public void SimpleGlyph_PointsMoveByTheirDeltas()
    {
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider { [0] = ([1, 2, 3, 4, 0, 0, 0, 0], [5, 6, 7, 8, 0, 0, 0, 0]) };

        var glyph = table.GetGlyph(0, provider, out var phantom);

        glyph.Contours[0].Points.ShouldBe(
        [
            new GlyphPoint(1, 5, true), new GlyphPoint(102, 6, true),
            new GlyphPoint(103, 107, true), new GlyphPoint(4, 108, true),
        ]);
        phantom.ShouldBe(new PhantomDeltas(0f, 0f, OutlineVaried: true));
    }

    [Fact]
    public void SimpleGlyph_DefaultCoordinatesAndContoursReachTheProvider()
    {
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider();

        table.GetGlyph(0, provider, out _);

        var call = provider.Calls.ShouldHaveSingleItem();
        call.Glyph.ShouldBe(0);
        call.PointCount.ShouldBe(4);
        call.Xs.ShouldBe([0, 100, 100, 0]);
        call.Ys.ShouldBe([0, 0, 100, 100]);
        call.Ends.ShouldBe([3]);
    }

    [Fact]
    public void Composite_ComponentOffsetMovesBeforeTheComponentIsPlaced()
    {
        // The component is scaled by 0.5 and placed at (+50, -25); a (+10, +4) delta on
        // "point" 0 — the component — moves the whole square to (+60, -21).
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider { [1] = ([10, 0, 0, 0, 0], [4, 0, 0, 0, 0]) };

        var glyph = table.GetGlyph(1, provider, out var phantom);

        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(60, -21, true));
        glyph.Contours[0].Points[2].ShouldBe(new GlyphPoint(110, 29, true));
        phantom.OutlineVaried.ShouldBeTrue();

        // The composite's own data addresses components, not points, and is never inferred.
        var call = provider.Calls.Single(c => c.Glyph == 1);
        call.PointCount.ShouldBe(1);
        call.Ends.ShouldBeEmpty();
    }

    [Fact]
    public void Composite_ScaledOffset_ScalesTheVariedOffset()
    {
        // With SCALED_COMPONENT_OFFSET the 0.5 scale applies to the delta-adjusted offset:
        // (50 + 10, -25 + 5) x 0.5 = (30, -10).
        var table = BuildTable(scaledOffset: true);
        var provider = new FakeProvider { [1] = ([10, 0, 0, 0, 0], [5, 0, 0, 0, 0]) };

        var glyph = table.GetGlyph(1, provider, out _);

        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(30, -10, true));
    }

    [Fact]
    public void Composite_ComponentsAreVariedAtTheirOwnLevel()
    {
        // The square's own deltas apply before the composite scales it by 0.5.
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider { [0] = ([20, 20, 20, 20, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0]) };

        var glyph = table.GetGlyph(1, provider, out var phantom);

        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(60, -25, true));
        phantom.OutlineVaried.ShouldBeTrue();
    }

    [Fact]
    public void PhantomPoints_ReportTheAdvanceAndShiftTheOrigin()
    {
        // The left phantom point is the horizontal origin: moving it by -3 moves the outline
        // +3 relative to the pen. The right one carries the advance.
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider { [0] = ([0, 0, 0, 0, -3, 7, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0]) };

        var glyph = table.GetGlyph(0, provider, out var phantom);

        phantom.ShouldBe(new PhantomDeltas(-3f, 7f, OutlineVaried: false));
        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(3, 0, true));
        glyph.Contours[0].Points[1].ShouldBe(new GlyphPoint(103, 0, true));
    }

    [Fact]
    public void EmptyGlyph_StillReportsPhantomDeltas()
    {
        // A space has no outline but its advance can still vary through its phantom points.
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider { [2] = ([0, 40, 0, 0], [0, 0, 0, 0]) };

        var glyph = table.GetGlyph(2, provider, out var phantom);

        glyph.IsEmpty.ShouldBeTrue();
        phantom.ShouldBe(new PhantomDeltas(0f, 40f, OutlineVaried: false));
    }

    [Fact]
    public void NoProvider_IsTheStaticGlyph()
    {
        var table = BuildTable(scaledOffset: false);

        var glyph = table.GetGlyph(1, variations: null, out var phantom);

        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(50, -25, true));
        phantom.ShouldBe(new PhantomDeltas(0f, 0f, OutlineVaried: false));
    }

    [Fact]
    public void Composite_WithoutUseMyMetrics_IsPlacedByItsOwnPhantomPoint()
    {
        // The square's pp1 moves by -3, the composite's own by +5: the composite's own wins.
        var table = BuildTable(scaledOffset: false);
        var provider = new FakeProvider
        {
            [0] = ([0, 0, 0, 0, -3, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0]),
            [1] = ([0, 5, 0, 0, 0], [0, 0, 0, 0, 0]),
        };

        var glyph = table.GetGlyph(1, provider, out var phantom);

        glyph.Origin.ShouldBe(new HorizontalOrigin(1, 50, 5f));
        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(45, -25, true));
        phantom.Left.ShouldBe(5f);
    }

    [Fact]
    public void Composite_WithUseMyMetrics_IsPlacedByTheComponentsPhantomPoint()
    {
        // USE_MY_METRICS hands the composite the square's phantom points: its varied pp1 (-3)
        // places the outline and its pp2 (+7) carries the advance; the composite's own phantom
        // deltas (+5, +11) are used for neither.
        var table = BuildTable(scaledOffset: false, useMyMetrics: true);
        var provider = new FakeProvider
        {
            [0] = ([0, 0, 0, 0, -3, 7, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0]),
            [1] = ([0, 5, 11, 0, 0], [0, 0, 0, 0, 0]),
        };

        var glyph = table.GetGlyph(1, provider, out var phantom);

        glyph.Origin.ShouldBe(new HorizontalOrigin(0, 0, -3f));
        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(53, -25, true));
        phantom.ShouldBe(new PhantomDeltas(-3f, 7f, OutlineVaried: false));
    }

    [Fact]
    public void Composite_WithUseMyMetrics_NamesTheComponent_WhenStatic()
    {
        var table = BuildTable(scaledOffset: false, useMyMetrics: true);

        var glyph = table.GetGlyph(1);

        glyph.Origin.ShouldBe(new HorizontalOrigin(0, 0, 0f));
        glyph.Contours[0].Points[0].ShouldBe(new GlyphPoint(50, -25, true));
    }

    /// <summary>
    /// Glyph 0: a 100x100 square. Glyph 1: a composite placing glyph 0 at half scale, offset
    /// (+50, -25), optionally with SCALED_COMPONENT_OFFSET or USE_MY_METRICS. Glyph 2: empty.
    /// </summary>
    private static GlyfTable BuildTable(bool scaledOffset, bool useMyMetrics = false)
    {
        byte[] simple = Be.Concat(
            Be.I16(1),
            Be.I16(0), Be.I16(0), Be.I16(100), Be.I16(100),
            Be.U16(3),
            Be.U16(0),
            [0x01, 0x01, 0x01, 0x01],
            Be.I16(0), Be.I16(100), Be.I16(0), Be.I16(-100),
            Be.I16(0), Be.I16(0), Be.I16(100), Be.I16(0));

        ushort flags = 0x0001 | 0x0002 | 0x0008;    // words | xy values | scale
        if (scaledOffset)
            flags |= 0x0800;
        if (useMyMetrics)
            flags |= 0x0200;

        byte[] composite = Be.Concat(
            Be.I16(-1),
            Be.I16(50), Be.I16(-25), Be.I16(100), Be.I16(25),
            Be.U16(flags),
            Be.U16(0),
            Be.I16(50), Be.I16(-25),
            Be.I16(8192));

        byte[] glyf = Be.Concat(simple, composite);
        byte[] loca = Be.Concat(
            Be.U32(0), Be.U32((uint)simple.Length), Be.U32((uint)glyf.Length), Be.U32((uint)glyf.Length));

        return new GlyfTable(glyf, LocaTable.Parse(loca, numGlyphs: 3, longFormat: true), maxComponentDepth: 64);
    }

    private sealed class FakeProvider : IGlyphVariationProvider
    {
        private readonly Dictionary<int, (float[] Dx, float[] Dy)> _deltas = [];

        public List<(int Glyph, int PointCount, int[] Xs, int[] Ys, int[] Ends)> Calls { get; } = [];

        public (float[] Dx, float[] Dy) this[int glyph]
        {
            set => _deltas[glyph] = value;
        }

        public bool TryGetDeltas(
            int glyphIndex, int pointCount, ReadOnlySpan<int> xs, ReadOnlySpan<int> ys,
            ReadOnlySpan<int> contourEnds, out float[] dx, out float[] dy)
        {
            Calls.Add((glyphIndex, pointCount, xs.ToArray(), ys.ToArray(), contourEnds.ToArray()));

            if (_deltas.TryGetValue(glyphIndex, out var deltas))
            {
                deltas.Dx.Length.ShouldBe(pointCount + 4);
                (dx, dy) = deltas;
                return true;
            }

            dx = dy = [];
            return false;
        }
    }
}
