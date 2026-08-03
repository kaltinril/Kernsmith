using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

public class CoreTableTests
{
    private static NativeFontFace LoadRoboto() => NativeFontFace.Load(TestFonts.RobotoRegularBytes());

    [Fact]
    public void Head_HasExpectedUnitsPerEmAndLocaFormat()
    {
        var face = LoadRoboto();
        // Roboto is drawn on a 2048 unit em.
        face.Head.UnitsPerEm.ShouldBe((ushort)2048);
        // Roboto-Regular is upright and not bold.
        face.Head.IsBold.ShouldBeFalse();
        face.Head.IsItalic.ShouldBeFalse();
        // indexToLocFormat is 0 (short) or 1 (long); both are valid.
        face.Head.IndexToLocFormat.ShouldBeInRange((short)0, (short)1);
    }

    [Fact]
    public void Head_BoundingBoxIsSane()
    {
        var face = LoadRoboto();
        face.Head.XMin.ShouldBeLessThan(face.Head.XMax);
        face.Head.YMin.ShouldBeLessThan(face.Head.YMax);
    }

    [Fact]
    public void Hhea_HasPositiveAscenderAndMetricCount()
    {
        var face = LoadRoboto();
        face.Hhea.Ascender.ShouldBeGreaterThan((short)0);
        face.Hhea.Descender.ShouldBeLessThan((short)0);
        face.Hhea.NumberOfHMetrics.ShouldBeGreaterThan((ushort)0);
    }

    [Fact]
    public void Maxp_NumGlyphsMatchesHmtxCoverage()
    {
        var face = LoadRoboto();
        face.Maxp.NumGlyphs.ShouldBeGreaterThan((ushort)0);
        face.Hmtx.GlyphCount.ShouldBe(face.Maxp.NumGlyphs);
    }

    [Fact]
    public void Hmtx_AdvanceWidthForCapitalA_IsPositive()
    {
        var face = LoadRoboto();
        int glyphA = face.GetGlyphIndex('A');
        glyphA.ShouldBeGreaterThan(0);
        face.Hmtx.GetAdvanceWidth(glyphA).ShouldBeGreaterThan((ushort)0);
    }

    [Fact]
    public void Os2_AscentDescentArePopulated()
    {
        var face = LoadRoboto();
        face.Os2.TypoAscender.ShouldBeGreaterThan((short)0);
        face.Os2.TypoDescender.ShouldBeLessThan((short)0);
        face.Os2.WinAscent.ShouldBeGreaterThan((ushort)0);
        face.Os2.WinDescent.ShouldBeGreaterThan((ushort)0);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('0')]
    [InlineData(' ')]
    public void Cmap_MapsCommonAsciiToGlyphs(int codepoint)
    {
        var face = LoadRoboto();
        face.GetGlyphIndex(codepoint).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Cmap_UnmappedCodepointReturnsZero()
    {
        var face = LoadRoboto();
        // A private-use / unlikely-to-be-covered codepoint maps to .notdef (0).
        face.GetGlyphIndex(0x10FFFF).ShouldBe(0);
    }

    [Fact]
    public void Cmap_DistinctCharactersMapToDistinctGlyphs()
    {
        var face = LoadRoboto();
        int a = face.GetGlyphIndex('a');
        int b = face.GetGlyphIndex('b');
        a.ShouldNotBe(b);
    }

    /// <summary>
    /// numGroups sizes three arrays, so it has to be checked against the bytes that are
    /// actually there before any of them is allocated. Untrusted font data reaching
    /// <c>new uint[numGroups]</c> unchecked turns a malformed file into an OutOfMemory or
    /// Overflow escape rather than the FontFormatException every other parse path raises.
    /// </summary>
    [Theory]
    [InlineData(0xFFFFFFF0u)] // Past int.MaxValue — the array length itself overflows.
    [InlineData(0x10000000u)] // Fits in an int, but would demand 3 GB across the three arrays.
    [InlineData(64u)]         // Plausible, yet still more groups than the table has bytes for.
    public void Cmap_Format12WithMoreGroupsThanBytes_ThrowsFontFormatException(uint numGroups)
    {
        var cmap = Format12CmapWithGroupCount(numGroups);

        Should.Throw<FontFormatException>(() => CmapTable.Parse(cmap));
    }

    /// <summary>
    /// A cmap holding a single format 12 subtable whose header declares
    /// <paramref name="numGroups"/> groups but carries no group records at all.
    /// </summary>
    private static byte[] Format12CmapWithGroupCount(uint numGroups)
    {
        // cmap header (4) + one encoding record (8) + the format 12 subtable header (16).
        var cmap = new byte[28];
        var span = cmap.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 0);          // version
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 1);     // numTables
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 3);     // platformId: Windows
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 10);    // encodingId: full Unicode
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 12);    // subtable offset

        BinaryPrimitives.WriteUInt16BigEndian(span[12..], 12);   // format
        BinaryPrimitives.WriteUInt16BigEndian(span[14..], 0);    // reserved
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], 16);   // length
        BinaryPrimitives.WriteUInt32BigEndian(span[20..], 0);    // language
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], numGroups);

        return cmap;
    }
}
