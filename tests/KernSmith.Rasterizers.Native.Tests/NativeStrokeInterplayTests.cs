using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticBitmapTables;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 168's vector outline, <see cref="RasterOptions.WidthPercent"/> and
/// <see cref="RasterOptions.ItalicAngle"/> against the features that landed beside them: color
/// bitmap glyphs (172), LCD options (173) and the DPI-zero default.
/// </summary>
public class NativeStrokeInterplayTests
{
    private static readonly int GlyphA = RobotoGlyph('A');

    private static readonly byte[] Distinct = Png(4, 3, (x, y) => ((byte)(x * 60), (byte)(y * 100), (byte)(200 - (x * 20)), (byte)(255 - (y * 50))));

    private static NativeRasterizer Load(byte[] font)
    {
        var native = new NativeRasterizer();
        native.LoadFont(font);
        return native;
    }

    private static byte[] BitmapOnly()
    {
        var (cblc, cbdt) = Cbdt(new CbdtStrike(20, 1, 17, [new CbdtGlyph(GlyphA, Distinct, 4, 3, 2, 5, 7)]));
        return BitmapOnlyRoboto(("CBLC", cblc), ("CBDT", cbdt));
    }

    private static int HmtxAdvance(int glyph, float size) =>
        (int)Math.Round(RobotoFace.Value.Hmtx.GetAdvanceWidth(glyph) * (double)(size / 2048f));

    // ---- Vector outline on faces with color bitmap glyphs ------------------------------------

    [Fact]
    public void VectorLayer_OnAFaceWithBitmapColorGlyphs_IsDeclinedForEveryGlyph_WhenColorIsOn()
    {
        // 'A' has an sbix bitmap; 'C' only its outline. Like a COLR face, the whole face keeps
        // core's bitmap outline effect, so the bitmap glyph and its outline-only neighbours match.
        var sbix = Sbix(RobotoGlyphCount, new SbixStrike(20, 72, [new SbixEntry(GlyphA, 0, 0, "png ", SolidPng(4, 4, 0, 255, 0))]));
        using var mixed = Load(MixedRoboto(("sbix", sbix)));
        IRasterizer r = mixed;

        r.RasterizeOutlineLayer('A', new RasterOptions { Size = 20, ColorFont = true }, 2, OutlineShape.Round).ShouldBeNull();
        r.RasterizeOutlineLayer('C', new RasterOptions { Size = 20, ColorFont = true }, 2, OutlineShape.Round)
            .ShouldBeNull("a face with color bitmaps keeps the bitmap outline for every glyph");

        // Without ColorFont the outlines draw, so the vector layer does too.
        r.RasterizeOutlineLayer('C', new RasterOptions { Size = 20 }, 2, OutlineShape.Round).ShouldNotBeNull();
        r.RasterizeOutlineLayer('A', new RasterOptions { Size = 20 }, 2, OutlineShape.Round).ShouldNotBeNull();
    }

    [Fact]
    public void VectorLayer_OnABitmapOnlyFace_IsDeclined_WithOrWithoutColor()
    {
        using var native = Load(BitmapOnly());
        IRasterizer r = native;

        r.RasterizeOutlineLayer('A', new RasterOptions { Size = 20, ColorFont = true }, 2, OutlineShape.Round).ShouldBeNull();
        r.RasterizeOutlineLayer('A', new RasterOptions { Size = 20 }, 2, OutlineShape.Round)
            .ShouldBeNull("a bitmap-only face has no outline to stroke; core keeps the bitmap outline");
    }

    [Fact]
    public void VectorMode_OnABitmapOnlyColorFont_KeepsTheEffectOutline_EndToEnd()
    {
        FontGeneratorOptions Options(OutlineMode mode) => new()
        {
            Size = 20,
            Characters = CharacterSet.FromChars("A"),
            Backend = RasterizerBackend.Native,
            ColorFont = true,
            Outline = 2,
            OutlineMode = mode,
        };

        var effect = BmFont.Generate(BitmapOnly(), Options(OutlineMode.Effect));
        var vector = BmFont.Generate(BitmapOnly(), Options(OutlineMode.Vector));

        vector.GetPngData(0).ShouldBe(effect.GetPngData(0));
        vector.FntText.ShouldBe(effect.FntText);
    }

    // ---- Width and italic angle on bitmap glyphs ---------------------------------------------

    [Theory]
    [InlineData(75, false, false, 12f)]
    [InlineData(150, false, false, 12f)]
    [InlineData(75, true, false, 12f)]
    [InlineData(100, false, true, 30f)]
    [InlineData(60, true, true, -20f)]
    public void WidthPercentAndItalicAngle_LeaveBitmapGlyphsUnchanged_AdvanceIncluded(int widthPercent, bool bold, bool italic, float angle)
    {
        // As synthetic bold and italic do: a bitmap glyph's pixels and hmtx advance stay as they are.
        using var native = Load(BitmapOnly());
        var options = new RasterOptions { Size = 20, ColorFont = true, WidthPercent = widthPercent, Bold = bold, Italic = italic, ItalicAngle = angle };

        var glyph = native.RasterizeGlyph('A', options)!;

        glyph.BitmapData.ShouldBe(KernSmith.Imaging.PngDecoder.Decode(Distinct).Pixels);
        var expected = new GlyphMetrics(BearingX: 2, BearingY: 5, Advance: HmtxAdvance(GlyphA, 20), Width: 4, Height: 3);
        glyph.Metrics.ShouldBe(expected);
        native.GetGlyphMetrics('A', options).ShouldBe(expected);
    }

    [Fact]
    public void WidthPercent_OnABitmapOnlyFace_WithoutColor_LeavesTheAdvance()
    {
        using var native = Load(BitmapOnly());
        var plain = native.RasterizeGlyph('A', new RasterOptions { Size = 20 })!;
        var condensed = native.RasterizeGlyph('A', new RasterOptions { Size = 20, WidthPercent = 50 })!;

        condensed.Metrics.ShouldBe(plain.Metrics);
        condensed.BitmapData.ShouldBe(plain.BitmapData);
    }

    // ---- Width and italic angle with LCD -----------------------------------------------------

    [Fact]
    public void Lcd_RendersTheWidthScaledAndSlantedOutline()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var lcd = new RasterOptions { Size = 48, AntiAlias = AntiAliasMode.Lcd, LcdFilter = LcdFilter.Smooth, LcdSubpixelOrder = LcdSubpixelOrder.Bgr };

        var plain = native.RasterizeGlyph('H', lcd)!;
        var condensed = native.RasterizeGlyph('H', lcd with { WidthPercent = 60 })!;
        var grayCondensed = native.RasterizeGlyph('H', new RasterOptions { Size = 48, WidthPercent = 60 })!;

        condensed.Format.ShouldBe(PixelFormat.Rgba32);
        condensed.Metrics.Advance.ShouldBe(grayCondensed.Metrics.Advance, "LCD keeps the condensed advance");
        condensed.Width.ShouldBeLessThan(plain.Width);
        condensed.Height.ShouldBe(plain.Height);
        native.GetGlyphMetrics('H', lcd with { WidthPercent = 60 }).ShouldBe(condensed.Metrics);

        var italic12 = native.RasterizeGlyph('H', lcd with { Italic = true })!;
        var italic30 = native.RasterizeGlyph('H', lcd with { Italic = true, ItalicAngle = 30f })!;
        italic30.Width.ShouldBeGreaterThan(italic12.Width, "a steeper slant widens the LCD glyph too");
        native.GetGlyphMetrics('H', lcd with { Italic = true, ItalicAngle = 30f }).ShouldBe(italic30.Metrics);
    }

    // ---- Vector outline with LCD, end to end -------------------------------------------------

    private static BmFontResult GenerateLcd(OutlineMode mode, int superSample = 1) =>
        BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("HOAg@"),
            Backend = RasterizerBackend.Native,
            AntiAlias = AntiAliasMode.Lcd,
            LcdFilter = LcdFilter.Smooth,
            Outline = 2,
            OutlineMode = mode,
            SuperSampleLevel = superSample,
        });

    [Fact]
    public void VectorMode_WithLcd_CompositesTheLayer_InTheEffectsCells()
    {
        // The layer is single-channel coverage composited under the LCD body, in the same cells.
        var effect = GenerateLcd(OutlineMode.Effect);
        var vector = GenerateLcd(OutlineMode.Vector);

        foreach (var e in effect.Model.Characters)
        {
            var v = vector.Model.Characters.Single(c => c.Id == e.Id);
            (v.Width, v.Height, v.XOffset, v.YOffset, v.XAdvance).ShouldBe((e.Width, e.Height, e.XOffset, e.YOffset, e.XAdvance));
        }

        vector.GetPngData(0).ShouldNotBe(effect.GetPngData(0), "the vector layer must replace the effect");
    }

    [Fact]
    public void VectorMode_WithLcd_IsNotSuperSampled()
    {
        // LCD renders at the final size whatever SuperSampleLevel says; the vector layer follows.
        var once = GenerateLcd(OutlineMode.Vector);
        var supersampled = GenerateLcd(OutlineMode.Vector, superSample: 2);

        supersampled.GetPngData(0).ShouldBe(once.GetPngData(0));
        foreach (var o in once.Model.Characters)
        {
            var s = supersampled.Model.Characters.Single(c => c.Id == o.Id);
            (s.X, s.Y, s.Width, s.Height, s.XOffset, s.YOffset, s.XAdvance).ShouldBe((o.X, o.Y, o.Width, o.Height, o.XOffset, o.YOffset, o.XAdvance));
        }
    }

    // ---- DPI zero ----------------------------------------------------------------------------

    [Fact]
    public void VectorLayer_DpiZero_Means72()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        IRasterizer r = native;
        var at72 = r.RasterizeOutlineLayer('H', new RasterOptions { Size = 24, Dpi = 72, WidthPercent = 80, Italic = true, ItalicAngle = 20f }, 2, OutlineShape.Round)!;
        var atZero = r.RasterizeOutlineLayer('H', new RasterOptions { Size = 24, Dpi = 0, WidthPercent = 80, Italic = true, ItalicAngle = 20f }, 2, OutlineShape.Round);

        atZero.ShouldNotBeNull();
        atZero.Metrics.ShouldBe(at72.Metrics);
        atZero.BitmapData.ShouldBe(at72.BitmapData);
    }

    [Fact]
    public void WidthAndAngle_DpiZero_Means72()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var at72 = new RasterOptions { Size = 24, Dpi = 72, WidthPercent = 80, Italic = true, ItalicAngle = 20f, AntiAlias = AntiAliasMode.Lcd };
        var atZero = at72 with { Dpi = 0 };

        var expected = native.RasterizeGlyph('H', at72)!;
        var actual = native.RasterizeGlyph('H', atZero)!;
        actual.Metrics.ShouldBe(expected.Metrics);
        actual.BitmapData.ShouldBe(expected.BitmapData);
        native.GetGlyphMetrics('H', atZero).ShouldBe(expected.Metrics);
    }
}
