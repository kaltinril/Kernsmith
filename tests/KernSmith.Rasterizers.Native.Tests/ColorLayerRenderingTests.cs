using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColorTables;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 — COLR version 0 layer compositing, on Roboto with hand-built <c>COLR</c>/<c>CPAL</c>
/// tables, so every layer's shape is a glyph whose plain coverage is known.
/// </summary>
/// <remarks>
/// Color glyphs come out as straight (non-premultiplied) RGBA, which is what the atlas, the PNG
/// encoder and <c>AtlasPage.GetPremultipliedRgbaPixelData</c> assume. FreeType's color path hands
/// back premultiplied values instead, so comparisons against it premultiply Native's output first.
/// </remarks>
public class ColorLayerRenderingTests
{
    private const uint Red = 0xFF0000FFu;
    private const uint Blue = 0x0000FFFFu;
    private const uint Green = 0x00FF00FFu;
    private const uint Yellow = 0xFFFF00FFu;

    private static readonly RasterOptions Color32 = new() { Size = 32, ColorFont = true };
    private static readonly RasterOptions Plain32 = new() { Size = 32 };

    private static readonly NativeFontFace Roboto = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
    private static readonly int O = Roboto.GetGlyphIndex('O');
    private static readonly int I = Roboto.GetGlyphIndex('I');
    private static readonly int Space = Roboto.GetGlyphIndex(' ');

    // ---- Dispatch --------------------------------------------------------------------------

    [Fact]
    public void ColorGlyph_IsRgba_AndAGlyphWithoutLayersStaysGrayscale()
    {
        using var native = Load(Font([Red], (O, [new(O, 0)])));

        var o = native.RasterizeGlyph('O', Color32)!;
        o.Format.ShouldBe(PixelFormat.Rgba32);
        o.Pitch.ShouldBe(o.Width * 4);
        o.BitmapData.Length.ShouldBe(o.Width * o.Height * 4);

        var colorI = native.RasterizeGlyph('I', Color32)!;
        var plainI = native.RasterizeGlyph('I', Plain32)!;
        colorI.Format.ShouldBe(PixelFormat.Grayscale8);
        colorI.BitmapData.ShouldBe(plainI.BitmapData);
        colorI.Metrics.ShouldBe(plainI.Metrics);
    }

    [Fact]
    public void ColorFontOff_RendersTheBaseOutlineAsGrayscale()
    {
        using var native = Load(Font([Red], (O, [new(I, 0)])));
        using var plain = Load(TestFonts.RobotoRegularBytes());

        var o = native.RasterizeGlyph('O', Plain32)!;
        o.Format.ShouldBe(PixelFormat.Grayscale8);
        o.BitmapData.ShouldBe(plain.RasterizeGlyph('O', Plain32)!.BitmapData);
    }

    // ---- Compositing -----------------------------------------------------------------------

    [Fact]
    public void SingleOpaqueLayer_AlphaIsTheLayerCoverage_AndColorIsStraight()
    {
        const uint Slate = 0x4080C0FFu;
        using var native = Load(Font([Slate], (O, [new(O, 0)])));

        var color = native.RasterizeGlyph('O', Color32)!;
        var coverage = native.RasterizeGlyph('O', Plain32)!;

        color.Metrics.ShouldBe(coverage.Metrics);
        Channel(color, 3).ShouldBe(coverage.BitmapData);

        // Straight alpha: every inked pixel carries the full palette color, edges included.
        bool sawEdge = false;
        for (int i = 0; i < color.BitmapData.Length; i += 4)
        {
            byte a = color.BitmapData[i + 3];
            if (a == 0)
                continue;
            sawEdge |= a < 255;
            color.BitmapData[i].ShouldBe((byte)0x40);
            color.BitmapData[i + 1].ShouldBe((byte)0x80);
            color.BitmapData[i + 2].ShouldBe((byte)0xC0);
        }

        sawEdge.ShouldBeTrue("the glyph should have anti-aliased edge pixels to check");
    }

    [Fact]
    public void Layers_CompositeBottomToTop()
    {
        using var redOnTop = Load(Font([Blue, Red], (O, [new(I, 0), new(O, 1)])));
        using var blueOnTop = Load(Font([Blue, Red], (O, [new(O, 1), new(I, 0)])));
        var (overlap, iOnly) = OverlapAndIOnlyPixels();

        var a = redOnTop.RasterizeGlyph('O', Color32)!;
        var b = blueOnTop.RasterizeGlyph('O', Color32)!;
        a.Metrics.ShouldBe(b.Metrics, "the canvas is the union of the layer boxes either way");

        Pixel(a, overlap).ShouldBe((255, 0, 0, 255));
        Pixel(b, overlap).ShouldBe((0, 0, 255, 255));
        Pixel(a, iOnly).ShouldBe((0, 0, 255, 255));
        Pixel(b, iOnly).ShouldBe((0, 0, 255, 255));
    }

    [Fact]
    public void Canvas_IsTheUnionOfTheLayerBoxes()
    {
        // 'I' sits inside 'O' horizontally; a 'W' layer is wider than both.
        int w = Roboto.GetGlyphIndex('W');
        using var native = Load(Font([Red, Blue], (I, [new(I, 0), new(w, 1)])));

        var union = native.RasterizeGlyph('I', Color32)!;
        var wPlain = native.RasterizeGlyph('W', Plain32)!;
        var iPlain = native.RasterizeGlyph('I', Plain32)!;

        int left = Math.Min(wPlain.Metrics.BearingX, iPlain.Metrics.BearingX);
        int right = Math.Max(wPlain.Metrics.BearingX + wPlain.Width, iPlain.Metrics.BearingX + iPlain.Width);
        int top = Math.Max(wPlain.Metrics.BearingY, iPlain.Metrics.BearingY);
        int bottom = Math.Min(wPlain.Metrics.BearingY - wPlain.Height, iPlain.Metrics.BearingY - iPlain.Height);

        union.Metrics.BearingX.ShouldBe(left);
        union.Width.ShouldBe(right - left);
        union.Metrics.BearingY.ShouldBe(top);
        union.Height.ShouldBe(top - bottom);
        union.Metrics.Advance.ShouldBe(iPlain.Metrics.Advance, "the advance is the base glyph's");
    }

    [Fact]
    public void TranslucentLayer_BlendsSourceOver()
    {
        const uint HalfRed = 0xFF000080u;
        using var native = Load(Font([Blue, HalfRed], (O, [new(I, 0), new(O, 1)])));
        var (overlap, _) = OverlapAndIOnlyPixels();

        // Half red over opaque blue: premultiplied (0.5, 0, 0.5, 1), which is straight too.
        var (r, g, b, a) = Pixel(native.RasterizeGlyph('O', Color32)!, overlap);
        a.ShouldBe(255);
        r.ShouldBeInRange(127, 128);
        g.ShouldBe(0);
        b.ShouldBeInRange(127, 128);
    }

    [Fact]
    public void TranslucentLayerAlone_KeepsItsAlpha_AndStraightColor()
    {
        const uint HalfRed = 0xFF000080u;
        using var native = Load(Font([HalfRed], (O, [new(O, 0)])));

        var glyph = native.RasterizeGlyph('O', Color32)!;
        var coverage = native.RasterizeGlyph('O', Plain32)!.BitmapData;

        for (int p = 0; p < coverage.Length; p++)
        {
            int expectedAlpha = (int)Math.Round(coverage[p] * 128.0 / 255.0);
            ((int)glyph.BitmapData[(p * 4) + 3]).ShouldBeInRange(expectedAlpha - 1, expectedAlpha + 1);
            if (glyph.BitmapData[(p * 4) + 3] != 0)
                glyph.BitmapData[p * 4].ShouldBe((byte)255);
        }
    }

    [Fact]
    public void AntiAliasNone_ThresholdsEachLayer()
    {
        using var native = Load(Font([Red, Blue], (O, [new(O, 0), new(I, 1)])));

        var glyph = native.RasterizeGlyph('O', Color32 with { AntiAlias = AntiAliasMode.None })!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        Channel(glyph, 3).ShouldAllBe(a => a == 0 || a == 255);
    }

    // ---- Palettes and the foreground entry -------------------------------------------------

    [Fact]
    public void SelectColorPalette_SwitchesTheLayerColors()
    {
        using var native = Load(Font([Red], [Green], (O, [new(O, 0)])));

        Solid(native.RasterizeGlyph('O', Color32)!).ShouldBe((255, 0, 0));
        native.SelectColorPalette(1);
        Solid(native.RasterizeGlyph('O', Color32)!).ShouldBe((0, 255, 0));
        native.SelectColorPalette(0);
        Solid(native.RasterizeGlyph('O', Color32)!).ShouldBe((255, 0, 0));
    }

    [Fact]
    public void SelectColorPalette_RejectsAnIndexTheFontDoesNotHave()
    {
        using var native = Load(Font([Red], [Green], (O, [new(O, 0)])));

        Should.Throw<ArgumentOutOfRangeException>(() => native.SelectColorPalette(2));
        Should.Throw<ArgumentOutOfRangeException>(() => native.SelectColorPalette(-1));
    }

    [Fact]
    public void SelectColorPalette_OnAFontWithoutPalettes_Throws()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        Should.Throw<ArgumentOutOfRangeException>(() => native.SelectColorPalette(1))
            .Message.ShouldContain("CPAL", Case.Sensitive);
    }

    [Fact]
    public void SelectColorPalette_BeforeLoadFont_Throws()
    {
        using var native = new NativeRasterizer();
        Should.Throw<InvalidOperationException>(() => native.SelectColorPalette(1));
    }

    [Fact]
    public void ForegroundEntry_IsBlack_ForAVersion0Palette()
    {
        using var native = Load(Font([Red], (O, [new(O, Foreground)])));
        Solid(native.RasterizeGlyph('O', Color32)!).ShouldBe((0, 0, 0));
    }

    [Fact]
    public void ForegroundEntry_IsWhite_WhenTheActivePaletteIsForDarkBackgrounds()
    {
        var cpal = Cpal([[Red], [Green]], [CpalTable.UsableWithLightBackground, CpalTable.UsableWithDarkBackground]);
        using var native = Load(SyntheticFonts.RobotoWith(("COLR", Colr((O, [new(O, Foreground)]))), ("CPAL", cpal)));

        Solid(native.RasterizeGlyph('O', Color32)!).ShouldBe((0, 0, 0), "palette 0 is for light backgrounds");
        native.SelectColorPalette(1);
        Solid(native.RasterizeGlyph('O', Color32)!).ShouldBe((255, 255, 255), "palette 1 is for dark backgrounds");
    }

    [Fact]
    public void ForegroundEntry_MatchesFreeType_ForAVersion0Palette()
    {
        var font = Font([Red, Blue], (O, [new(O, Foreground), new(I, 1)]));
        var options = Color32 with { EnableHinting = false };

        using var native = Load(font);
        var mine = native.RasterizeGlyph('O', options)!;
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(font);
        var reference = freeType.RasterizeGlyph('O', options)!;

        reference.Format.ShouldBe(PixelFormat.Rgba32);
        ColorComparison.MinimumChannelSsim(ColorComparison.Premultiply(mine), reference).ShouldBeGreaterThan(0.95);
    }

    // ---- Blank and invalid layers ----------------------------------------------------------

    [Fact]
    public void EmptyBaseGlyph_StillDrawsItsLayers()
    {
        using var native = Load(Font([Red], (Space, [new(O, 0)])));

        var space = native.RasterizeGlyph(' ', Color32)!;
        var plainO = native.RasterizeGlyph('O', Plain32)!;
        var plainSpace = native.RasterizeGlyph(' ', Plain32)!;

        space.Format.ShouldBe(PixelFormat.Rgba32);
        space.Width.ShouldBe(plainO.Width);
        space.Height.ShouldBe(plainO.Height);
        space.Metrics.BearingX.ShouldBe(plainO.Metrics.BearingX);
        space.Metrics.BearingY.ShouldBe(plainO.Metrics.BearingY);
        space.Metrics.Advance.ShouldBe(plainSpace.Metrics.Advance);
        Channel(space, 3).ShouldBe(plainO.BitmapData);
        native.GetGlyphMetrics(' ', Color32).ShouldBe(space.Metrics);
    }

    [Fact]
    public void LayersThatAreAllBlank_GiveAnEmptyGlyphWithTheBaseAdvance()
    {
        using var native = Load(Font([Red], (O, [new(Space, 0)])));

        var glyph = native.RasterizeGlyph('O', Color32)!;
        glyph.Width.ShouldBe(0);
        glyph.Height.ShouldBe(0);
        glyph.BitmapData.ShouldBeEmpty();
        glyph.Metrics.Advance.ShouldBe(native.RasterizeGlyph('O', Plain32)!.Metrics.Advance);
        native.GetGlyphMetrics('O', Color32).ShouldBe(glyph.Metrics);
    }

    [Fact]
    public void InvalidLayer_EndsTheLayerList_LikeFreeType()
    {
        // Palette entry 5 does not exist: the layers before it still draw.
        using var native = Load(Font([Red, Blue], (O, [new(I, 1), new(O, 5)])));

        var glyph = native.RasterizeGlyph('O', Color32)!;
        var plainI = native.RasterizeGlyph('I', Plain32)!;
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Width.ShouldBe(plainI.Width);
        glyph.Height.ShouldBe(plainI.Height);
        Channel(glyph, 3).ShouldBe(plainI.BitmapData);
        glyph.Metrics.Advance.ShouldBe(native.RasterizeGlyph('O', Plain32)!.Metrics.Advance);
    }

    [Theory]
    [InlineData(5, false)]      // palette entry out of range
    [InlineData(0, true)]       // glyph index past maxp.numGlyphs
    public void InvalidFirstLayer_FallsBackToGrayscale_LikeFreeType(int paletteIndex, bool badGlyph)
    {
        int layerGlyph = badGlyph ? 60000 : O;
        using var native = Load(Font([Red, Blue], (O, [new(layerGlyph, paletteIndex), new(I, 1)])));

        var glyph = native.RasterizeGlyph('O', Color32)!;
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.BitmapData.ShouldBe(native.RasterizeGlyph('O', Plain32)!.BitmapData);
    }

    [Fact]
    public void ColrWithoutCpal_IsIgnored()
    {
        using var native = Load(SyntheticFonts.RobotoWith(("COLR", Colr((O, [new(O, 0)])))));
        native.RasterizeGlyph('O', Color32)!.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    [Fact]
    public void MalformedColr_IsIgnored_AndTheFontStillRenders()
    {
        var colr = Colr((O, [new(O, 0)]));
        using var native = Load(SyntheticFonts.RobotoWith(("COLR", colr[..10]), ("CPAL", Cpal([[Red]]))));

        var glyph = native.RasterizeGlyph('O', Color32)!;
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.Width.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void MalformedCpal_IsIgnored_AndTheFontStillRenders()
    {
        var cpal = Cpal([[Red, Blue]]);
        using var native = Load(SyntheticFonts.RobotoWith(("COLR", Colr((O, [new(O, 0)]))), ("CPAL", cpal[..^1])));

        native.RasterizeGlyph('O', Color32)!.Format.ShouldBe(PixelFormat.Grayscale8);
        Should.Throw<ArgumentOutOfRangeException>(() => native.SelectColorPalette(1));
    }

    // ---- Metrics ---------------------------------------------------------------------------

    [Fact]
    public void GetGlyphMetrics_MatchesTheRenderedColorGlyph()
    {
        int w = Roboto.GetGlyphIndex('W');
        using var native = Load(Font([Red, Blue], (O, [new(I, 0), new(w, 1), new(O, 0)])));

        foreach (var options in new[] { Color32, Color32 with { Size = 13 }, Color32 with { AntiAlias = AntiAliasMode.None } })
        {
            var glyph = native.RasterizeGlyph('O', options)!;
            native.GetGlyphMetrics('O', options).ShouldBe(glyph.Metrics);
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static byte[] Font(uint[] palette, params (int BaseGlyph, Layer[] Layers)[] baseGlyphs) =>
        SyntheticFonts.RobotoWith(("COLR", Colr(baseGlyphs)), ("CPAL", Cpal([palette])));

    private static byte[] Font(uint[] palette0, uint[] palette1, params (int BaseGlyph, Layer[] Layers)[] baseGlyphs) =>
        SyntheticFonts.RobotoWith(("COLR", Colr(baseGlyphs)), ("CPAL", Cpal([palette0, palette1])));

    private static NativeRasterizer Load(byte[] font)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    /// <summary>
    /// A pixel (pen-relative, Y up from the baseline) fully inside both 'O' and 'I', and one
    /// fully inside 'I' but outside 'O', found from Roboto's plain coverage.
    /// </summary>
    private static ((int X, int Y) Overlap, (int X, int Y) IOnly) OverlapAndIOnlyPixels()
    {
        using var plain = Load(TestFonts.RobotoRegularBytes());
        var o = plain.RasterizeGlyph('O', Plain32)!;
        var i = plain.RasterizeGlyph('I', Plain32)!;

        (int, int)? overlap = null, iOnly = null;
        for (int row = 0; row < i.Height; row++)
        {
            for (int col = 0; col < i.Width; col++)
            {
                int x = i.Metrics.BearingX + col;
                int y = i.Metrics.BearingY - row;
                if (i.BitmapData[(row * i.Width) + col] != 255)
                    continue;

                int oCoverage = Coverage(o, x, y);
                if (oCoverage == 255)
                    overlap ??= (x, y);
                else if (oCoverage == 0)
                    iOnly ??= (x, y);
            }
        }

        overlap.ShouldNotBeNull("'I' and 'O' should share fully covered pixels");
        iOnly.ShouldNotBeNull("'I' should cross the counter of 'O'");
        return (overlap.Value, iOnly.Value);
    }

    private static int Coverage(RasterizedGlyph glyph, int x, int y)
    {
        int col = x - glyph.Metrics.BearingX;
        int row = glyph.Metrics.BearingY - y;
        return col < 0 || row < 0 || col >= glyph.Width || row >= glyph.Height
            ? 0
            : glyph.BitmapData[(row * glyph.Width) + col];
    }

    private static (int R, int G, int B, int A) Pixel(RasterizedGlyph glyph, (int X, int Y) at)
    {
        int col = at.X - glyph.Metrics.BearingX;
        int row = glyph.Metrics.BearingY - at.Y;
        int index = (row * glyph.Pitch) + (col * 4);
        var d = glyph.BitmapData;
        return (d[index], d[index + 1], d[index + 2], d[index + 3]);
    }

    /// <summary>The color of the glyph's fully opaque pixels, which must all agree.</summary>
    private static (int R, int G, int B) Solid(RasterizedGlyph glyph)
    {
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        var colors = new HashSet<(int, int, int)>();
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i + 3] == 255)
                colors.Add((glyph.BitmapData[i], glyph.BitmapData[i + 1], glyph.BitmapData[i + 2]));
        }

        colors.Count.ShouldBe(1);
        return colors.Single();
    }

    private static byte[] Channel(RasterizedGlyph glyph, int channel)
    {
        var values = new byte[glyph.Width * glyph.Height];
        for (int p = 0; p < values.Length; p++)
            values[p] = glyph.BitmapData[(p * 4) + channel];
        return values;
    }
}
