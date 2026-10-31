using System.Buffers.Binary;
using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Phase 179 — regressions for the core parser bugs the Native fuzz harness
/// (<c>NativeFuzzTests</c>) found by feeding damaged fonts through <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/>.
/// A damaged table directory is a <see cref="FontParsingException"/>; a damaged <c>GPOS</c>
/// subtable is skipped (kerning is optional), as the parser already did for most truncations —
/// never an <see cref="ArgumentOutOfRangeException"/>, a hang or an unbounded allocation.
/// </summary>
public class TtfParserHostileInputTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ------------------------------------------------------------ table directory

    [Theory]
    [InlineData(13)] // inside the first record
    [InlineData(44)] // records stop two and a half entries in
    public void TruncatedTableDirectory_ThrowsFontParsingException(int length)
    {
        var font = Roboto()[..length];

        Should.Throw<FontParsingException>(() => new TtfParser(font));
    }

    [Fact]
    public void CollectionFaceOffsetPastTheData_ThrowsFontParsingException()
    {
        // ttcf 1.0 with one face whose offset table lies far beyond the 16-byte file.
        byte[] ttc = [.. "ttcf"u8, 0, 1, 0, 0, 0, 0, 0, 1, 0x7F, 0xFF, 0xFF, 0xF0];

        Should.Throw<FontParsingException>(() => new TtfParser(ttc));
    }

    [Fact]
    public void CollectionOffsetArrayPastTheData_ThrowsFontParsingException()
    {
        // ttcf header claiming two faces but holding only the first offset; face 1 is requested.
        byte[] ttc = [.. "ttcf"u8, 0, 1, 0, 0, 0, 0, 0, 2, 0, 0, 0, 20];

        Should.Throw<FontParsingException>(() => new TtfParser(ttc, faceIndex: 1));
    }

    // ------------------------------------------------------------ GPOS offsets

    [Fact]
    public void Gpos_LookupListOffsetPastTheTable_IsSkipped()
    {
        var parser = new TtfParser(RobotoWithGpos(Gpos(PairPosFormat1(), lookupListOffset: 0xFFF0)));

        parser.GposPairs.ShouldBeEmpty();
    }

    [Fact]
    public void Gpos_SubtableOffsetPastTheLookup_IsSkipped()
    {
        var parser = new TtfParser(RobotoWithGpos(Gpos(PairPosFormat1(), subtableOffset: 0xFFF0)));

        parser.GposPairs.ShouldBeEmpty();
    }

    [Fact]
    public void Gpos_FeatureOffsetPastTheFeatureList_IsSkipped()
    {
        var parser = new TtfParser(RobotoWithGpos(Gpos(PairPosFormat1(), featureOffset: 0xFFF0)));

        parser.GposPairs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0xFFF0, 14)] // coverage past the subtable
    [InlineData(18, 0xFFF0)] // pair set past the subtable
    public void Gpos_PairPosFormat1OffsetsPastTheSubtable_AreSkipped(int coverageOffset, int pairSetOffset)
    {
        var parser = new TtfParser(RobotoWithGpos(Gpos(PairPosFormat1(coverageOffset, pairSetOffset))));

        parser.GposPairs.ShouldBeEmpty();
    }

    [Fact]
    public void Gpos_WellFormedSyntheticPairPos_StillReadsItsPair()
    {
        // Guards the builders above: with valid offsets the same subtable yields glyphs 5 -> 6, -40.
        var parser = new TtfParser(RobotoWithGpos(Gpos(PairPosFormat1())));

        parser.GposPairs.ShouldHaveSingleItem();
        parser.GposPairs[0].LeftCodepoint.ShouldBe(5);
        parser.GposPairs[0].RightCodepoint.ShouldBe(6);
        parser.GposPairs[0].XAdvanceAdjustment.ShouldBe(-40);
    }

    [Theory]
    [InlineData(0xFFF0, 0xFFFF)] // coverage range ending at the last glyph id
    [InlineData(0, 0xFFFF)]
    public void Gpos_CoverageRangeEndingAtGlyph65535_Terminates(int start, int end)
    {
        // A ushort loop counter wrapped from 0xFFFF to 0 and never reached the end.
        var subtable = PairPosFormat1(coverage: Concat(U16(2), U16(1), U16((ushort)start), U16((ushort)end), U16(0)));

        var parser = RunWithin(TimeSpan.FromSeconds(10), () => new TtfParser(RobotoWithGpos(Gpos(subtable))));

        parser.GposPairs.ShouldNotBeEmpty();
    }

    [Fact]
    public void Gpos_ClassDefRangeEndingAtGlyph65535_Terminates()
    {
        var subtable = PairPosFormat2(classDef2: Concat(U16(2), U16(1), U16(0xFFF0), U16(0xFFFF), U16(1)));

        var parser = RunWithin(TimeSpan.FromSeconds(10), () => new TtfParser(RobotoWithGpos(Gpos(subtable))));

        parser.GposPairs.ShouldNotBeEmpty();
    }

    [Fact]
    public void Gpos_PairPosFormat2RecordOffsetPastInt32_IsSkipped()
    {
        // 65535 x 65535 classes of 64-byte record pairs: row 600's offset passes int.MaxValue.
        // Computed in 32 bits it wrapped negative, slipped under the length check and threw.
        var coverage = Concat(U16(1), U16(1), U16(5));
        var classDef1 = Concat(U16(1), U16(5), U16(1), U16(600)); // glyph 5 -> class 600
        var classDef2 = Concat(U16(2), U16(0));
        const int CoverageAt = 16;
        int classDef1At = CoverageAt + coverage.Length;
        int classDef2At = classDef1At + classDef1.Length;
        var subtable = Concat(
            U16(2), U16(CoverageAt), U16(0xFFFF), U16(0xFFFF),
            U16((ushort)classDef1At), U16((ushort)classDef2At), U16(0xFFFF), U16(0xFFFF),
            coverage, classDef1, classDef2);

        var parser = new TtfParser(RobotoWithGpos(Gpos(subtable)));

        parser.GposPairs.ShouldBeEmpty();
    }

    [Fact]
    public void Gpos_OverlappingCoverageRanges_StayBounded()
    {
        // 1000 ranges each claiming every glyph: 65 million coverage entries if expanded,
        // although a coverage can name each of the 65536 glyph ids only once.
        const int Ranges = 1000;
        var coverage = Concat([U16(2), U16(Ranges), .. Enumerable.Repeat(Concat(U16(0), U16(0xFFFE), U16(0)), Ranges)]);
        var damaged = RobotoWithGpos(Gpos(PairPosFormat1(coverage: coverage)));
        var single = RobotoWithGpos(Gpos(PairPosFormat1(coverage: Concat(U16(2), U16(1), U16(0), U16(0xFFFE), U16(0)))));

        long baseline = AllocatedBy(() => new TtfParser(single));
        long allocated = AllocatedBy(() => new TtfParser(damaged));

        (allocated - baseline).ShouldBeLessThan(5_000_000, "repeated ranges must not expand past the 65536 glyph ids");
    }

    private static long AllocatedBy(Action work)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        work();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // ------------------------------------------------------------ cmap ranges

    [Fact]
    public void CmapFormat12_GroupStartingPastUnicode_IsSkippedQuickly()
    {
        // startCharCode 0x80000000 read as an int is negative: the loop ran 2 billion times.
        var cmap = CmapFormat12((0x80000000u, 0x41u, 36u), (0x41u, 0x41u, 36u));

        var parser = RunWithin(TimeSpan.FromSeconds(10), () => new TtfParser(RobotoWithTable("cmap", cmap), 0, [0x41]));

        parser.CmapTable[0x41].ShouldBe(36);
        parser.CmapTable.Count.ShouldBe(1);
    }

    [Fact]
    public void CmapFormat12_ManyFullRangeGroups_ParseQuicklyForARequestedSubset()
    {
        // 2000 groups each spanning all of Unicode: 2.2 billion codepoints to filter one by one.
        var groups = Enumerable.Repeat((0u, 0x10FFFFu, 1u), 2000).ToArray();
        var cmap = CmapFormat12(groups);

        var parser = RunWithin(TimeSpan.FromSeconds(10), () => new TtfParser(RobotoWithTable("cmap", cmap), 0, [0x41, 0x42]));

        parser.CmapTable[0x41].ShouldBe(0x42);
        parser.CmapTable[0x42].ShouldBe(0x43);
    }

    [Fact]
    public void CmapFormat4_OverlappingSegments_AreRejectedQuickly()
    {
        // 8000 segments each covering the whole BMP: segments may not overlap, so a valid
        // format 4 visits at most 65536 codes; this one would visit 524 million.
        var cmap = CmapFormat4(Enumerable.Repeat(((ushort)0, (ushort)0xFFFE), 8000).ToArray());

        RunWithin(TimeSpan.FromSeconds(10), () =>
            Should.Throw<FontParsingException>(() => new TtfParser(RobotoWithTable("cmap", cmap), 0, [0x41])));
    }

    [Fact]
    public void Cmap_SubtableOffsetOneByteFromTheEnd_IsInvalidNotACrash()
    {
        // The (3, 10) record points at the cmap's last byte: too short to hold a format field.
        var cmap = Concat(U16(0), U16(1), U16(3), U16(10), U32(12), [0]);

        var parser = new TtfParser(RobotoWithTable("cmap", cmap));

        parser.IsValid.ShouldBeFalse();
    }

    // ------------------------------------------------------------ fvar

    [Fact]
    public void Fvar_AxisArraySizeOverflowingInt_IsSkipped()
    {
        // 65535 axes of 65535 bytes: the instance array "starts" at a negative int offset.
        var fvar = new byte[140_000];
        Concat(U16(1), U16(0), U16(16), U16(2), U16(0xFFFF), U16(0xFFFF), U16(1), U16(4)).CopyTo(fvar, 0);

        var parser = new TtfParser(RobotoWithTable("fvar", fvar));

        parser.NamedInstances.ShouldBeEmpty();
    }

    [Fact]
    public void Fvar_InstanceWithMoreCoordinatesThanAxesRead_IsSkipped()
    {
        // One declared axis whose record runs past the 32-byte table, then an instance that fits.
        var fvar = new byte[32];
        Concat(U16(1), U16(0), U16(16), U16(2), U16(1), U16(4), U16(1), U16(8)).CopyTo(fvar, 0);

        var parser = new TtfParser(RobotoWithTable("fvar", fvar));

        parser.VariationAxes.ShouldBeEmpty();
        parser.NamedInstances.ShouldBeEmpty();
    }

    /// <summary>A cmap with one (3, 10) format 12 subtable.</summary>
    private static byte[] CmapFormat12(params (uint Start, uint End, uint Glyph)[] groups)
    {
        var body = Concat([.. groups.Select(g => Concat(U32(g.Start), U32(g.End), U32(g.Glyph)))]);
        return Concat(U16(0), U16(1), U16(3), U16(10), U32(12),
            U16(12), U16(0), U32((uint)(16 + body.Length)), U32(0), U32((uint)groups.Length), body);
    }

    /// <summary>A cmap with one (3, 1) format 4 subtable, idDelta 1, plus the closing 0xFFFF segment.</summary>
    private static byte[] CmapFormat4(params (ushort Start, ushort End)[] segments)
    {
        var all = segments.Append((Start: (ushort)0xFFFF, End: (ushort)0xFFFF)).ToArray();
        int segX2 = all.Length * 2;
        var subtable = Concat(
            U16(4), U16(16 + (segX2 * 4)), U16(0), U16(segX2), U16(0), U16(0), U16(0),
            Concat([.. all.Select(s => U16(s.End))]), U16(0),
            Concat([.. all.Select(s => U16(s.Start))]),
            Concat([.. all.Select(_ => U16(1))]),
            Concat([.. all.Select(_ => U16(0))]));
        return Concat(U16(0), U16(1), U16(3), U16(1), U32(12), subtable);
    }

    private static byte[] U32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }

    // ------------------------------------------------------------ builders

    /// <summary>
    /// A GPOS with one 'kern' feature pointing at one lookup of <paramref name="lookupType"/>
    /// with one subtable. Offsets can be overridden to point anywhere.
    /// </summary>
    private static byte[] Gpos(byte[] subtable, int lookupType = 2, int lookupListOffset = 24, int subtableOffset = 8, int featureOffset = 8) =>
        Concat(
            U16(1), U16(0), U16(0), U16(10), U16((ushort)lookupListOffset), // header
            U16(1), Tag("kern"), U16((ushort)featureOffset),                // FeatureList @10
            U16(0), U16(1), U16(0),                                         // Feature @18
            U16(1), U16(4),                                                 // LookupList @24
            U16((ushort)lookupType), U16(0), U16(1), U16((ushort)subtableOffset), // Lookup @28
            subtable);                                                      // subtable @36

    /// <summary>PairPos format 1: first glyph 5 (from coverage), one pair 5 -> 6 with XAdvance -40.</summary>
    private static byte[] PairPosFormat1(int coverageOffset = 18, int pairSetOffset = 12, byte[]? coverage = null)
    {
        coverage ??= Concat(U16(1), U16(1), U16(5));
        return Concat(
            U16(1), U16((ushort)coverageOffset), U16(4), U16(0), U16(1), U16((ushort)pairSetOffset), // @0..12
            U16(1), U16(6), I16(-40),                                                               // PairSet @12
            coverage);                                                                              // Coverage @18
    }

    /// <summary>PairPos format 2: glyph 5 in class 0 of classDef1, everything in classDef2 class 1 gets -40.</summary>
    private static byte[] PairPosFormat2(byte[] classDef2)
    {
        // Header 16 bytes, then class1Count(1) x class2Count(2) records of one int16 (XAdvance).
        var records = Concat(I16(0), I16(-40));
        var coverage = Concat(U16(1), U16(1), U16(5));
        var classDef1 = Concat(U16(2), U16(0));
        int coverageAt = 16 + records.Length;
        int classDef1At = coverageAt + coverage.Length;
        int classDef2At = classDef1At + classDef1.Length;
        return Concat(
            U16(2), U16((ushort)coverageAt), U16(4), U16(0),
            U16((ushort)classDef1At), U16((ushort)classDef2At), U16(1), U16(2),
            records, coverage, classDef1, classDef2);
    }

    /// <summary>Roboto with its GPOS replaced, rebuilt as a fresh sfnt.</summary>
    private static byte[] RobotoWithGpos(byte[] gpos) => RobotoWithTable("GPOS", gpos);

    /// <summary>Roboto with one table replaced (or added), rebuilt as a fresh sfnt.</summary>
    private static byte[] RobotoWithTable(string replacedTag, byte[] replacement)
    {
        var font = Roboto();
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var tables = new List<(string Tag, byte[] Data)>();
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            string tag = System.Text.Encoding.ASCII.GetString(font, record, 4);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            tables.Add((tag, tag == replacedTag ? replacement : font[offset..(offset + length)]));
        }

        if (!tables.Any(t => t.Tag == replacedTag))
            tables.Add((replacedTag, replacement));

        int dataOffset = 12 + (tables.Count * 16);
        var result = new byte[dataOffset + tables.Sum(t => (t.Data.Length + 3) & ~3)];
        BinaryPrimitives.WriteUInt32BigEndian(result, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), (ushort)tables.Count);
        for (int i = 0; i < tables.Count; i++)
        {
            int record = 12 + (i * 16);
            System.Text.Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(result, record);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(record + 8), (uint)dataOffset);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(record + 12), (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(result, dataOffset);
            dataOffset += (tables[i].Data.Length + 3) & ~3;
        }

        return result;
    }

    private static T RunWithin<T>(TimeSpan limit, Func<T> work)
    {
        var task = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        task.Wait(limit).ShouldBeTrue($"did not finish within {limit.TotalSeconds:0} s");
        return task.Result;
    }

    private static byte[] U16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        return buffer;
    }

    private static byte[] I16(short value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] Tag(string tag) => System.Text.Encoding.ASCII.GetBytes(tag);

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
