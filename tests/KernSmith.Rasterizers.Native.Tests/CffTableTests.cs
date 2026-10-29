using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Cff;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 166 Part 2 — CFF/CFF2 table structure: INDEX, DICT, Private DICT widths and local
/// subroutines, CID-keyed FDArray/FDSelect, the CFF2 header and variation store, FontMatrix.
/// Built from synthetic tables so each structure can be exercised in isolation.
/// </summary>
public class CffTableTests
{
    private static readonly RasterOptions Options = new() { Size = 32 };

    private static CffOutlineDecoder Decoder(byte[] table, string tag = "CFF ") =>
        (CffOutlineDecoder)OutlineDecoders.ForFace(NativeFontFace.Load(SyntheticFonts.CffFlavoured(table, tag)));

    private static string Path(CffOutlineDecoder decoder, int glyph) =>
        Type2CharstringTests.Describe(decoder.Decode(glyph, Options).Outline.Commands);

    // ---- Name-keyed --------------------------------------------------------------------------

    [Fact]
    public void NameKeyed_UsesThePrivateDictsLocalSubrsAndWidths()
    {
        var table = SyntheticCff.NameKeyed(
            [
                Cs.Of(T2.EndChar),
                Cs.Of(50, 10, 10, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar),   // width 600 + 50
                Cs.Of(10, 10, T2.RMoveTo, -107, T2.CallGSubr, T2.EndChar),      // width default 400
            ],
            new SyntheticPrivate([Cs.Of(100, T2.HLineTo, T2.Return)], DefaultWidthX: 400, NominalWidthX: 600),
            globalSubrs: [Cs.Of(30, T2.VLineTo, T2.Return)]);

        var decoder = Decoder(table);
        decoder.Font.IsCff2.ShouldBeFalse();
        decoder.Font.IsCidKeyed.ShouldBeFalse();

        Path(decoder, 1).ShouldBe("M 10,10 L 110,10 Z");
        Path(decoder, 2).ShouldBe("M 10,10 L 10,40 Z");
        decoder.CharstringWidth(0).ShouldBe(400);
        decoder.CharstringWidth(1).ShouldBe(650);
        decoder.CharstringWidth(2).ShouldBe(400);
    }

    [Fact]
    public void Decode_TakesTheAdvanceFromHmtx_NotTheCharstringWidth()
    {
        var table = SyntheticCff.NameKeyed(
            [Cs.Of(T2.EndChar), Cs.Of(9999, 0, 0, T2.RMoveTo, 10, T2.HLineTo, T2.EndChar)],
            new SyntheticPrivate([]));

        var face = NativeFontFace.Load(SyntheticFonts.CffFlavoured(table));
        var decoded = OutlineDecoders.ForFace(face).Decode(1, Options);

        decoded.AdvanceWidth.ShouldBe(face.Hmtx.GetAdvanceWidth(1));
        decoded.DeclaredBounds.ShouldBeNull();
    }

    [Fact]
    public void GlyphBeyondTheCharStrings_Throws()
    {
        var decoder = Decoder(SyntheticCff.NameKeyed([Cs.Of(T2.EndChar)], new SyntheticPrivate([])));
        Should.Throw<FontFormatException>(() => decoder.Decode(1, Options));
    }

    // ---- CID-keyed ---------------------------------------------------------------------------

    /// <summary>
    /// Two Font DICTs whose local subr 0 draws different shapes and whose widths differ, so a
    /// glyph decoded against the wrong Private DICT is visible in both its path and its width.
    /// </summary>
    private static readonly SyntheticPrivate[] TwoFontDicts =
    [
        new([Cs.Of(100, T2.HLineTo, T2.Return)], DefaultWidthX: 111, NominalWidthX: 1000),
        new([Cs.Of(200, T2.VLineTo, T2.Return)], DefaultWidthX: 222, NominalWidthX: 2000),
    ];

    private static readonly byte[][] CidGlyphs =
    [
        Cs.Of(T2.EndChar),
        Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar),
        Cs.Of(5, 0, 0, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar),
        Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar),
    ];

    [Fact]
    public void CidKeyed_FdSelectFormat0_PicksEachGlyphsPrivateDict()
    {
        var decoder = Decoder(SyntheticCff.CidKeyed(CidGlyphs, TwoFontDicts, SyntheticCff.FdSelectFormat0(0, 0, 1, 1)));
        AssertCidAssignment(decoder);
    }

    [Fact]
    public void CidKeyed_FdSelectFormat3_PicksEachGlyphsPrivateDict()
    {
        var decoder = Decoder(SyntheticCff.CidKeyed(CidGlyphs, TwoFontDicts, SyntheticCff.FdSelectFormat3(4, (0, 0), (2, 1))));
        AssertCidAssignment(decoder);
    }

    private static void AssertCidAssignment(CffOutlineDecoder decoder)
    {
        decoder.Font.IsCidKeyed.ShouldBeTrue();
        decoder.Font.FontDictCount.ShouldBe(2);

        Path(decoder, 1).ShouldBe("M 0,0 L 100,0 Z");
        Path(decoder, 2).ShouldBe("M 0,0 L 0,200 Z");
        Path(decoder, 3).ShouldBe("M 0,0 L 0,200 Z");
        decoder.CharstringWidth(1).ShouldBe(111);
        decoder.CharstringWidth(2).ShouldBe(2005);
        decoder.CharstringWidth(3).ShouldBe(222);
    }

    [Fact]
    public void CidKeyed_FdSelectNamingAMissingFontDict_FailsTheLoad()
    {
        Should.Throw<FontFormatException>(() =>
            Decoder(SyntheticCff.CidKeyed(CidGlyphs, TwoFontDicts, SyntheticCff.FdSelectFormat0(0, 0, 2, 1))));
        Should.Throw<FontFormatException>(() =>
            Decoder(SyntheticCff.CidKeyed(CidGlyphs, TwoFontDicts, SyntheticCff.FdSelectFormat3(4, (0, 0), (2, 7)))));
    }

    [Fact]
    public void CidKeyed_FdSelectFormat3_NotStartingAtGlyphZero_FailsTheLoad()
    {
        Should.Throw<FontFormatException>(() =>
            Decoder(SyntheticCff.CidKeyed(CidGlyphs, TwoFontDicts, SyntheticCff.FdSelectFormat3(4, (1, 0), (2, 1)))));
    }

    [Fact]
    public void CidKeyed_SeacIsUnavailable()
    {
        var glyphs = new[] { Cs.Of(T2.EndChar), Cs.Of(0, 0, 65, 194, T2.EndChar) };
        var decoder = Decoder(SyntheticCff.CidKeyed(glyphs, TwoFontDicts, SyntheticCff.FdSelectFormat0(0, 0)));
        Should.Throw<FontFormatException>(() => decoder.Decode(1, Options));
    }

    // ---- seac through the charset ------------------------------------------------------------

    [Fact]
    public void Seac_ResolvesComponentsThroughTheCharset()
    {
        // The predefined ISOAdobe charset makes glyph i SID i: 'A' (code 65) is SID 34, acute
        // (code 194) is SID 125.
        var glyphs = new byte[130][];
        for (int i = 0; i < glyphs.Length; i++)
            glyphs[i] = Cs.Of(T2.EndChar);
        glyphs[34] = Cs.Of(0, 0, T2.RMoveTo, 300, T2.HLineTo, T2.EndChar);
        glyphs[125] = Cs.Of(0, 0, T2.RMoveTo, 50, T2.VLineTo, T2.EndChar);
        glyphs[129] = Cs.Of(100, 500, 65, 194, T2.EndChar);

        var decoder = Decoder(SyntheticCff.NameKeyed(glyphs, new SyntheticPrivate([])));
        Path(decoder, 129).ShouldBe("M 0,0 L 300,0 Z M 100,500 L 100,550 Z");
    }

    // ---- FontMatrix --------------------------------------------------------------------------

    private static readonly byte[] UniformMatrix =
        [.. CffBytes.Real("0.001"), 139, 139, .. CffBytes.Real("0.001"), 139, 139, 12, 7];

    /// <summary>x stretched 1.5× relative to y, and shifted by 0.01 em (10 normalized units).</summary>
    private static readonly byte[] StretchedMatrix =
        [.. CffBytes.Real("0.0015"), 139, 139, .. CffBytes.Real("0.001"), .. CffBytes.Real("0.01"), 139, 12, 7];

    private static readonly byte[][] Bar = [Cs.Of(T2.EndChar), Cs.Of(0, 0, T2.RMoveTo, 1000, T2.HLineTo, T2.EndChar)];

    [Fact]
    public void FontMatrix_Absent_LeavesCharstringUnitsAsFontUnits()
    {
        // The synthetic face carries Roboto's head (unitsPerEm 2048); the charstring is already in those units.
        Path(Decoder(SyntheticCff.NameKeyed(Bar, new SyntheticPrivate([]))), 1).ShouldBe("M 0,0 L 1000,0 Z");
    }

    [Fact]
    public void FontMatrix_UniformScale_IsIgnored()
    {
        // 0.001 against a 2048-unit em does not rescale: head.unitsPerEm sets the em.
        Path(Decoder(SyntheticCff.NameKeyed(Bar, new SyntheticPrivate([]), fontMatrix: UniformMatrix)), 1)
            .ShouldBe("M 0,0 L 1000,0 Z");
    }

    [Fact]
    public void FontMatrix_Shape_IsApplied_NormalizedToTheYScale()
    {
        Path(Decoder(SyntheticCff.NameKeyed(Bar, new SyntheticPrivate([]), fontMatrix: StretchedMatrix)), 1)
            .ShouldBe("M 10,0 L 1510,0 Z");
    }

    [Fact]
    public void FontMatrix_ThatBlowsUpCoordinates_IsRejected()
    {
        // x scaled a million times y: the charstring stays tiny, but the transformed outline
        // would demand a bitmap millions of pixels wide.
        byte[] hostile = [.. CffBytes.Real("1000"), 139, 139, .. CffBytes.Real("0.001"), 139, 139, 12, 7];
        var decoder = Decoder(SyntheticCff.NameKeyed(Bar, new SyntheticPrivate([]), fontMatrix: hostile));
        Should.Throw<FontFormatException>(() => decoder.Decode(1, Options));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("uniform")]
    [InlineData("stretched")]
    public void FontMatrix_MatchesFreeType(string kind)
    {
        var fontMatrix = kind switch { "uniform" => UniformMatrix, "stretched" => StretchedMatrix, _ => null };

        // One identical 1000-unit square per glyph. 229 is the most the implicit ISOAdobe charset
        // covers — FreeType rejects a larger font relying on it — and Roboto's cmap maps 'H' well
        // inside that.
        var glyphs = new byte[229][];
        for (int i = 0; i < glyphs.Length; i++)
            glyphs[i] = Cs.Of(0, 0, T2.RMoveTo, 1000, 0, 0, 1000, -1000, 0, T2.RLineTo, T2.EndChar);

        var font = SyntheticFonts.CffFlavoured(SyntheticCff.NameKeyed(glyphs, new SyntheticPrivate([]), fontMatrix: fontMatrix));
        var options = new RasterOptions { Size = 64, EnableHinting = false };

        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(font);
        using var native = new NativeRasterizer();
        native.LoadFont(font);

        native.GetGlyphIndex('H').ShouldBeInRange(1, 228);
        var expected = freeType.RasterizeGlyph('H', options);
        var actual = native.RasterizeGlyph('H', options);

        expected.ShouldNotBeNull();
        actual.ShouldNotBeNull();
        expected!.Width.ShouldBeGreaterThan(10);
        Math.Abs(actual!.Width - expected.Width).ShouldBeLessThanOrEqualTo(1, $"width: native {actual.Width} vs FreeType {expected.Width}");
        Math.Abs(actual.Height - expected.Height).ShouldBeLessThanOrEqualTo(1, $"height: native {actual.Height} vs FreeType {expected.Height}");
        Math.Abs(actual.Metrics.BearingX - expected.Metrics.BearingX).ShouldBeLessThanOrEqualTo(1,
            $"bearingX: native {actual.Metrics.BearingX} vs FreeType {expected.Metrics.BearingX}");
    }

    // ---- CFF2 --------------------------------------------------------------------------------

    [Fact]
    public void Cff2_SpecExample_RendersTheDefaultInstance()
    {
        var decoder = Decoder(Cff2Example(), "CFF2");
        decoder.Font.IsCff2.ShouldBeTrue();
        decoder.Font.RegionCounts.ShouldBe([2]);

        // subr 0: "50 50 100 1 blend 0 rmoveto  500 -100 -200 1 blend hlineto  500 vlineto
        //          -500 100 200 1 blend hlineto" → a 500×500 square at x = 50.
        Path(decoder, 0).ShouldBe("M 50,0 L 550,0 L 550,500 L 50,500 Z");
        Path(decoder, 1).ShouldBe("M 50,0 L 550,0 L 550,500 L 50,500 Z");
    }

    [Fact]
    public void Cff2_TruncatedHeaderOrTopDict_FailsTheLoad()
    {
        var example = Cff2Example();
        Should.Throw<FontFormatException>(() => Decoder(example[..4], "CFF2"));
        Should.Throw<FontFormatException>(() => Decoder(example[..10], "CFF2"));
    }

    [Fact]
    public void Cff2_BlendInAPrivateDictWithoutAVariationStore_FailsTheLoad()
    {
        // Overwrite the Top DICT's VariationStoreOffset entry (9B 18) with an inert "0 FontBBox"
        // (8B 05), keeping every offset intact: the Private DICT's blends can then not be sized.
        var example = Cff2Example();
        Decoder(example, "CFF2").Font.RegionCounts.ShouldBe([2]);
        example[10] = 0x8B;
        example[11] = 0x05;
        Should.Throw<FontFormatException>(() => Decoder(example, "CFF2"));
    }

    /// <summary>
    /// The 226-byte example CFF2 table from the OpenType CFF2 chapter, verbatim: two glyphs that
    /// both call one local subroutine drawing a blended 500-unit square, a single Font DICT whose
    /// Private DICT blends its hint values, and a two-region variation store.
    /// </summary>
    private static byte[] Cff2Example() => Hex("""
        02 00 05 00 07 CF 0C 24 C3 11 9B 18 00 00 00 00
        00 26 00 01 00 00 00 0C 00 01 00 00 00 1C 00 01
        00 02 C0 00 E0 00 00 00 C0 00 C0 00 E0 00 00 00
        00 00 00 02 00 00 00 01 00 00 00 02 01 01 03 05
        20 0A 20 0A 00 00 00 01 01 01 05 F7 06 DA 12 77
        9F F8 6C 9D AE 9A F4 9A 95 9F B3 9F 8B 8B 8B 8B
        85 9A 8B 8B 97 73 8B 8B 8C 80 8B 8B 8B 8D 8B 8B
        8C 8A 8B 8B 97 17 06 FB 8E 95 86 9D 8B 8B 8D 17
        07 77 9F F8 6D 9D AD 9A F3 9A 95 9F B3 9F 08 FB
        8D 95 09 1E A0 37 5F 0C 09 8B 0C 0B C2 6E 9E 8C
        17 0A DB 57 F7 02 8C 17 0B B3 9A 77 9F 82 8A 8D
        17 0C 0C DB 95 57 F7 02 85 8B 8D 17 0C 0D F7 06
        13 00 00 00 01 01 01 1B BD BD EF 8C 10 8B 15 F8
        88 27 FB 5C 8C 10 06 F8 88 07 FC 88 EF F7 5C 8C
        10 06
        """);

    private static byte[] Hex(string text) =>
        Convert.FromHexString(string.Concat(text.Where(char.IsAsciiHexDigit)));

    // ---- Structural hardening ----------------------------------------------------------------

    [Fact]
    public void Index_TruncatedOrInconsistent_Throws()
    {
        var good = CffBytes.Index([[1, 2, 3], [4, 5]]);
        CffIndex.Parse(good, 0, cff2: false, out int end).Count.ShouldBe(2);
        end.ShouldBe(good.Length);

        for (int length = 1; length < good.Length; length++)
            Should.Throw<FontFormatException>(() => CffIndex.Parse(good[..length], 0, cff2: false, out _), $"length {length}");

        // offSize 0 and 5 are invalid.
        Should.Throw<FontFormatException>(() => CffIndex.Parse(new byte[] { 0, 1, 0, 1, 1 }, 0, cff2: false, out _));
        Should.Throw<FontFormatException>(() => CffIndex.Parse(new byte[] { 0, 1, 5, 1, 1 }, 0, cff2: false, out _));
        // First offset must be 1; offsets must not decrease.
        Should.Throw<FontFormatException>(() => CffIndex.Parse(new byte[] { 0, 1, 1, 2, 3, 0 }, 0, cff2: false, out _));
        Should.Throw<FontFormatException>(() => CffIndex.Parse(new byte[] { 0, 2, 1, 1, 3, 2, 0, 0 }, 0, cff2: false, out _));
    }

    [Fact]
    public void Index_HugeCount_ThrowsWithoutAllocating()
    {
        // A CFF2 INDEX claiming 4 billion objects in a 9-byte table.
        Should.Throw<FontFormatException>(() => CffIndex.Parse(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 4, 0, 0, 0, 1 }, 0, cff2: true, out _));
        Should.Throw<FontFormatException>(() => CffIndex.Parse(new byte[] { 0xFF, 0xFF, 2, 0, 1 }, 0, cff2: false, out _));
    }

    [Fact]
    public void Index_EmptyIsTwoBytesInCff_AndFourInCff2()
    {
        CffIndex.Parse(new byte[] { 0, 0 }, 0, cff2: false, out int end).Count.ShouldBe(0);
        end.ShouldBe(2);
        CffIndex.Parse(new byte[] { 0, 0, 0, 0 }, 0, cff2: true, out end).Count.ShouldBe(0);
        end.ShouldBe(4);
    }

    [Fact]
    public void Dict_ParsesIntegersRealsAndTwoByteOperators()
    {
        byte[] data = [.. CffBytes.Int32(123456), 17, .. CffBytes.Real("-2.5E-3"), 139, 139, .. CffBytes.Real("0.001"), 139, 139, 12, 7, 28, 0x01, 0x00, 139, 18];
        var dict = CffDict.Parse(data, cff2: false);

        dict.GetInt(CffDict.CharStrings).ShouldBe(123456);
        dict.Get(CffDict.FontMatrix).ShouldBe([-0.0025, 0, 0, 0.001, 0, 0]);
        dict.GetInt(CffDict.Private, 0).ShouldBe(256);
        dict.GetInt(CffDict.Private, 1).ShouldBe(0);
    }

    [Fact]
    public void Dict_Malformed_Throws()
    {
        Should.Throw<FontFormatException>(() => CffDict.Parse([139], cff2: false));          // operand with no operator
        Should.Throw<FontFormatException>(() => CffDict.Parse([28, 1], cff2: false));        // truncated int16
        Should.Throw<FontFormatException>(() => CffDict.Parse([30, 0x1A], cff2: false));     // unterminated real
        Should.Throw<FontFormatException>(() => CffDict.Parse([255, 17], cff2: false));      // reserved byte
        Should.Throw<FontFormatException>(() => CffDict.Parse([12], cff2: false));           // truncated escape
        Should.Throw<FontFormatException>(() => CffDict.Parse([.. Enumerable.Repeat((byte)139, 49), 17], cff2: false));
    }
}
