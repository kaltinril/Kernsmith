using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticBitmapTables;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 bitmap color glyphs (<c>CBDT</c>/<c>CBLC</c> and <c>sbix</c>) on hand-built tables,
/// where every placement, strike and pixel is known.
/// </summary>
public class NativeBitmapGlyphTests
{
    private static readonly int GlyphA = RobotoGlyph('A');
    private static readonly int GlyphB = RobotoGlyph('B');
    private static readonly int GlyphC = RobotoGlyph('C');

    /// <summary>A 4x3 PNG whose pixels are all different.</summary>
    private static readonly byte[] Distinct = Png(4, 3, (x, y) => ((byte)(x * 60), (byte)(y * 100), (byte)(200 - (x * 20)), (byte)(255 - (y * 50))));

    // ---- CBDT: locating, formats, metrics -----------------------------------------------------

    [Fact]
    public void Cbdt_AtTheStrikeSize_ReturnsThePngPixelsAndTheStoredMetrics()
    {
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, Distinct, 4, 3, 2, 5, 7)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = 20, ColorFont = true })!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Metrics.ShouldBe(new GlyphMetrics(BearingX: 2, BearingY: 5, Advance: HmtxAdvance(GlyphA, 20), Width: 4, Height: 3));
        glyph.BitmapData.ShouldBe(Pixels(Distinct));
    }

    [Theory]
    [InlineData(1, 17)]
    [InlineData(1, 18)]
    [InlineData(3, 17)]
    [InlineData(3, 18)]
    [InlineData(4, 17)]
    [InlineData(4, 18)]
    [InlineData(2, 19)]
    [InlineData(5, 19)]
    public void Cbdt_EveryIndexAndPngImageFormat_LocatesTheBitmap(int indexFormat, int imageFormat)
    {
        // Two glyphs with a gap between them, so offset arrays and glyph lists both matter.
        var other = SolidPng(4, 3, 9, 9, 9);
        var font = BitmapOnly(new CbdtStrike(20, indexFormat, imageFormat,
            [new CbdtGlyph(GlyphA, Distinct, 4, 3, 2, 5, 7), new CbdtGlyph(GlyphC, other, 4, 3, 2, 5, 7)]));

        var a = Render(font, 'A', new RasterOptions { Size = 20, ColorFont = true })!;
        var c = Render(font, 'C', new RasterOptions { Size = 20, ColorFont = true })!;

        a.BitmapData.ShouldBe(Pixels(Distinct));
        a.Metrics.BearingX.ShouldBe(2);
        a.Metrics.BearingY.ShouldBe(5);
        c.BitmapData.ShouldBe(Pixels(other));
        Render(font, 'B', new RasterOptions { Size = 20, ColorFont = true })!.Width.ShouldBe(0); // in the range gap: no bitmap
    }

    [Fact]
    public void Cbdt_UnsupportedImageFormat_IsSkipped()
    {
        // Image format 1 (EBDT small metrics, byte-aligned) is not a PNG format.
        var strike = new CbdtStrike(20, 1, 1, [new CbdtGlyph(GlyphA, new byte[12], 4, 3, 2, 5, 7)]);

        // Bitmap-only: the glyph has nothing to draw.
        var blank = Render(BitmapOnly(strike), 'A', new RasterOptions { Size = 20, ColorFont = true })!;
        blank.Width.ShouldBe(0);
        blank.Metrics.Advance.ShouldBe(HmtxAdvance(GlyphA, 20));

        // Mixed: the outline draws instead.
        var (cblc, cbdt) = Cbdt(strike);
        var outline = Render(MixedRoboto(("CBLC", cblc), ("CBDT", cbdt)), 'A', new RasterOptions { Size = 20, ColorFont = true })!;
        outline.Format.ShouldBe(PixelFormat.Grayscale8);
        outline.BitmapData.ShouldBe(Render(TestFonts.RobotoRegularBytes(), 'A', new RasterOptions { Size = 20 })!.BitmapData);
    }

    // ---- Strike selection --------------------------------------------------------------------

    [Theory]
    [InlineData(5f, 10)]
    [InlineData(10f, 10)]
    [InlineData(15f, 20)]
    [InlineData(20f, 20)]
    [InlineData(21f, 40)]
    [InlineData(41f, 40)]
    [InlineData(200f, 40)]
    public void StrikeSelection_PicksTheSmallestStrikeAtOrAboveTheRequest_ElseTheLargest(float size, int expectedPpem)
    {
        // Each strike paints 'A' in its own color; strikes are deliberately out of order.
        var font = BitmapOnly(
            new CbdtStrike(40, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(8, 8, 40, 0, 0), 8, 8, 0, 8, 8)]),
            new CbdtStrike(10, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(8, 8, 10, 0, 0), 8, 8, 0, 8, 8)]),
            new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(8, 8, 20, 0, 0), 8, 8, 0, 8, 8)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = size, ColorFont = true })!;

        // The center pixel is interior at every scale.
        int center = (((glyph.Height / 2) * glyph.Width) + (glyph.Width / 2)) * 4;
        glyph.BitmapData[center].ShouldBe((byte)expectedPpem);
    }

    [Fact]
    public void StrikeSelection_IsPerGlyph_AStrikeWithoutTheGlyphIsPassedOver()
    {
        // 'B' exists only in the 10 ppem strike; at 30 px it is scaled up from there.
        var font = BitmapOnly(
            new CbdtStrike(10, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(4, 4, 1, 1, 1), 4, 4, 0, 4, 4), new CbdtGlyph(GlyphB, SolidPng(4, 4, 2, 2, 2), 4, 4, 0, 4, 4)]),
            new CbdtStrike(40, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(4, 4, 3, 3, 3), 4, 4, 0, 4, 4)]));

        var b = Render(font, 'B', new RasterOptions { Size = 30, ColorFont = true })!;

        b.Metrics.Width.ShouldBe(12);
        b.Metrics.Height.ShouldBe(12);
        b.BitmapData[((6 * 12) + 6) * 4].ShouldBe((byte)2);
    }

    // ---- Scaling -----------------------------------------------------------------------------

    [Fact]
    public void Downscale_AveragesPixelAreas_InPremultipliedColor()
    {
        // A 4x4 checkerboard of opaque red and fully transparent green, halved: every output pixel
        // covers two of each. Premultiplied averaging keeps the red pure (transparent green adds
        // no color) at half coverage.
        var checker = Png(4, 4, (x, y) => ((x + y) % 2 == 0) ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)255, (byte)0, (byte)0));
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, checker, 4, 4, 0, 4, 4)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = 10, ColorFont = true })!;

        glyph.Width.ShouldBe(2);
        glyph.Height.ShouldBe(2);
        for (int i = 0; i < 4; i++)
            glyph.BitmapData.AsSpan(i * 4, 4).ToArray().ShouldBe(new byte[] { 255, 0, 0, 128 });
    }

    [Fact]
    public void Scale_BoxCoversTheScaledBitmapRectangle()
    {
        // 6x8 at bearing (3, 9) on a 20 ppem strike, drawn at 30 px: x 4.5..13.5, y 13.5 down to 1.5.
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(6, 8, 50, 60, 70), 6, 8, 3, 9, 10)]));
        var options = new RasterOptions { Size = 30, ColorFont = true };

        using var native = Load(font);
        var metrics = native.GetGlyphMetrics('A', options);
        var glyph = native.RasterizeGlyph('A', options)!;

        metrics.ShouldBe(new GlyphMetrics(BearingX: 4, BearingY: 14, Advance: HmtxAdvance(GlyphA, 30), Width: 10, Height: 13));
        metrics.ShouldBe(glyph.Metrics);

        // Interior pixels keep the color exactly; the half-covered edge columns are half alpha.
        int row = 6 * glyph.Width;
        glyph.BitmapData.AsSpan((row + 5) * 4, 4).ToArray().ShouldBe(new byte[] { 50, 60, 70, 255 });
        glyph.BitmapData[(row * 4) + 3].ShouldBe((byte)128);
    }

    [Fact]
    public void Upscale_IsBilinear_InteriorKeepsTheColor()
    {
        var font = BitmapOnly(new CbdtStrike(10, 1, 17, [new CbdtGlyph(GlyphA, SolidPng(4, 4, 10, 200, 30), 4, 4, 0, 4, 4)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = 40, ColorFont = true })!;

        glyph.Width.ShouldBe(16);
        glyph.Height.ShouldBe(16);
        glyph.BitmapData.AsSpan(((8 * 16) + 8) * 4, 4).ToArray().ShouldBe(new byte[] { 10, 200, 30, 255 });
        // Outermost pixels fade, since their bilinear neighbourhood reaches past the bitmap edge.
        glyph.BitmapData[3].ShouldBeLessThan((byte)255);
        glyph.BitmapData[3].ShouldBeGreaterThan((byte)0);
    }

    // ---- sbix --------------------------------------------------------------------------------

    [Fact]
    public void Sbix_BitmapOnly_PlacesTheBitmapByItsOriginOffsets()
    {
        // Bottom-left corner at (2, -3) strike pixels from the pen; 4x3 tall, so the top is at y = 0.
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72, [new SbixEntry(GlyphA, 2, -3, "png ", Distinct)]));
        var font = BitmapOnlyRoboto(("sbix", sbix));

        var glyph = Render(font, 'A', new RasterOptions { Size = 20, ColorFont = true })!;

        glyph.Metrics.ShouldBe(new GlyphMetrics(BearingX: 2, BearingY: 0, Advance: HmtxAdvance(GlyphA, 20), Width: 4, Height: 3));
        glyph.BitmapData.ShouldBe(Pixels(Distinct));
    }

    [Fact]
    public void Sbix_WithAGlyfContour_PlacesTheOriginAtTheGlyphBoxCorner()
    {
        // ppem = unitsPerEm / 16, so Roboto's xMin/yMin land on whole strike pixels after floor.
        var parsed = RobotoFace.Value.GetGlyph(GlyphA);
        const int ppem = 128;
        double left = 1 + ((double)parsed.XMin * ppem / 2048.0);
        double bottom = -2 + ((double)parsed.YMin * ppem / 2048.0);
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(ppem, 72, [new SbixEntry(GlyphA, 1, -2, "png ", Distinct)]));
        var font = MixedRoboto(("sbix", sbix));

        var glyph = Render(font, 'A', new RasterOptions { Size = ppem, ColorFont = true })!;

        glyph.Metrics.BearingX.ShouldBe((int)Math.Floor(left));
        glyph.Metrics.BearingY.ShouldBe((int)Math.Ceiling(bottom + 3));
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public void Sbix_Dupe_DrawsTheReferencedGlyphsBitmap()
    {
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72,
        [
            new SbixEntry(GlyphA, 0, 0, "png ", Distinct),
            new SbixEntry(GlyphB, 0, 0, "dupe", Dupe(GlyphA)),
        ]));
        var font = BitmapOnlyRoboto(("sbix", sbix));

        Render(font, 'B', new RasterOptions { Size = 20, ColorFont = true })!.BitmapData.ShouldBe(Pixels(Distinct));
    }

    [Fact]
    public void Sbix_DupeCycle_HasNoBitmap()
    {
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72,
        [
            new SbixEntry(GlyphA, 0, 0, "dupe", Dupe(GlyphB)),
            new SbixEntry(GlyphB, 0, 0, "dupe", Dupe(GlyphA)),
        ]));

        Render(BitmapOnlyRoboto(("sbix", sbix)), 'A', new RasterOptions { Size = 20, ColorFont = true })!.Width.ShouldBe(0);
    }

    [Theory]
    [InlineData("jpg ")]
    [InlineData("tiff")]
    [InlineData("pdf ")]
    [InlineData("mask")]
    public void Sbix_NonPngGraphics_AreSkipped_TheOutlineDrawsInstead(string graphicType)
    {
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72, [new SbixEntry(GlyphA, 0, 0, graphicType, [1, 2, 3, 4])]));

        var glyph = Render(MixedRoboto(("sbix", sbix)), 'A', new RasterOptions { Size = 20, ColorFont = true })!;

        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.BitmapData.ShouldBe(Render(TestFonts.RobotoRegularBytes(), 'A', new RasterOptions { Size = 20 })!.BitmapData);
    }

    [Fact]
    public void Sbix_GlyphMissingFromTheBestStrike_ComesFromAnother()
    {
        var sbix = Sbix(RobotoGlyphCount,
            new SbixStrike(20, 72, [new SbixEntry(GlyphA, 0, 0, "png ", SolidPng(4, 4, 7, 7, 7))]),
            new SbixStrike(40, 72, [new SbixEntry(GlyphB, 0, 0, "png ", SolidPng(4, 4, 8, 8, 8))]));
        var font = BitmapOnlyRoboto(("sbix", sbix));

        // At 40 px the 40 ppem strike is preferred, but it has no 'A'.
        var a = Render(font, 'A', new RasterOptions { Size = 40, ColorFont = true })!;
        a.Width.ShouldBe(8);
        a.BitmapData[((4 * 8) + 4) * 4].ShouldBe((byte)7);
    }

    // ---- Precedence and options --------------------------------------------------------------

    [Fact]
    public void Precedence_IsColrThenBitmapThenOutline()
    {
        // 'A' has COLR layers and a bitmap, 'B' only a bitmap, 'C' only its outline.
        var colr = SyntheticColorTables.Colr((GlyphA, [new SyntheticColorTables.Layer(GlyphA, 0)]));
        var cpal = SyntheticColorTables.Cpal([[0xFF0000FFu]]);
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72,
        [
            new SbixEntry(GlyphA, 0, 0, "png ", SolidPng(4, 4, 0, 255, 0)),
            new SbixEntry(GlyphB, 0, 0, "png ", SolidPng(4, 4, 0, 255, 0)),
        ]));
        var font = MixedRoboto(("COLR", colr), ("CPAL", cpal), ("sbix", sbix));
        var options = new RasterOptions { Size = 20, ColorFont = true };

        var a = Render(font, 'A', options)!;
        var b = Render(font, 'B', options)!;
        var c = Render(font, 'C', options)!;

        // The COLR layer fills Roboto's 'A' in red; the bitmaps are green.
        a.Format.ShouldBe(PixelFormat.Rgba32);
        a.BitmapData.Chunk(4).ShouldContain(p => p[0] == 255 && p[3] == 255);
        a.BitmapData.Chunk(4).ShouldNotContain(p => p[1] > 0);
        b.Format.ShouldBe(PixelFormat.Rgba32);
        b.BitmapData.Chunk(4).ShouldContain(p => p[1] == 255 && p[3] == 255);
        c.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    [Fact]
    public void ColorFontOff_MixedFont_RendersOutlinesOnly()
    {
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72, [new SbixEntry(GlyphA, 0, 0, "png ", Distinct)]));

        var glyph = Render(MixedRoboto(("sbix", sbix)), 'A', new RasterOptions { Size = 20 })!;

        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.BitmapData.ShouldBe(Render(TestFonts.RobotoRegularBytes(), 'A', new RasterOptions { Size = 20 })!.BitmapData);
    }

    [Fact]
    public void ColorFontOff_BitmapOnly_RendersInkCoverage()
    {
        // Coverage is how dark the glyph makes a white background: opaque black is full ink,
        // opaque white none, half-transparent black half.
        var png = Png(3, 1, (x, _) => x switch
        {
            0 => ((byte)0, (byte)0, (byte)0, (byte)255),
            1 => ((byte)255, (byte)255, (byte)255, (byte)255),
            _ => ((byte)0, (byte)0, (byte)0, (byte)128),
        });
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, png, 3, 1, 0, 1, 3)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = 20 })!;

        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.BitmapData.ShouldBe(new byte[] { 255, 0, 128 });
    }

    [Fact]
    public void Sdf_WithColor_BitmapGlyphsStayColor_OthersAreDistanceFields()
    {
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72, [new SbixEntry(GlyphA, 0, 0, "png ", Distinct)]));
        var font = MixedRoboto(("sbix", sbix));
        var options = new RasterOptions { Size = 20, ColorFont = true, Sdf = true };

        // 'A' has a contour, so its bitmap is placed from the glyph box corner; with or without
        // SDF it is the same color bitmap.
        var a = Render(font, 'A', options)!;
        a.Format.ShouldBe(PixelFormat.Rgba32);
        a.BitmapData.ShouldBe(Render(font, 'A', new RasterOptions { Size = 20, ColorFont = true })!.BitmapData);
        Render(font, 'C', options)!.BitmapData.ShouldBe(Render(TestFonts.RobotoRegularBytes(), 'C', new RasterOptions { Size = 20, Sdf = true })!.BitmapData);
    }

    [Fact]
    public void Msdf_WithColor_OnABitmapFont_IsRejected()
    {
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72, [new SbixEntry(GlyphA, 0, 0, "png ", Distinct)]));

        Should.Throw<NotSupportedException>(() => Render(MixedRoboto(("sbix", sbix)), 'A', new RasterOptions { Size = 20, ColorFont = true, SdfMode = SdfMode.Msdf }));
    }

    [Fact]
    public void DistanceField_WithoutColor_OnABitmapOnlyFont_IsRejected()
    {
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, Distinct, 4, 3, 2, 5, 7)]));

        var ex = Should.Throw<NotSupportedException>(() => Render(font, 'A', new RasterOptions { Size = 20, Sdf = true }));
        ex.Message.ShouldContain("color bitmaps", Case.Sensitive);
        Should.Throw<NotSupportedException>(() => Render(font, 'A', new RasterOptions { Size = 20, SdfMode = SdfMode.Mtsdf }));
    }

    [Theory]
    [InlineData(AntiAliasMode.Lcd)]
    [InlineData(AntiAliasMode.Light)]
    public void LcdAndLight_LeaveBitmapGlyphsUnchanged(AntiAliasMode mode)
    {
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, Distinct, 4, 3, 2, 5, 7)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = 20, ColorFont = true, AntiAlias = mode })!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.BitmapData.ShouldBe(Pixels(Distinct));
    }

    [Fact]
    public void AntiAliasNone_ThresholdsAlpha_AndCropsToTheInk()
    {
        // A transparent border around a 2x2 block of 60%-alpha blue and one 40%-alpha pixel.
        var png = Png(4, 4, (x, y) => (x, y) switch
        {
            (1 or 2, 1 or 2) => ((byte)0, (byte)0, (byte)255, (byte)153),
            (3, 3) => ((byte)0, (byte)0, (byte)255, (byte)102),
            _ => ((byte)9, (byte)9, (byte)9, (byte)0),
        });
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, png, 4, 4, 0, 4, 4)]));
        var options = new RasterOptions { Size = 20, ColorFont = true, AntiAlias = AntiAliasMode.None };

        using var native = Load(font);
        var glyph = native.RasterizeGlyph('A', options)!;

        glyph.Metrics.ShouldBe(new GlyphMetrics(BearingX: 1, BearingY: 3, Advance: HmtxAdvance(GlyphA, 20), Width: 2, Height: 2));
        native.GetGlyphMetrics('A', options).ShouldBe(glyph.Metrics);
        for (int i = 0; i < 4; i++)
            glyph.BitmapData.AsSpan(i * 4, 4).ToArray().ShouldBe(new byte[] { 0, 0, 255, 255 });
    }

    [Fact]
    public void BoldAndItalic_LeaveBitmapGlyphsUnchanged_AdvanceIncluded()
    {
        // FreeType's backend leaves bitmap glyphs, advance included, untouched by its synthetic styles.
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, Distinct, 4, 3, 2, 5, 7)]));

        var glyph = Render(font, 'A', new RasterOptions { Size = 20, ColorFont = true, Bold = true, Italic = true })!;

        glyph.BitmapData.ShouldBe(Pixels(Distinct));
        glyph.Metrics.ShouldBe(new GlyphMetrics(BearingX: 2, BearingY: 5, Advance: HmtxAdvance(GlyphA, 20), Width: 4, Height: 3));
    }

    // ---- Hostile data ------------------------------------------------------------------------

    [Fact]
    public void CorruptPng_IsARasterizationException_FromMetricsAndRendering()
    {
        var broken = (byte[])Distinct.Clone();
        broken[^20] ^= 0xFF; // inside IDAT: fails its CRC
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, broken, 4, 3, 2, 5, 7)]));
        var options = new RasterOptions { Size = 20, ColorFont = true };

        using var native = Load(font);
        Should.Throw<RasterizationException>(() => native.RasterizeGlyph('A', options)).Codepoint.ShouldBe('A');
    }

    [Fact]
    public void PngSizeDisagreeingWithTheMetrics_IsARasterizationException()
    {
        var font = BitmapOnly(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, Distinct, 5, 3, 2, 5, 7)]));
        var options = new RasterOptions { Size = 20, ColorFont = true };

        using var native = Load(font);
        Should.Throw<RasterizationException>(() => native.GetGlyphMetrics('A', options));
        Should.Throw<RasterizationException>(() => native.RasterizeGlyph('A', options));
    }

    [Fact]
    public void MalformedBitmapTables_FailABitmapOnlyFontAtLoad_ButAreIgnoredBesideOutlines()
    {
        byte[] garbage = [0, 3, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF];

        using (var native = new NativeRasterizer())
            Should.Throw<FontFormatException>(() => native.LoadFont(BitmapOnlyRoboto(("CBLC", garbage), ("CBDT", [0, 3, 0, 0]))));

        var mixed = Render(MixedRoboto(("CBLC", garbage), ("CBDT", [0, 3, 0, 0])), 'A', new RasterOptions { Size = 20, ColorFont = true })!;
        mixed.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static byte[] BitmapOnly(params CbdtStrike[] strikes)
    {
        var (cblc, cbdt) = Cbdt(strikes);
        return BitmapOnlyRoboto(("CBLC", cblc), ("CBDT", cbdt));
    }

    private static NativeRasterizer Load(byte[] font)
    {
        var native = new NativeRasterizer();
        native.LoadFont(font);
        return native;
    }

    private static RasterizedGlyph? Render(byte[] font, char c, RasterOptions options)
    {
        using var native = Load(font);
        return native.RasterizeGlyph(c, options);
    }

    private static int HmtxAdvance(int glyph, float size) =>
        (int)Math.Round(RobotoFace.Value.Hmtx.GetAdvanceWidth(glyph) * (double)(size / 2048f));

    private static byte[] Pixels(byte[] png) => KernSmith.Imaging.PngDecoder.Decode(png).Pixels;
}
