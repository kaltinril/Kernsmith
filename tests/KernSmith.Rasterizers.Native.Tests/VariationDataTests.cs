using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 171 — the variation data formats, decoded from hand-assembled bytes: packed point
/// numbers and deltas, region scalars, the item variation store and its index maps, and
/// <c>gvar</c> glyph variation data (shared/private points, embedded and intermediate peaks).
/// Expected values come from the OpenType "Font Variations Common Table Formats" and overview
/// chapters.
/// </summary>
public class VariationDataTests
{
    // ------------------------------------------------------------ packed deltas

    [Fact]
    public void PackedDeltas_DecodeTheSpecExample()
    {
        // "03 0A 97 00 C6 87 41 10 22 FB 34": four bytes, eight zeros, two words.
        byte[] data = [0x03, 0x0A, 0x97, 0x00, 0xC6, 0x87, 0x41, 0x10, 0x22, 0xFB, 0x34];
        var deltas = new int[14];

        var reader = new FontReader(data);
        PackedData.ReadDeltas(ref reader, deltas);

        deltas.ShouldBe([10, -105, 0, -58, 0, 0, 0, 0, 0, 0, 0, 0, 4130, -1228]);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void PackedDeltas_StopAtTheLogicalCount()
    {
        // The second run belongs to whatever follows; it must not be consumed.
        byte[] data = [0x01, 0x05, 0x06, 0x00, 0x07];
        var deltas = new int[2];

        var reader = new FontReader(data);
        PackedData.ReadDeltas(ref reader, deltas);

        deltas.ShouldBe([5, 6]);
        reader.Position.ShouldBe(3);
    }

    [Fact]
    public void PackedDeltas_RunPastTheLogicalCount_IsMalformed()
    {
        byte[] data = [0x03, 0x01, 0x02, 0x03, 0x04];
        var deltas = new int[2];

        Should.Throw<FontFormatException>(() =>
        {
            var reader = new FontReader(data);
            PackedData.ReadDeltas(ref reader, deltas);
        });
    }

    // ------------------------------------------------------------ packed point numbers

    [Fact]
    public void PackedPoints_ZeroCount_MeansAllPoints()
    {
        byte[] data = [0x00];

        var reader = new FontReader(data);
        PackedData.ReadPointNumbers(ref reader).ShouldBeNull();
        reader.Position.ShouldBe(1);
    }

    [Fact]
    public void PackedPoints_AreRunLengthDifferences()
    {
        // Count 3; a two-byte run (5, 1) -> points 5, 6; then a one-word run (10) -> point 16.
        byte[] data = [0x03, 0x01, 0x05, 0x01, 0x80, 0x00, 0x0A];

        var reader = new FontReader(data);
        var points = PackedData.ReadPointNumbers(ref reader);

        points.ShouldBe([(ushort)5, (ushort)6, (ushort)16]);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void PackedPoints_TwoByteCount()
    {
        // 0x81 0x22 is a count of 290; three byte runs of 128, 128 and 34 ones -> points 1..290.
        var data = new List<byte> { 0x81, 0x22 };
        foreach (int run in new[] { 128, 128, 34 })
        {
            data.Add((byte)(run - 1));
            data.AddRange(Enumerable.Repeat((byte)1, run));
        }

        var reader = new FontReader(data.ToArray());
        var points = PackedData.ReadPointNumbers(ref reader);

        points.ShouldNotBeNull();
        points!.Length.ShouldBe(290);
        points[0].ShouldBe((ushort)1);
        points[289].ShouldBe((ushort)290);
    }

    // ------------------------------------------------------------ region scalars

    [Fact]
    public void RegionScalar_MatchesTheSpecIntermediateExample()
    {
        // Region (0.3, 0.15)..(1, 1) peaking at (0.7, 0.5); instance (0.5, 0.35) -> 0.5 x 0.571429.
        float x = RegionScalar.ForAxis(0.3f, 0.7f, 1f, 0.5f);
        float y = RegionScalar.ForAxis(0.15f, 0.5f, 1f, 0.35f);

        x.ShouldBe(0.5f, 0.00001f);
        y.ShouldBe(0.571429f, 0.00001f);
        (x * y).ShouldBe(0.285714f, 0.00001f);
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 0.2f, 0.2f)]      // the gvar chapter's Skia example, R1 weight
    [InlineData(0f, 0f, 0f, 0.7f, 1f)]        // a zero peak ignores the axis
    [InlineData(-1f, 0f, 1f, 0.7f, 1f)]       // ... whatever start and end say
    [InlineData(0f, 1f, 1f, -0.2f, 0f)]       // outside the region
    [InlineData(-1f, -1f, 0f, -0.25f, 0.25f)] // negative side, between peak and end
    [InlineData(-1f, -1f, 0f, -1f, 1f)]       // at the peak
    [InlineData(0.5f, 0.2f, 1f, 0.3f, 1f)]    // start > peak: invalid, axis ignored
    [InlineData(-0.5f, 0.5f, 1f, 0.9f, 1f)]   // crosses zero with a non-zero peak: ignored
    public void RegionScalar_PerAxisRules(float start, float peak, float end, float coordinate, float expected)
    {
        RegionScalar.ForAxis(start, peak, end, coordinate).ShouldBe(expected, 0.00001f);
    }

    // ------------------------------------------------------------ item variation store

    [Fact]
    public void ItemVariationStore_SumsScaledRegionDeltas()
    {
        var store = ItemVariationStore.Parse(BuildItemVariationStore());

        var positive = store.ComputeRegionScalars([0.5f]);
        store.GetDelta(0, 0, positive).ShouldBe(150f);       // 300 x 0.5, region 1 inactive
        store.GetDelta(0, 1, positive).ShouldBe(-1f);        // -2 x 0.5

        var negative = store.ComputeRegionScalars([-0.25f]);
        store.GetDelta(0, 0, negative).ShouldBe(-2.5f);      // -10 x 0.25
        store.GetDelta(0, 1, negative).ShouldBe(1.25f);      // 5 x 0.25
    }

    [Fact]
    public void ItemVariationStore_LongWords_AreInt32ThenInt16()
    {
        var store = ItemVariationStore.Parse(BuildItemVariationStore());
        var scalars = store.ComputeRegionScalars([1f]);

        store.GetDelta(1, 0, scalars).ShouldBe(100000f);
    }

    [Fact]
    public void ItemVariationStore_NoVariationIndexAndNullSubtables_AreZero()
    {
        var store = ItemVariationStore.Parse(BuildItemVariationStore());
        var scalars = store.ComputeRegionScalars([1f]);

        store.GetDelta(0xFFFF, 0xFFFF, scalars).ShouldBe(0f);
        store.GetDelta(2, 0, scalars).ShouldBe(0f);          // NULL subtable offset
        store.GetDelta(9, 0, scalars).ShouldBe(0f);          // past the subtable array
        store.GetDelta(0, 99, scalars).ShouldBe(0f);         // past the subtable's rows
    }

    // ------------------------------------------------------------ delta-set index maps

    [Fact]
    public void DeltaSetIndexMap_Format0_SplitsOuterAndInner()
    {
        // entryFormat 0x13: two-byte entries, four inner bits.
        byte[] data = Be.Concat([0x00, 0x13], Be.U16(3), Be.U16(0x0012), Be.U16(0x0003), Be.U16(0x0105));
        var map = DeltaSetIndexMap.Parse(data);

        map.Map(0).ShouldBe(((ushort)1, (ushort)2));
        map.Map(1).ShouldBe(((ushort)0, (ushort)3));
        map.Map(2).ShouldBe(((ushort)16, (ushort)5));
        map.Map(7).ShouldBe(((ushort)16, (ushort)5));         // past the end: the last entry
    }

    [Fact]
    public void DeltaSetIndexMap_Format1_HasA32BitCount()
    {
        // entryFormat 0x00: one-byte entries, one inner bit.
        byte[] data = Be.Concat([0x01, 0x00], Be.U32(2), [0x03, 0x02]);
        var map = DeltaSetIndexMap.Parse(data);

        map.Map(0).ShouldBe(((ushort)1, (ushort)1));
        map.Map(1).ShouldBe(((ushort)1, (ushort)0));
    }

    // ------------------------------------------------------------ gvar

    private static readonly int[] SquareXs = [0, 100, 100, 0];
    private static readonly int[] SquareYs = [0, 0, 100, 100];
    private static readonly int[] SquareEnds = [3];

    [Fact]
    public void Gvar_SharedPointsAndSharedTuple_InferUnreferencedPoints()
    {
        var gvar = GvarTable.Parse(BuildGvar());

        gvar.AxisCount.ShouldBe(1);
        gvar.TryGetDeltas(0, [1f], 4, SquareXs, SquareYs, SquareEnds, out var dx, out var dy).ShouldBeTrue();

        // Points 0 and 2 carry 10 and 20; 1 and 3 sit at the far ends of their neighbours'
        // X range and so take the nearer neighbour's delta. The intermediate tuple is inactive.
        dx.ShouldBe([10f, 20f, 20f, 10f, 0f, 0f, 0f, 0f]);
        dy.ShouldBe(new float[8]);
    }

    [Fact]
    public void Gvar_IntermediateRegionWithPrivatePoints_MovesThePhantomPoint()
    {
        var gvar = GvarTable.Parse(BuildGvar());

        gvar.TryGetDeltas(0, [0.5f], 4, SquareXs, SquareYs, SquareEnds, out var dx, out _).ShouldBeTrue();
        dx.ShouldBe([5f, 10f, 10f, 5f, 0f, 100f, 0f, 0f]);

        // Halfway up the intermediate region's rising edge (0.25 -> 0.5).
        gvar.TryGetDeltas(0, [0.375f], 4, SquareXs, SquareYs, SquareEnds, out dx, out _).ShouldBeTrue();
        dx.ShouldBe([3.75f, 7.5f, 7.5f, 3.75f, 0f, 50f, 0f, 0f]);
    }

    [Fact]
    public void Gvar_EmbeddedPeakWithAllPoints_AppliesEveryDelta()
    {
        var gvar = GvarTable.Parse(BuildGvar());

        gvar.TryGetDeltas(1, [-0.5f], 4, SquareXs, SquareYs, SquareEnds, out var dx, out var dy).ShouldBeTrue();

        dx.ShouldBe([0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3.5f, 4f]);
        dy.ShouldBe(new float[8]);
    }

    [Fact]
    public void Gvar_NoApplicableTuple_ReportsNoDeltas()
    {
        var gvar = GvarTable.Parse(BuildGvar());

        gvar.TryGetDeltas(0, [0f], 4, SquareXs, SquareYs, SquareEnds, out _, out _).ShouldBeFalse();
        gvar.TryGetDeltas(0, [-0.5f], 4, SquareXs, SquareYs, SquareEnds, out _, out _).ShouldBeFalse();
        gvar.TryGetDeltas(2, [1f], 4, SquareXs, SquareYs, SquareEnds, out _, out _).ShouldBeFalse(); // no data
    }

    [Fact]
    public void Gvar_CompositeEntries_AreNeverInferred()
    {
        // The same data read as a two-component composite: no contours, so the unreferenced
        // component (1) keeps a zero delta instead of being interpolated.
        var gvar = GvarTable.Parse(BuildGvar());

        gvar.TryGetDeltas(0, [1f], 2, [], [], [], out var dx, out _).ShouldBeTrue();

        dx.ShouldBe([10f, 0f, 20f, 0f, 0f, 0f]);
    }

    /// <summary>
    /// Three-glyph, one-axis <c>gvar</c> with short offsets and one shared tuple (peak 1.0).
    /// Glyph 0: shared points {0, 2} with X deltas (10, 20) on the shared tuple, plus an
    /// intermediate region (0.25..0.5..1.0) with private point {5} moving X by 100.
    /// Glyph 1: one embedded peak (-1.0) with deltas for all eight points.
    /// Glyph 2: no variation data.
    /// </summary>
    internal static byte[] BuildGvar()
    {
        byte[] glyph0 = Be.Concat(
            Be.U16(0x8002),                         // SHARED_POINT_NUMBERS | 2 tuples
            Be.U16(18),                             // dataOffset
            Be.U16(4), Be.U16(0x0000),              // tuple 1: 4 bytes, shared tuple 0
            Be.U16(7), Be.U16(0xE000),              // tuple 2: 7 bytes, embedded | intermediate | private
            Be.F2Dot14(0.5), Be.F2Dot14(0.25), Be.F2Dot14(1.0),
            [0x02, 0x01, 0x00, 0x02],               // shared points: 0, 2
            [0x01, 0x0A, 0x14], [0x81],             // tuple 1: X (10, 20), Y zeros
            [0x01, 0x00, 0x05],                     // tuple 2 private points: 5
            [0x40, 0x00, 0x64], [0x80]);            // tuple 2: X (100), Y zero

        byte[] glyph1 = Be.Concat(
            Be.U16(0x0001),                         // 1 tuple, no shared points
            Be.U16(10),                             // dataOffset
            Be.U16(11), Be.U16(0xA000),             // 11 bytes, embedded | private
            Be.F2Dot14(-1.0),
            [0x00],                                 // all points
            [0x07, 1, 2, 3, 4, 5, 6, 7, 8],         // X
            [0x87]);                                // Y: eight zeros

        glyph0 = Pad2(glyph0);
        glyph1 = Pad2(glyph1);

        const int HeaderLength = 20;
        int offsetsLength = 4 * 2;                  // glyphCount + 1 short offsets
        int sharedTuplesOffset = HeaderLength + offsetsLength;
        int dataArrayOffset = sharedTuplesOffset + 2;

        return Be.Concat(
            Be.U16(1), Be.U16(0),                   // version
            Be.U16(1),                              // axisCount
            Be.U16(1),                              // sharedTupleCount
            Be.U32((uint)sharedTuplesOffset),
            Be.U16(3),                              // glyphCount
            Be.U16(0),                              // flags: short offsets
            Be.U32((uint)dataArrayOffset),
            Be.U16(0),
            Be.U16((ushort)(glyph0.Length / 2)),
            Be.U16((ushort)((glyph0.Length + glyph1.Length) / 2)),
            Be.U16((ushort)((glyph0.Length + glyph1.Length) / 2)),
            Be.F2Dot14(1.0),                        // shared tuple 0
            glyph0,
            glyph1);
    }

    private static byte[] Pad2(byte[] data) => data.Length % 2 == 0 ? data : [.. data, 0];

    /// <summary>
    /// One-axis store, two regions — R0 (0, 1, 1) and R1 (-1, -1, 0) — and three subtables:
    /// 0 has rows (300, -10) and (-2, 5) as word + byte; 1 is LONG_WORDS with (100000, -7);
    /// 2 is a NULL offset.
    /// </summary>
    internal static byte[] BuildItemVariationStore()
    {
        const int HeaderLength = 2 + 4 + 2 + (3 * 4);
        byte[] regions = Be.Concat(
            Be.U16(1), Be.U16(2),
            Be.F2Dot14(0), Be.F2Dot14(1), Be.F2Dot14(1),
            Be.F2Dot14(-1), Be.F2Dot14(-1), Be.F2Dot14(0));
        byte[] data0 = Be.Concat(
            Be.U16(2), Be.U16(1), Be.U16(2), Be.U16(0), Be.U16(1),
            Be.I16(300), [unchecked((byte)-10)],
            Be.I16(-2), [0x05]);
        byte[] data1 = Be.Concat(
            Be.U16(1), Be.U16(0x8001), Be.U16(2), Be.U16(0), Be.U16(1),
            Be.I32(100000), Be.I16(-7));

        int regionsOffset = HeaderLength;
        int data0Offset = regionsOffset + regions.Length;
        int data1Offset = data0Offset + data0.Length;

        return Be.Concat(
            Be.U16(1),
            Be.U32((uint)regionsOffset),
            Be.U16(3),
            Be.U32((uint)data0Offset), Be.U32((uint)data1Offset), Be.U32(0),
            regions, data0, data1);
    }
}
