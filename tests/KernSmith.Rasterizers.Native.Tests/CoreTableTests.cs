using KernSmith.Rasterizers.Native.Internal;
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
}
