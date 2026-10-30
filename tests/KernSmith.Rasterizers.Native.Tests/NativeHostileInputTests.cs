using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.Be;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 179 — regressions for the hostile-input bugs <see cref="NativeFuzzTests"/> found, each
/// minimised to a hand-built table. Malformed data must surface as the documented
/// <see cref="FontFormatException"/> (which the rasterizer reports as a
/// <see cref="RasterizationException"/>), never as an arithmetic or indexing exception.
/// </summary>
public class NativeHostileInputTests
{
    /// <summary>An empty item variation store: format 1, region list at 8, no subtables, no regions.</summary>
    private static readonly byte[] EmptyStore = Concat(U16(1), U32(8), U16(0), U16(0), U16(0));

    // ------------------------------------------------------------ variation table offsets

    [Theory]
    [InlineData(0x80000000u)] // above int.MaxValue: a checked cast overflowed
    [InlineData(0x00000100u)] // past the table: the span slice threw ArgumentOutOfRangeException
    public void Hvar_StoreOffsetOutsideTheTable_IsAFormatError(uint storeOffset)
    {
        var hvar = Concat(U16(1), U16(0), U32(storeOffset), U32(0), U32(0), U32(0));

        Should.Throw<FontFormatException>(() => HvarTable.Parse(hvar));
    }

    [Theory]
    [InlineData(0x80000000u)]
    [InlineData(0x0000FFF0u)]
    public void Hvar_AdvanceMapOffsetOutsideTheTable_IsAFormatError(uint mapOffset)
    {
        var hvar = Concat(U16(1), U16(0), U32(20), U32(mapOffset), U32(0), U32(0), EmptyStore);

        Should.Throw<FontFormatException>(() => HvarTable.Parse(hvar));
    }

    [Fact]
    public void ItemVariationStore_RegionListOffsetAboveIntMax_IsAFormatError()
    {
        var store = Concat(U16(1), U32(0x80000000u), U16(0));

        Should.Throw<FontFormatException>(() => ItemVariationStore.Parse(store));
    }

    [Fact]
    public void ItemVariationStore_SubtableOffsetAboveIntMax_IsAFormatError()
    {
        // One subtable whose offset no span can reach; the (empty) region list follows the header.
        var store = Concat(U16(1), U32(12), U16(1), U32(0xFFFFFFF0u), U16(0), U16(0));

        Should.Throw<FontFormatException>(() => ItemVariationStore.Parse(store));
    }

    [Theory]
    [InlineData(0xFFFF, 0x3000)] // 65535 axes x 12288 regions x 3 wraps the array size negative
    [InlineData(1000, 1000)]     // 12 MB of coordinates the table cannot hold
    public void ItemVariationStore_RegionCountsBeyondTheData_AreAFormatErrorBeforeAllocating(int axisCount, int regionCount)
    {
        var store = Concat(U16(1), U32(8), U16(0), U16((ushort)axisCount), U16((ushort)regionCount));

        long before = GC.GetAllocatedBytesForCurrentThread();
        Should.Throw<FontFormatException>(() => ItemVariationStore.Parse(store));
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1_000_000, "the region array must not be allocated");
    }

    [Fact]
    public void ItemVariationData_RowsBeyondTheData_AreAFormatErrorBeforeAllocating()
    {
        // 1000 one-axis regions, then a subtable claiming 65535 rows of 1000 byte deltas (65 MB)
        // with no delta bytes at all: the delta matrix would be 262 MB of ints.
        const int Regions = 1000;
        var regionList = Concat(U16(1), U16(Regions), Concat(Enumerable.Range(0, Regions * 3).Select(_ => F2Dot14(0)).ToArray()));
        var subtable = Concat(U16(0xFFFF), U16(0), U16(Regions), Concat(Enumerable.Range(0, Regions).Select(i => U16((ushort)i)).ToArray()));
        var store = Concat(U16(1), U32(12), U16(1), U32((uint)(12 + regionList.Length)), regionList, subtable);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Should.Throw<FontFormatException>(() => ItemVariationStore.Parse(store));
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1_000_000, "the delta matrix must not be allocated");
    }

    [Fact]
    public void Gvar_SharedTupleCountsBeyondTheData_AreAFormatError()
    {
        // 65535 axes x 65535 shared tuples wraps the array size negative; nothing follows.
        var gvar = Concat(U16(1), U16(0), U16(0xFFFF), U16(0xFFFF), U32(20), U16(0), U16(0), U32(22), U16(0));

        Should.Throw<FontFormatException>(() => GvarTable.Parse(gvar));
    }

    [Fact]
    public void Mvar_StoreOffsetPastTheTable_IsAFormatError()
    {
        // One 'hasc' value record, its store offset pointing far past the 20-byte table.
        var mvar = Concat(U16(1), U16(0), U16(0), U16(8), U16(1), U16(0xFFF0), Tag("hasc"), U16(0), U16(0));

        Should.Throw<FontFormatException>(() => MvarTable.Parse(mvar));
    }

    [Fact]
    public void Gvar_SharedTuplesOffsetAboveIntMax_IsAFormatError()
    {
        // axisCount 1, one shared tuple at an unreachable offset, zero glyphs, short offsets.
        var gvar = Concat(U16(1), U16(0), U16(1), U16(1), U32(0x80000000u), U16(0), U16(0), U32(24), U16(0));

        Should.Throw<FontFormatException>(() => GvarTable.Parse(gvar));
    }

    // ------------------------------------------------------------ glyph size limits

    [Theory]
    [InlineData(1, SdfMode.Mtsdf)] // the fuzzed hang: every glyph ~1300 em, a distance field per pixel
    [InlineData(3, SdfMode.Sdf)]
    [InlineData(16, SdfMode.None)] // spec-legal unitsPerEm, but Roboto's outlines become 80 em tall
    public void TinyUnitsPerEm_GlyphsLargerThanTheEmLimit_AreRejectedQuickly(int unitsPerEm, SdfMode mode)
    {
        var font = RobotoWithUnitsPerEm(unitsPerEm);
        var options = new RasterOptions { Size = 18, SdfMode = mode, SdfSpread = 3 };

        var error = RunWithin(TimeSpan.FromSeconds(5), () =>
        {
            using var rasterizer = Load(font);
            return Should.Throw<RasterizationException>(() => rasterizer.RasterizeGlyph('A', options));
        });

        error.Codepoint.ShouldBe('A');
        error.Message.ShouldContain("limit", Case.Sensitive);
    }

    [Fact]
    public void TinyUnitsPerEm_MetricsAgreeWithRasterizing()
    {
        using var rasterizer = Load(RobotoWithUnitsPerEm(16));
        var options = new RasterOptions { Size = 18 };

        Should.Throw<RasterizationException>(() => rasterizer.GetGlyphMetrics('A', options));
    }

    [Fact]
    public void ColrV1_TransformScalingAGlyphPastTheEmLimit_IsRejected()
    {
        // PaintTransform scales 'O' a hundredfold: 100 em of canvas the pipeline never sees.
        int o = NativeFontFace.Load(TestFonts.RobotoRegularBytes()).GetGlyphIndex('O');
        var colr = SyntheticColrV1.Colr(o, new SyntheticColrV1.Transform(100, 0, 0, 100, 0, 0,
            new SyntheticColrV1.Glyph(o, new SyntheticColrV1.Solid(0))));
        using var rasterizer = Load(SyntheticFonts.RobotoWith(("COLR", colr), ("CPAL", SyntheticColorTables.Cpal([[0xFF0000FFu]]))));
        var options = new RasterOptions { Size = 18, ColorFont = true };

        Should.Throw<RasterizationException>(() => rasterizer.RasterizeGlyph('O', options)).Codepoint.ShouldBe('O');
        Should.Throw<RasterizationException>(() => rasterizer.GetGlyphMetrics('O', options));
    }

    [Fact]
    public void ColrV1_ClipBoxPastTheEmLimit_IsRejected()
    {
        // A solid fill clipped only by the clip box: 32767 units at unitsPerEm 256 is 128 em.
        var font = RobotoWithUnitsPerEm(256);
        int o = NativeFontFace.Load(font).GetGlyphIndex('O');
        var colr = SyntheticColrV1.Colr(o, new SyntheticColrV1.Solid(0), new SyntheticColrV1.Clip(o, o, 0, 0, 32767, 32767));
        using var rasterizer = Load(SyntheticFonts.WithTables(font, new Dictionary<string, byte[]?>
        {
            ["COLR"] = colr,
            ["CPAL"] = SyntheticColorTables.Cpal([[0xFF0000FFu]]),
        }));
        var options = new RasterOptions { Size = 18, ColorFont = true };

        Should.Throw<RasterizationException>(() => rasterizer.RasterizeGlyph('O', options)).Codepoint.ShouldBe('O');
    }

    private static byte[] RobotoWithUnitsPerEm(int unitsPerEm)
    {
        var roboto = TestFonts.RobotoRegularBytes();
        var head = TableProvider.Parse(roboto).GetTable("head").ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), (ushort)unitsPerEm);
        return SyntheticFonts.WithTables(roboto, new Dictionary<string, byte[]?> { ["head"] = head });
    }

    internal static NativeRasterizer Load(byte[] font)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    /// <summary>Runs <paramref name="work"/> on its own thread and fails the test if it does not finish in time.</summary>
    internal static T RunWithin<T>(TimeSpan limit, Func<T> work)
    {
        var task = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        task.Wait(limit).ShouldBeTrue($"did not finish within {limit.TotalSeconds:0} s");
        return task.Result;
    }
}
