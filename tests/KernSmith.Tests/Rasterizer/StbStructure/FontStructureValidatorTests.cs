using KernSmith.Rasterizers.StbTrueType;
using Shouldly;
using static KernSmith.Tests.Rasterizer.StbStructure.SyntheticFonts;

namespace KernSmith.Tests.Rasterizer.StbStructure;

/// <summary>
/// The StbTrueType backend's OpenType structure check, rule by rule: each test takes a font
/// that passes, breaks one structural field so it violates one rule, and asserts the
/// validator rejects it with a <see cref="FontParsingException"/> naming the table and the rule.
/// These call the validator only, never stb_truetype.
/// </summary>
public class FontStructureValidatorTests
{
    private static void Rejects(byte[] font, string table, string rule, int faceIndex = 0)
    {
        var ex = Should.Throw<FontParsingException>(() => FontStructureValidator.Validate(font, faceIndex));
        ex.TableTag.ShouldBe(table);
        ex.Message.ShouldContain(rule, Case.Sensitive);
    }

    private static void Rejects(SfntEditor font, string table, string rule, int faceIndex = 0) =>
        Rejects(font.Bytes, table, rule, faceIndex);

    private static void RejectsGlyph(byte[] font, int glyph, string table, string rule)
    {
        var validator = FontStructureValidator.Validate(font, 0);
        var ex = Should.Throw<FontParsingException>(() => validator.EnsureGlyph(glyph));
        ex.TableTag.ShouldBe(table);
        ex.Message.ShouldContain(rule, Case.Sensitive);
    }

    private static void AcceptsGlyph(byte[] font, int glyph) =>
        Should.NotThrow(() => FontStructureValidator.Validate(font, 0).EnsureGlyph(glyph));

    // ------------------------------------------------------------ passing controls

    [Fact]
    public void Roboto_Passes()
    {
        var v = FontStructureValidator.Validate(SfntEditor.Roboto().Bytes, 0);
        v.NumGlyphs.ShouldBe(3359);
        v.IsCff.ShouldBeFalse();
        v.FaceOffset.ShouldBe(0);
    }

    [Fact]
    public void SourceSerif_PassesAsCff()
    {
        var v = FontStructureValidator.Validate(SfntEditor.SourceSerif().Bytes, 0);
        v.IsCff.ShouldBeTrue();
        v.NumGlyphs.ShouldBe(1464);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntheticCff_Passes(bool cid)
    {
        var font = Cff([Square], localSubrs: ["return"], cid: cid);
        var v = FontStructureValidator.Validate(font, 0);
        v.IsCff.ShouldBeTrue();
        v.NumGlyphs.ShouldBe(2);
        Should.NotThrow(() => v.EnsureGlyph(1));
    }

    // ------------------------------------------------------------ file header and directory

    [Fact]
    public void FileShorterThanHeader_IsRejected() =>
        Rejects(SfntEditor.Roboto().Bytes[..10], "sfnt", "file is shorter than");

    [Fact]
    public void UnknownSfntVersion_IsRejected()
    {
        var font = SfntEditor.Roboto();
        "abcd"u8.CopyTo(font.Bytes);
        Rejects(font, "sfnt", "unknown sfnt version");
    }

    [Fact]
    public void TableDirectoryPastTheFile_IsRejected()
    {
        var font = SfntEditor.Roboto();
        font.Bytes[4] = 0xFF;
        font.Bytes[5] = 0xFF;
        Rejects(font, "sfnt", "table directory runs past the end of the file");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FaceIndexOtherThanZeroOnASingleFont_IsRejected(int faceIndex) =>
        Rejects(SfntEditor.Roboto(), "sfnt", "face index", faceIndex);

    [Fact]
    public void Collection_PassesWithFaceOffset()
    {
        var ttc = SfntEditor.AsCollection(SfntEditor.Roboto().Bytes);
        FontStructureValidator.Validate(ttc, 0).FaceOffset.ShouldBe(16);
    }

    [Fact]
    public void CollectionFaceIndexOutOfRange_IsRejected() =>
        Rejects(SfntEditor.AsCollection(SfntEditor.Roboto().Bytes), "ttcf", "face index 1 is out of range", faceIndex: 1);

    [Fact]
    public void CollectionNegativeFaceIndex_IsRejected() =>
        Rejects(SfntEditor.AsCollection(SfntEditor.Roboto().Bytes), "sfnt", "face index", faceIndex: -1);

    [Fact]
    public void CollectionOffsetArrayPastTheFile_IsRejected()
    {
        var ttc = SfntEditor.AsCollection(SfntEditor.Roboto().Bytes);
        Be.U32(0x10000000).CopyTo(ttc, 8);
        Rejects(ttc, "ttcf", "offset array runs past the end of the file");
    }

    [Fact]
    public void CollectionFaceOffsetPastTheFile_IsRejected()
    {
        var ttc = SfntEditor.AsCollection(SfntEditor.Roboto().Bytes);
        Be.U32(ttc.Length - 4).CopyTo(ttc, 12);
        Rejects(ttc, "ttcf", "starts past the end of the file");
    }

    [Fact]
    public void CollectionUnknownVersion_IsRejected()
    {
        var ttc = SfntEditor.AsCollection(SfntEditor.Roboto().Bytes);
        Be.U32(0x00030000).CopyTo(ttc, 4);
        Rejects(ttc, "ttcf", "unsupported TTC version");
    }

    [Theory]
    [InlineData("glyf")]
    [InlineData("cmap")]
    [InlineData("hmtx")]
    public void TablePastTheFile_IsRejected(string tag)
    {
        var font = SfntEditor.Roboto();
        font.SetLength(tag, font.Bytes.Length);
        Rejects(font, tag, "runs past the end of the file");
    }

    [Fact]
    public void TableAtOffsetZero_IsRejected()
    {
        var font = SfntEditor.Roboto();
        font.SetOffset("cmap", 0);
        Rejects(font, "cmap", "overlaps the file header");
    }

    [Theory]
    [InlineData("cmap")]
    [InlineData("head")]
    [InlineData("hhea")]
    [InlineData("hmtx")]
    [InlineData("maxp")]
    [InlineData("loca")]
    public void MissingTrueTypeTable_IsRejected(string tag) =>
        Rejects(SfntEditor.Roboto().Rename(tag, "zzzz"), tag, "required table is missing");

    [Fact]
    public void MissingCffTable_IsRejected() =>
        Rejects(SfntEditor.SourceSerif().Rename("CFF ", "zzzz"), "CFF ", "required table is missing");

    // ------------------------------------------------------------ head, hhea, maxp, hmtx

    [Fact]
    public void ShortHead_IsRejected() => Rejects(SfntEditor.Roboto().SetLength("head", 53), "head", "shorter than 54 bytes");

    [Theory]
    [InlineData(15)]
    [InlineData(16385)]
    [InlineData(0)]
    public void UnitsPerEmOutsideTheSpecRange_IsRejected(int unitsPerEm) =>
        Rejects(SfntEditor.Roboto().SetU16("head", 18, unitsPerEm), "head", "unitsPerEm");

    [Theory]
    [InlineData(16)]
    [InlineData(16384)]
    public void UnitsPerEmAtTheSpecLimits_Passes(int unitsPerEm) =>
        Should.NotThrow(() => FontStructureValidator.Validate(SfntEditor.Roboto().SetU16("head", 18, unitsPerEm).Bytes, 0));

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void UnknownIndexToLocFormat_IsRejected(int format) =>
        Rejects(SfntEditor.Roboto().SetU16("head", 50, format), "head", "indexToLocFormat");

    [Fact]
    public void ShortHhea_IsRejected() => Rejects(SfntEditor.Roboto().SetLength("hhea", 35), "hhea", "shorter than 36 bytes");

    [Fact]
    public void ZeroNumberOfHMetrics_IsRejected() =>
        Rejects(SfntEditor.Roboto().SetU16("hhea", 34, 0), "hhea", "numberOfHMetrics");

    [Fact]
    public void NumberOfHMetricsAboveNumGlyphs_IsRejected() =>
        Rejects(SfntEditor.Roboto().SetU16("hhea", 34, 3360), "hhea", "numberOfHMetrics");

    [Fact]
    public void ShortMaxp_IsRejected() => Rejects(SfntEditor.Roboto().SetLength("maxp", 5), "maxp", "shorter than 6 bytes");

    [Fact]
    public void ZeroNumGlyphs_IsRejected() => Rejects(SfntEditor.Roboto().SetU16("maxp", 4, 0), "maxp", "numGlyphs is 0");

    [Fact]
    public void HmtxShorterThanItsMetrics_IsRejected()
    {
        var font = SfntEditor.Roboto();
        int need = (4 * 3358) + 2;
        font.SetLength("hmtx", need - 1);
        Rejects(font, "hmtx", "need");
    }

    // ------------------------------------------------------------ loca

    [Fact]
    public void LocaShorterThanItsOffsets_IsRejected()
    {
        var font = SfntEditor.Roboto();
        font.SetLength("loca", (4 * 3360) - 1);
        Rejects(font, "loca", "offsets need");
    }

    [Fact]
    public void LocaOffsetsDecreasing_IsRejected()
    {
        var font = SfntEditor.Roboto();
        font.SetLocaEntry(1, font.LocaEntry(2) + 4);
        Rejects(font, "loca", "offsets decrease");
    }

    [Fact]
    public void LocaPastTheGlyfTable_IsRejected()
    {
        var font = SfntEditor.Roboto();
        font.SetLength("glyf", font.LocaEntry(3359) - 4);
        Rejects(font, "loca", "past the end of glyf");
    }

    // ------------------------------------------------------------ cmap

    private static SfntEditor RobotoWithCmap(byte[] cmap) => SfntEditor.Roboto().ReplaceTable("cmap", cmap);

    [Fact]
    public void ShortCmap_IsRejected() => Rejects(SfntEditor.Roboto().SetLength("cmap", 3), "cmap", "shorter than its 4-byte header");

    [Fact]
    public void CmapEncodingRecordsPastTheTable_IsRejected() =>
        Rejects(SfntEditor.Roboto().SetU16("cmap", 2, 0x1000), "cmap", "encoding records run past the table");

    [Fact]
    public void CmapSubtableOffsetOutsideTheTable_IsRejected()
    {
        var font = SfntEditor.Roboto();
        font.SetU32("cmap", 4 + 4, font.LengthOf("cmap")); // first record (0,3)
        Rejects(font, "cmap", "subtable outside the table");
    }

    [Fact]
    public void CmapWithoutAUnicodeSubtable_IsRejected()
    {
        var font = SfntEditor.Roboto();
        for (int i = 0; i < 4; i++)
            font.SetU16("cmap", 4 + (8 * i), 1).SetU16("cmap", 4 + (8 * i) + 2, 0);
        Rejects(font, "cmap", "no Unicode or symbol subtable");
    }

    private static byte[] SymbolFormat4() => Format4([(0xF020, 0xF07E, 1 - 0xF020)]);

    [Fact]
    public void SymbolOnlyCmap_PassesAndIsRelabelledForStb()
    {
        // Wingdings-style (3,0) subtable: accepted, and its record becomes (3,1) in the copy
        // stb_truetype reads, since stb only reads Unicode subtables.
        var bytes = RobotoWithCmap(CmapWith(3, 0, SymbolFormat4())).Bytes;
        var cmapOffset = CmapOffset(bytes);

        var validator = FontStructureValidator.Validate(bytes, 0);

        validator.IsSymbolCmap.ShouldBeTrue();
        (bytes[cmapOffset + 6], bytes[cmapOffset + 7]).ShouldBe(((byte)0, (byte)1));
    }

    [Fact]
    public void SymbolCmap_IsValidatedLikeAUnicodeOne() =>
        Rejects(RobotoWithCmap(CmapWith(3, 0, SymbolFormat4())).SetU16("cmap", Sub + 6, 3), "cmap", "segCountX2");

    [Fact]
    public void UnicodeCmap_WinsOverASymbolOne()
    {
        // Roboto's own cmap has Unicode subtables; adding nothing symbol-like leaves it alone.
        var validator = FontStructureValidator.Validate(SfntEditor.Roboto().Bytes, 0);
        validator.IsSymbolCmap.ShouldBeFalse();
    }

    private static int CmapOffset(byte[] font)
    {
        int numTables = (font[4] << 8) | font[5];
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (16 * i);
            if (font[rec] == 'c' && font[rec + 1] == 'm' && font[rec + 2] == 'a' && font[rec + 3] == 'p')
                return (font[rec + 8] << 24) | (font[rec + 9] << 16) | (font[rec + 10] << 8) | font[rec + 11];
        }

        throw new InvalidOperationException("no cmap");
    }

    private static byte[] AsciiFormat4() => Format4([(0x20, 0x7E, 1 - 0x20)]);

    [Fact]
    public void Format4_Passes() =>
        Should.NotThrow(() => FontStructureValidator.Validate(RobotoWithCmap(CmapWith(3, 1, AsciiFormat4())).Bytes, 0));

    // Format-4 subtable field positions, relative to the cmap table (header + one record = 12).
    private const int Sub = 12;

    [Fact]
    public void Format4OddSegCountX2_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, AsciiFormat4())).SetU16("cmap", Sub + 6, 3), "cmap", "segCountX2");

    [Theory]
    [InlineData(8, 2)]  // searchRange
    [InlineData(10, 2)] // entrySelector
    [InlineData(12, 2)] // rangeShift
    public void Format4SearchFieldsDisagreeWithSegCount_IsRejected(int field, int value) =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, AsciiFormat4())).SetU16("cmap", Sub + field, value), "cmap", "searchRange");

    [Fact]
    public void Format4LengthPastTheCmapTable_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, AsciiFormat4())).SetU16("cmap", Sub + 2, 0x1000), "cmap", "length runs past the cmap table");

    [Fact]
    public void Format4SegmentArraysPastItsLength_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, AsciiFormat4())).SetU16("cmap", Sub + 2, 20), "cmap", "segment arrays run past");

    [Fact]
    public void Format4StartAfterEnd_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, Format4([(0x50, 0x40, 1)]))), "cmap", "startCode");

    [Fact]
    public void Format4SegmentsOutOfOrder_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, Format4([(0x50, 0x60, 1), (0x40, 0x45, 1)]))), "cmap", "out of order");

    [Fact]
    public void Format4WithoutTheClosingSegment_IsRejected()
    {
        // Segment arrays: endCode at 14, startCode at 14 + segX2 + 2 (segCount 2 → 20).
        var font = RobotoWithCmap(CmapWith(3, 1, AsciiFormat4()));
        font.SetU16("cmap", Sub + 14 + 2, 0xFFFE).SetU16("cmap", Sub + 20 + 2, 0xFFFE);
        Rejects(font, "cmap", "0xFFFF");
    }

    [Fact]
    public void Format4IdRangeOffsetPastTheSubtable_IsRejected()
    {
        // idRangeOffset[0] sits at 14 + 3 * segX2 + 2 = 28 for two segments.
        var font = RobotoWithCmap(CmapWith(3, 1, AsciiFormat4()));
        font.SetU16("cmap", Sub + 28, 4);
        Rejects(font, "cmap", "idRangeOffset");
    }

    [Fact]
    public void Format4IdRangeOffsetInsideTheSubtable_Passes()
    {
        // Segment 0 maps one codepoint (0x41) through glyphIdArray; the array follows the subtable.
        byte[] sub = Format4([(0x41, 0x41, 0)]);
        sub = [.. sub, .. Be.U16(7)];
        Be.U16(sub.Length).CopyTo(sub, 2);
        var font = RobotoWithCmap(CmapWith(3, 1, sub));
        font.SetU16("cmap", Sub + 28, 4); // idRangeOffset[0] → 4 bytes on = the appended entry
        Should.NotThrow(() => FontStructureValidator.Validate(font.Bytes, 0));
    }

    [Fact]
    public void Format12_Passes() =>
        Should.NotThrow(() => FontStructureValidator.Validate(RobotoWithCmap(CmapWith(3, 10, Format12([(0x41, 0x5A, 1)]))).Bytes, 0));

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    public void Format12GroupsPastItsLength_IsRejected(int format) =>
        Rejects(RobotoWithCmap(CmapWith(3, 10, Format12([(0x41, 0x5A, 1)], format))).SetU32("cmap", Sub + 12, 2), "cmap", "groups run past");

    [Fact]
    public void Format12LengthPastTheCmapTable_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 10, Format12([(0x41, 0x5A, 1)]))).SetU32("cmap", Sub + 4, 0x10000), "cmap", "length runs past the cmap table");

    [Fact]
    public void Format12GroupsOutOfOrder_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 10, Format12([(0x61, 0x7A, 1), (0x41, 0x5A, 1)]))), "cmap", "out of order");

    [Fact]
    public void Format12StartAfterEnd_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 10, Format12([(0x5A, 0x41, 1)]))), "cmap", "startCharCode");

    [Fact]
    public void Format0_Passes() =>
        Should.NotThrow(() => FontStructureValidator.Validate(RobotoWithCmap(CmapWith(3, 1, Format0((0x41, 5)))).Bytes, 0));

    [Fact]
    public void Format0LengthPastTheCmapTable_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, Format0((0x41, 5)))).SetU16("cmap", Sub + 2, 300), "cmap", "length runs past the cmap table");

    [Fact]
    public void Format6_Passes() =>
        Should.NotThrow(() => FontStructureValidator.Validate(RobotoWithCmap(CmapWith(3, 1, Format6(0x41, 5, 6))).Bytes, 0));

    [Fact]
    public void Format6EntriesPastItsLength_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, Format6(0x41, 5, 6))).SetU16("cmap", Sub + 8, 3), "cmap", "entryCount");

    [Fact]
    public void Format6LengthPastTheCmapTable_IsRejected() =>
        Rejects(RobotoWithCmap(CmapWith(3, 1, Format6(0x41, 5, 6))).SetU16("cmap", Sub + 2, 100), "cmap", "length runs past the cmap table");

    [Fact]
    public void MappedGlyphPastNumGlyphs_IsUnmappedNotRejected()
    {
        var v = FontStructureValidator.Validate(RobotoWithCmap(CmapWith(3, 1, Format6(0x41, 60000))).Bytes, 0);

        v.ResolveGlyph(60000).ShouldBe(0);
        v.ResolveGlyph(3359).ShouldBe(0);
        v.ResolveGlyph(-5).ShouldBe(0);
        v.ResolveGlyph(3358).ShouldBe(3358);
        v.ResolveGlyph(36).ShouldBe(36);
    }

    // ------------------------------------------------------------ glyf: simple glyphs

    private static byte[] Glyph(int contours, int xMin, int yMin, int xMax, int yMax, params byte[][] body) =>
        Be.Cat([Be.U16(contours), Be.U16(xMin), Be.U16(yMin), Be.U16(xMax), Be.U16(yMax), .. body]);

    /// <summary>A 100-unit square: one contour, four on-curve points, long coordinates.</summary>
    private static byte[] SquareGlyph(int instructionLength = 0, int xMin = 0, int xMax = 100) =>
        Glyph(1, xMin, 0, xMax, 100,
            Be.U16(3), Be.U16(instructionLength), [1, 1, 1, 1],
            Be.U16(0), Be.U16(100), Be.U16(0), Be.U16(-100),
            Be.U16(0), Be.U16(0), Be.U16(100), Be.U16(0));

    private static (SfntEditor Font, int Glyph) RobotoWithGlyph(byte[] record)
    {
        var font = SfntEditor.Roboto();
        int glyph = font.GlyphsWithRecordAtLeast(record.Length).Last();
        font.WriteGlyph(glyph, record);
        return (font, glyph);
    }

    private static void RejectsSimple(byte[] record, string rule)
    {
        var (font, glyph) = RobotoWithGlyph(record);
        RejectsGlyph(font.Bytes, glyph, "glyf", rule);
    }

    [Fact]
    public void SimpleGlyph_Passes()
    {
        var (font, glyph) = RobotoWithGlyph(SquareGlyph());
        AcceptsGlyph(font.Bytes, glyph);
    }

    [Fact]
    public void EveryRobotoGlyph_Passes()
    {
        var v = FontStructureValidator.Validate(SfntEditor.Roboto().Bytes, 0);
        for (int g = 0; g < v.NumGlyphs; g++)
            v.EnsureGlyph(g);
    }

    [Fact]
    public void GlyphRecordShorterThanItsHeader_IsRejected()
    {
        var font = SfntEditor.Roboto();
        int glyph = font.GlyphsWithRecordAtLeast(40).Last();
        font.SetLocaEntry(glyph + 1, font.LocaEntry(glyph) + 6);
        RejectsGlyph(font.Bytes, glyph, "glyf", "shorter than the 10-byte glyph header");
    }

    [Fact]
    public void EndPointsNotIncreasing_IsRejected() =>
        RejectsSimple(Glyph(2, 0, 0, 100, 100, Be.U16(3), Be.U16(3), Be.U16(0), [1, 1, 1, 1], new byte[16]), "endPtsOfContours");

    [Fact]
    public void InstructionsPastTheRecord_IsRejected() => RejectsSimple(SquareGlyph(instructionLength: 0x4000), "instructions run past");

    [Fact]
    public void FlagRepeatPastTheLastPoint_IsRejected() =>
        RejectsSimple(Glyph(1, 0, 0, 100, 100, Be.U16(3), Be.U16(0), [1 | 8, 10], new byte[16]), "repeat count runs past the last point");

    [Fact]
    public void FlagsPastTheRecord_IsRejected()
    {
        var font = SfntEditor.Roboto();
        int glyph = font.GlyphsWithRecordAtLeast(40).Last();
        font.WriteGlyph(glyph, SquareGlyph());
        font.SetLocaEntry(glyph + 1, font.LocaEntry(glyph) + 16); // header + endPts + insLen + 2 flags
        RejectsGlyph(font.Bytes, glyph, "glyf", "flags run past");
    }

    [Fact]
    public void CoordinatesPastTheRecord_IsRejected()
    {
        var font = SfntEditor.Roboto();
        int glyph = font.GlyphsWithRecordAtLeast(40).Last();
        font.WriteGlyph(glyph, SquareGlyph());
        font.SetLocaEntry(glyph + 1, font.LocaEntry(glyph) + 30);
        RejectsGlyph(font.Bytes, glyph, "glyf", "coordinates run past");
    }

    [Fact]
    public void InvertedBoundingBox_IsRejected() => RejectsSimple(SquareGlyph(xMin: 100, xMax: 0), "bounding box");

    [Fact]
    public void LastContourOfOneOffCurvePoint_IsMadeOnCurveInTheCheckedCopy()
    {
        // The one-point "space" glyph several Windows fonts use; stb_truetype would read the
        // point after it, past its vertex buffer. Flags start at 10 + 2 + 2.
        var (font, glyph) = RobotoWithGlyph(Glyph(1, 0, 0, 0, 0, Be.U16(0), Be.U16(0), [0], Be.U16(0), Be.U16(0)));
        int flag = font.OffsetOf("glyf") + font.GlyphRecord(glyph).Start + 14;

        AcceptsGlyph(font.Bytes, glyph);

        font.Bytes[flag].ShouldBe((byte)0x01);
    }

    [Fact]
    public void LastContourOfOneOffCurvePointSharingARepeatedFlag_IsRejected() =>
        RejectsSimple(Glyph(2, 0, 0, 0, 0, Be.U16(0), Be.U16(1), Be.U16(0), [0x08, 1], new byte[8]), "repeat run shares");

    [Fact]
    public void LastContourOfOneOnCurvePoint_Passes()
    {
        var (font, glyph) = RobotoWithGlyph(Glyph(1, 0, 0, 0, 0, Be.U16(0), Be.U16(0), [1], Be.U16(0), Be.U16(0)));
        AcceptsGlyph(font.Bytes, glyph);
    }

    // ------------------------------------------------------------ glyf: composite glyphs

    private const int ArgsAreXy = 0x02;
    private const int MoreComponents = 0x20;

    /// <summary>A composite whose components all use byte XY offsets (0, 0).</summary>
    private static byte[] CompositeGlyph(params int[] components) =>
        Glyph(-1, 0, 0, 100, 100, components.SelectMany((c, i) => Be.Cat(
            Be.U16(ArgsAreXy | (i < components.Length - 1 ? MoreComponents : 0)), Be.U16(c), [0, 0])).ToArray());

    /// <summary>Writes each record into its own Roboto glyph (largest records first) and returns their ids.</summary>
    private static (SfntEditor Font, int[] Glyphs) RobotoWithGlyphs(int count, Func<int[], int, byte[]> record, int minLength)
    {
        var font = SfntEditor.Roboto();
        int[] ids = font.GlyphsWithRecordAtLeast(minLength).Take(count).ToArray();
        if (ids.Length < count)
            throw new InvalidOperationException($"Roboto has fewer than {count} glyphs of {minLength}+ bytes.");
        for (int i = 0; i < count; i++)
            font.WriteGlyph(ids[i], record(ids, i));
        return (font, ids);
    }

    [Fact]
    public void Composite_Passes()
    {
        var (font, g) = RobotoWithGlyphs(3, (ids, i) => i == 0 ? CompositeGlyph(ids[1], ids[2]) : SquareGlyph(), 40);
        AcceptsGlyph(font.Bytes, g[0]);
    }

    [Fact]
    public void ComponentPastNumGlyphs_IsRejected() =>
        RejectsSimple(CompositeGlyph(3359), "component glyph index");

    [Fact]
    public void CompositeContainingItself_IsRejected()
    {
        var (font, g) = RobotoWithGlyphs(1, (ids, _) => CompositeGlyph(ids[0]), 20);
        RejectsGlyph(font.Bytes, g[0], "glyf", "contains itself");
    }

    [Fact]
    public void CompositeCycleThroughAnotherGlyph_IsRejected()
    {
        var (font, g) = RobotoWithGlyphs(2, (ids, i) => CompositeGlyph(ids[1 - i]), 20);
        RejectsGlyph(font.Bytes, g[0], "glyf", "contains itself");
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void CompositeNesting_IsLimitedTo64Levels(int composites, bool passes)
    {
        // composites[0] → [1] → ... → [n-1] → a simple glyph, which sits n levels deep.
        var (font, g) = RobotoWithGlyphs(composites + 1,
            (ids, i) => i < composites ? CompositeGlyph(ids[i + 1]) : SquareGlyph(), 40);
        if (passes)
            AcceptsGlyph(font.Bytes, g[0]);
        else
            RejectsGlyph(font.Bytes, g[0], "glyf", "nest deeper than 64");
    }

    [Fact]
    public void PointMatchedComponent_IsRejected() =>
        RejectsSimple(Glyph(-1, 0, 0, 100, 100, Be.U16(0), Be.U16(5), [0, 0]), "point matching");

    [Fact]
    public void ComponentWithTwoTransforms_IsRejected() =>
        RejectsSimple(Glyph(-1, 0, 0, 100, 100, Be.U16(ArgsAreXy | 0x08 | 0x40), Be.U16(5), [0, 0], new byte[6]), "more than one transform");

    [Fact]
    public void ComponentRecordPastTheGlyphRecord_IsRejected()
    {
        var font = SfntEditor.Roboto();
        int glyph = font.GlyphsWithRecordAtLeast(40).Last();
        font.WriteGlyph(glyph, Glyph(-1, 0, 0, 100, 100, Be.U16(ArgsAreXy | 0x80), Be.U16(5), [0, 0], new byte[8]));
        font.SetLocaEntry(glyph + 1, font.LocaEntry(glyph) + 20);
        RejectsGlyph(font.Bytes, glyph, "glyf", "component record runs past");
    }

    [Fact]
    public void ExponentialComponentTree_IsBoundedByTheComponentBudget()
    {
        // Thirteen levels of two components each resolve 2 + 4 + ... + 2^13 > 4096 components.
        const int levels = 13;
        var (font, g) = RobotoWithGlyphs(levels + 1,
            (ids, i) => i < levels ? CompositeGlyph(ids[i + 1], ids[i + 1]) : SquareGlyph(), 40);
        RejectsGlyph(font.Bytes, g[0], "glyf", "components in total");
    }

    [Fact]
    public void CompositeOverThePointBudget_IsRejected()
    {
        // A 2048-point simple glyph (flag runs, no coordinate bytes) used 33 times: 67,584 points.
        byte[] flags = Enumerable.Range(0, 8).SelectMany(_ => new byte[] { 0x39, 255 }).ToArray();
        byte[] big = Glyph(1, 0, 0, 0, 0, Be.U16(2047), Be.U16(0), flags);
        var (font, g) = RobotoWithGlyphs(2,
            (ids, i) => i == 0 ? CompositeGlyph(Enumerable.Repeat(ids[1], 33).ToArray()) : big, 10 + (33 * 6));
        AcceptsGlyph(font.Bytes, g[1]);
        RejectsGlyph(font.Bytes, g[0], "glyf", "points in total");
    }

    // ------------------------------------------------------------ CFF: font level

    private static byte[] SyntheticCff(out CffLayout layout, bool cid = false) =>
        Cff(out layout, [Square, Square], globalSubrs: ["return"], localSubrs: ["return"], cid: cid);

    private static void Poke(byte[] font, int pos, params byte[] bytes) => bytes.CopyTo(font, pos);

    [Fact]
    public void CffHeaderSizeTooSmall_IsRejected()
    {
        var font = SyntheticCff(out var l);
        font[l.Cff + 2] = 2;
        Rejects(font, "CFF ", "header");
    }

    [Fact]
    public void CffMajorVersionNotOne_IsRejected()
    {
        var font = SyntheticCff(out var l);
        font[l.Cff] = 2;
        Rejects(font, "CFF ", "major version");
    }

    [Fact]
    public void CffIndexOffSizeOutOfRange_IsRejected()
    {
        var font = SyntheticCff(out var l);
        font[l.NameIndex + 2] = 5;
        Rejects(font, "CFF ", "offSize");
    }

    [Fact]
    public void CffIndexFirstOffsetNotOne_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.TopDictIndex + 3, Be.U32(2));
        Rejects(font, "CFF ", "first offset");
    }

    [Fact]
    public void CffIndexOffsetsNotAscending_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.CharStringsIndex + 3 + 4, Be.U32(0x100)); // offset[1] above offset[2]
        Rejects(font, "CFF ", "not ascending");
    }

    [Fact]
    public void CffIndexDataPastTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.CharStringsIndex + 3 + 12, Be.U32(0x100000)); // last of four offsets
        Rejects(font, "CFF ", "runs past the table");
    }

    [Fact]
    public void CffGlobalSubrIndexPastTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.GlobalSubrIndex + 3 + 4, Be.U32(0x100000));
        Rejects(font, "CFF ", "runs past the table");
    }

    [Fact]
    public void CffTableShorterThanItsHeader_IsRejected()
    {
        var font = new SfntEditor(SyntheticCff(out _)).SetLength("CFF ", 3);
        Rejects(font, "CFF ", "header");
    }

    [Fact]
    public void CharStringsCountDifferentFromNumGlyphs_IsRejected() =>
        Rejects(Cff([Square], maxpNumGlyphs: 3), "CFF ", "CharStrings count");

    [Fact]
    public void TopDictWithoutCharStrings_IsRejected()
    {
        var font = SyntheticCff(out var l);
        font[l.TopDict + 5] = 16; // CharStrings (17) becomes Encoding (16)
        Rejects(font, "CFF ", "no CharStrings");
    }

    [Fact]
    public void PrivateDictOutsideTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.TopDict + 12, Be.U32(0x7FFF0000)); // Private offset operand
        Rejects(font, "CFF ", "Private DICT");
    }

    [Fact]
    public void PrivateDictSizePastTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.TopDict + 7, Be.U32(0x10000)); // Private size operand
        Rejects(font, "CFF ", "Private DICT");
    }

    [Fact]
    public void LocalSubrsOutsideTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l);
        Poke(font, l.Private + 1, Be.U32(0x7FFF0000));
        Rejects(font, "CFF ", "Subrs");
    }

    [Fact]
    public void LocalSubrIndexMalformed_IsRejected()
    {
        var font = SyntheticCff(out var l);
        font[l.LocalSubrIndex + 2] = 0;
        Rejects(font, "CFF ", "offSize");
    }

    // CID Top DICT: ROS (17 bytes), CharStrings (6), FDArray (7), FDSelect (7).
    private const int CidFdArrayOperand = 17 + 6 + 1;
    private const int CidFdSelectOperand = 17 + 6 + 7 + 1;

    [Fact]
    public void FdArrayWithoutFdSelect_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        font[l.TopDict + CidFdSelectOperand + 5] = 38; // FDSelect (12 37) becomes 12 38 (FontName)
        Rejects(font, "CFF ", "FDSelect");
    }

    [Fact]
    public void FdArrayOutsideTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        Poke(font, l.TopDict + CidFdArrayOperand, Be.U32(0x7FFF0000));
        Rejects(font, "CFF ", "FDArray");
    }

    [Fact]
    public void FdSelectOutsideTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        Poke(font, l.TopDict + CidFdSelectOperand, Be.U32(0x7FFF0000));
        Rejects(font, "CFF ", "FDSelect");
    }

    [Fact]
    public void FdSelectUnknownFormat_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        font[l.FdSelect] = 2;
        Rejects(font, "CFF ", "FDSelect format");
    }

    [Fact]
    public void FdSelectFontDictOutOfRange_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        font[l.FdSelect + 5] = 1; // only one Font DICT
        Rejects(font, "CFF ", "Font DICT 1");
    }

    [Fact]
    public void FdSelectSentinelNotNumGlyphs_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        Poke(font, l.FdSelect + 6, Be.U16(2));
        Rejects(font, "CFF ", "sentinel");
    }

    [Fact]
    public void FdSelectFirstRangeNotGlyphZero_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        Poke(font, l.FdSelect + 3, Be.U16(1));
        Rejects(font, "CFF ", "first range");
    }

    [Fact]
    public void FdSelectFormat0_Passes()
    {
        var font = Cff([Square, Square], localSubrs: ["return"], cid: true, fdSelect: [0, 0, 0, 0]);
        Should.NotThrow(() => FontStructureValidator.Validate(font, 0).EnsureGlyph(2));
    }

    [Fact]
    public void FdSelectFormat0FontDictOutOfRange_IsRejected() =>
        Rejects(Cff([Square, Square], localSubrs: ["return"], cid: true, fdSelect: [0, 0, 3, 0]), "CFF ", "Font DICT 3");

    [Fact]
    public void FdSelectFormat0PastTheTable_IsRejected() =>
        Rejects(Cff([Square, Square], localSubrs: ["return"], cid: true, fdSelect: [0, 0]), "CFF ", "FDSelect");

    [Fact]
    public void FontDictPrivateOutsideTheTable_IsRejected()
    {
        var font = SyntheticCff(out var l, cid: true);
        Poke(font, l.FdArrayIndex + 11 + 6, Be.U32(0x7FFF0000));
        Rejects(font, "CFF ", "Private DICT");
    }

    // ------------------------------------------------------------ CFF: charstrings

    private static void RejectsCharstring(string program, string rule, string[]? localSubrs = null, string[]? globalSubrs = null, bool cid = false) =>
        RejectsGlyph(Cff([program], globalSubrs, localSubrs, cid), 1, "CFF ", rule);

    private static void AcceptsCharstring(string program, string[]? localSubrs = null, string[]? globalSubrs = null, bool cid = false) =>
        AcceptsGlyph(Cff([program], globalSubrs, localSubrs, cid), 1);

    [Fact]
    public void EverySourceSerifGlyph_Passes()
    {
        var v = FontStructureValidator.Validate(SfntEditor.SourceSerif().Bytes, 0);
        for (int g = 0; g < v.NumGlyphs; g++)
            v.EnsureGlyph(g);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubroutineCalls_Pass(bool cid) =>
        AcceptsCharstring("0 0 rmoveto -107 callsubr -107 callgsubr endchar",
            localSubrs: ["100 0 rlineto return"], globalSubrs: ["0 100 rlineto return"], cid: cid);

    [Theory]
    [InlineData("-106 callsubr endchar")]   // subr 1 of 1
    [InlineData("-108 callsubr endchar")]   // subr -1
    [InlineData("-106 callgsubr endchar")]
    public void SubroutineOutsideItsIndex_IsRejected(string program) =>
        RejectsCharstring(program, "outside its INDEX", localSubrs: ["return"], globalSubrs: ["return"]);

    [Fact]
    public void LocalSubroutineWithoutSubrs_IsRejected() => RejectsCharstring("-107 callsubr endchar", "outside its INDEX");

    [Fact]
    public void CidLocalSubroutineOutsideItsIndex_IsRejected() =>
        RejectsCharstring("-106 callsubr endchar", "outside its INDEX", localSubrs: ["return"], cid: true);

    [Fact]
    public void EmptySubroutine_IsRejected() => RejectsCharstring("-107 callsubr endchar", "empty subroutine", localSubrs: [""]);

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void SubroutineNesting_IsLimitedToTen(int depth, bool passes)
    {
        // subr i calls subr i+1; the last returns. The charstring's call is the first level.
        var subrs = Enumerable.Range(0, depth)
            .Select(i => i < depth - 1 ? $"{i + 1 - 107} callsubr return" : "return").ToArray();
        if (passes)
            AcceptsCharstring("-107 callsubr endchar", localSubrs: subrs);
        else
            RejectsCharstring("-107 callsubr endchar", "nesting deeper than 10", localSubrs: subrs);
    }

    [Fact]
    public void RecursiveSubroutine_IsRejected() =>
        RejectsCharstring("-107 callsubr endchar", "nesting deeper than 10", localSubrs: ["-107 callsubr return"]);

    [Fact]
    public void StackOverflow_IsRejected() => RejectsCharstring(Repeat("1", 49) + " endchar", "more than 48");

    [Fact]
    public void FullStack_Passes() => AcceptsCharstring(Repeat("1", 48) + " endchar");

    [Theory]
    [InlineData("0x02")]
    [InlineData("0x00")]
    [InlineData("0x11")]
    public void ReservedOperator_IsRejected(string op) => RejectsCharstring($"0 0 rmoveto {op} endchar", "reserved operator");

    [Fact]
    public void ReturnOutsideASubroutine_IsRejected() => RejectsCharstring("0 0 rmoveto return endchar", "return outside a subroutine");

    [Fact]
    public void CharstringWithoutEndchar_IsRejected() => RejectsCharstring("0 0 rmoveto 10 10 rlineto", "without endchar");

    [Fact]
    public void SubroutineFallingOffItsEnd_IsRejected() =>
        RejectsCharstring("-107 callsubr endchar", "without endchar", localSubrs: ["10 10 rlineto"]);

    [Fact]
    public void EndcharInsideASubroutine_Passes() => AcceptsCharstring("-107 callsubr", localSubrs: [Square]);

    [Fact]
    public void ExponentialSubroutineFanOut_IsBoundedByTheOperationBudget()
    {
        var subrs = Enumerable.Range(0, 9)
            .Select(i => i < 8 ? Repeat($"{i + 1 - 107} callsubr", 100) + " return" : "return").ToArray();
        RejectsCharstring("-107 callsubr endchar", "operations", localSubrs: subrs);
    }

    [Fact]
    public void TooManyPathCommands_IsRejected()
    {
        string subr = Repeat("1 0 rlineto -1 0 rlineto", 500) + " return";
        RejectsCharstring("0 0 rmoveto " + Repeat("-107 callsubr", 70) + " endchar", "path commands", localSubrs: [subr]);
    }

    [Fact]
    public void CoordinatesBeyondTheLimit_IsRejected() =>
        RejectsCharstring("0 0 rmoveto 30000 0 rlineto 30000 0 rlineto 30000 0 rlineto endchar", "coordinate");

    [Fact]
    public void HintMaskBytesAreSkipped()
    {
        // Three stems → a one-byte mask; the mask byte 0x0B would be "return" if it were read as an operator.
        AcceptsCharstring("10 20 hstemhm 10 20 30 40 vstemhm hintmask 0x0B " + Square);
    }

    [Theory]
    [InlineData("0 0 rmoveto 1 abs endchar")] // arithmetic operator stb_truetype does not implement
    [InlineData("rmoveto endchar")]           // too few operands
    public void CharstringStbStopsOn_IsAcceptedAsAnEmptyGlyph(string program) => AcceptsCharstring(program);

    [Fact]
    public void EveryDrawingOperator_Passes() =>
        AcceptsCharstring(
            "0 0 rmoveto 10 hmoveto 10 vmoveto 1 2 3 4 hlineto 1 2 3 4 vlineto 1 2 3 4 5 6 rrcurveto "
            + "1 2 3 4 5 6 7 8 rcurveline 1 2 3 4 5 6 7 8 rlinecurve 1 2 3 4 5 vvcurveto 1 2 3 4 5 hhcurveto "
            + "1 2 3 4 5 vhcurveto 1 2 3 4 hvcurveto 1 2 3 4 5 6 7 hflex 1 2 3 4 5 6 7 8 9 10 11 12 13 flex "
            + "1 2 3 4 5 6 7 8 9 hflex1 1 2 3 4 5 6 7 8 9 10 11 flex1 endchar");

    // ------------------------------------------------------------ output size

    [Theory]
    [InlineData(16385, 1)]
    [InlineData(1, 16385)]
    [InlineData(-1, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void BitmapOutsideTheLimit_IsRejected(double width, double height) =>
        Should.Throw<RasterizationException>(() => FontStructureValidator.EnsureBitmap(7, width, height))
            .Message.ShouldContain("16384", Case.Sensitive);

    [Theory]
    [InlineData(16384, 16384)]
    [InlineData(0, 0)]
    public void BitmapAtTheLimit_Passes(double width, double height) =>
        Should.NotThrow(() => FontStructureValidator.EnsureBitmap(7, width, height));
}
