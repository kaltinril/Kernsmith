using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;
using static KernSmith.Tests.Rasterizer.StbStructure.SyntheticFonts;

namespace KernSmith.Tests.Rasterizer.StbStructure;

/// <summary>
/// The StbTrueType backend runs its structure check before stb_truetype sees the font: a
/// non-conforming font fails <see cref="StbTrueTypeRasterizer.LoadFont"/>, a damaged glyph
/// fails when it is first measured or rendered, a cmap entry past numGlyphs is unmapped, and a
/// bitmap over 16384 px per side fails before stb_truetype allocates it.
/// </summary>
public class StbTrueTypeStructureCheckTests
{
    private static readonly RasterOptions Options = new() { Size = 32 };

    private static StbTrueTypeRasterizer Load(byte[] font)
    {
        var rasterizer = new StbTrueTypeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    private static int GlyphOf(int codepoint)
    {
        using var rasterizer = Load(SfntEditor.Roboto().Bytes);
        return rasterizer.RasterizeGlyph(codepoint, Options)!.GlyphIndex;
    }

    [Fact]
    public void NonConformingFont_FailsLoadFont()
    {
        var font = SfntEditor.Roboto().SetU16("hhea", 34, 0);
        using var rasterizer = new StbTrueTypeRasterizer();

        var ex = Should.Throw<FontParsingException>(() => rasterizer.LoadFont(font.Bytes));

        ex.TableTag.ShouldBe("hhea");
    }

    [Fact]
    public void FontWithoutAUnicodeCmap_FailsLoadFontWithFontParsingException()
    {
        var font = SfntEditor.Roboto();
        for (int i = 0; i < 4; i++)
            font.SetU16("cmap", 4 + (8 * i), 1).SetU16("cmap", 4 + (8 * i) + 2, 0);
        using var rasterizer = new StbTrueTypeRasterizer();

        Should.Throw<FontParsingException>(() => rasterizer.LoadFont(font.Bytes)).TableTag.ShouldBe("cmap");
    }

    [Fact]
    public void CmapEntryPastNumGlyphs_IsUnmapped()
    {
        var font = SfntEditor.Roboto().ReplaceTable("cmap", CmapWith(3, 1, Format6(0x41, 60000, 36)));
        using var rasterizer = Load(font.Bytes);

        rasterizer.RasterizeGlyph(0x41, Options).ShouldBeNull();
        rasterizer.GetGlyphMetrics(0x41, Options).ShouldBeNull();
        rasterizer.RasterizeGlyph(0x42, Options).ShouldNotBeNull().GlyphIndex.ShouldBe(36);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DamagedGlyph_FailsWhenUsed(bool metrics, bool bold)
    {
        var font = SfntEditor.Roboto();
        int glyph = GlyphOf('A');
        int at = font.OffsetOf("glyf") + font.GlyphRecord(glyph).Start;
        Be.U16(1).CopyTo(font.Bytes, at); // one contour
        Be.U16(0xFFFF).CopyTo(font.Bytes, at + 12); // instructionLength past the record
        using var rasterizer = Load(font.Bytes);
        var options = new RasterOptions { Size = 32, Bold = bold };

        var ex = Should.Throw<FontParsingException>(() =>
        {
            if (metrics)
                rasterizer.GetGlyphMetrics('A', options);
            else
                rasterizer.RasterizeGlyph('A', options);
        });

        ex.TableTag.ShouldBe("glyf");
        rasterizer.RasterizeGlyph('B', Options).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void GlyphBoxOverTheBitmapLimit_FailsBeforeAllocating(bool sdf, bool bold, bool metrics)
    {
        // A header (and outline) spanning the whole coordinate space: 65534 units at 1100/2048 px
        // per unit is 35,199 px wide — past the 16384 px limit, yet only 54 rows. Synthetic bold
        // wraps the outermost points (stb_truetype vertices are 16-bit), halving the span, which
        // is still over the limit.
        var font = SfntEditor.Roboto();
        int glyph = GlyphOf('A');
        int at = font.OffsetOf("glyf") + font.GlyphRecord(glyph).Start;
        Be.Cat(Be.U16(-32768), Be.U16(0), Be.U16(32767), Be.U16(100)).CopyTo(font.Bytes, at + 2);
        WidenFirstPoints(font, glyph);
        using var rasterizer = Load(font.Bytes);
        var options = new RasterOptions { Size = 1100, Sdf = sdf, Bold = bold };

        var ex = Should.Throw<RasterizationException>(() =>
        {
            if (metrics)
                rasterizer.GetGlyphMetrics('A', options);
            else
                rasterizer.RasterizeGlyph('A', options);
        });

        ex.Message.ShouldContain("16384", Case.Sensitive);
    }

    /// <summary>Replaces the glyph with a triangle from x = -32768 to 32766, so the outline-boxed (styled) paths see the same span as the header.</summary>
    private static void WidenFirstPoints(SfntEditor font, int glyph)
    {
        byte[] record = Be.Cat(
            Be.U16(1), Be.U16(-32768), Be.U16(0), Be.U16(32767), Be.U16(100),
            Be.U16(2), Be.U16(0), [1, 1, 1],
            Be.U16(-32768), Be.U16(32767), Be.U16(32767), // x deltas: -32768, then +32767 twice
            Be.U16(0), Be.U16(100), Be.U16(-100));
        font.WriteGlyph(glyph, record);
    }

    [Fact]
    public void OrdinaryGlyphAtALargeSize_StillRenders()
    {
        using var rasterizer = Load(SfntEditor.Roboto().Bytes);

        var glyph = rasterizer.RasterizeGlyph('A', new RasterOptions { Size = 1000 });

        glyph.ShouldNotBeNull().Width.ShouldBeGreaterThan(500);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SyntheticCffFont_Renders(bool cid, bool sdf)
    {
        using var rasterizer = Load(Cff(["0 0 rmoveto -107 callsubr endchar"], localSubrs: ["700 0 rlineto 0 700 rlineto -700 0 rlineto return"], cid: cid));

        var glyph = rasterizer.RasterizeGlyph(FirstCodepoint, new RasterOptions { Size = 32, Sdf = sdf });

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(15);
        glyph.BitmapData.Max().ShouldBe((byte)255);
    }

    [Fact]
    public void DamagedCharstring_FailsWhenUsed()
    {
        using var rasterizer = Load(Cff([Square, "0 0 rmoveto -106 callsubr endchar"], localSubrs: ["return"]));

        rasterizer.RasterizeGlyph(FirstCodepoint, Options).ShouldNotBeNull();
        Should.Throw<FontParsingException>(() => rasterizer.RasterizeGlyph(FirstCodepoint + 1, Options)).TableTag.ShouldBe("CFF ");
    }

    [Fact]
    public void OnePointOffCurveGlyph_RendersEmptyWhenStyled()
    {
        // The Windows-font "space": stb_truetype would otherwise start the contour from memory
        // past its vertex buffer, and the styled paths would box whatever it found there.
        var font = SfntEditor.Roboto();
        font.WriteGlyph(GlyphOf('A'), Be.Cat(Be.U16(1), new byte[8], Be.U16(0), Be.U16(0), [0], Be.U16(0), Be.U16(0)));
        using var rasterizer = Load(font.Bytes);

        foreach (var options in new[] { Options, new RasterOptions { Size = 32, Sdf = true } })
        {
            var glyph = rasterizer.RasterizeGlyph('A', options).ShouldNotBeNull();
            glyph.Width.ShouldBe(0);
            glyph.Height.ShouldBe(0);
        }

        // Bold offsets the zero-length contour into a one-pixel box that stays blank.
        var bold = rasterizer.RasterizeGlyph('A', new RasterOptions { Size = 32, Bold = true }).ShouldNotBeNull();
        bold.Width.ShouldBeLessThanOrEqualTo(1);
        bold.Height.ShouldBeLessThanOrEqualTo(1);
        bold.BitmapData.ShouldAllBe(b => b == 0);
    }
}
