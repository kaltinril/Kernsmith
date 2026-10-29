using System.Globalization;
using System.Text;
using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 168 — Native's vector outline layer (<see cref="NativeRasterizer.RasterizeOutlineLayer"/>)
/// and how <see cref="OutlineMode.Vector"/> output compares with core's bitmap outline effect.
/// </summary>
public class NativeVectorOutlineTests
{
    private static NativeRasterizer Load(byte[]? font = null)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font ?? TestFonts.RobotoRegularBytes());
        return rasterizer;
    }

    private static byte At(RasterizedGlyph g, int x, int y) => g.BitmapData[(y * g.Pitch) + x];

    // ---- Capabilities ------------------------------------------------------------------------

    [Fact]
    public void Capabilities_ReportTheLayerWidthAndAngle()
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.Capabilities.SupportsOutlineStroke.ShouldBeTrue();
        rasterizer.Capabilities.SupportsWidthScale.ShouldBeTrue();
        rasterizer.Capabilities.SupportsItalicAngle.ShouldBeTrue();
    }

    // ---- The layer ---------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Layer_IsTheGlyphGrownByTheWidth_OnEverySide(int width)
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        var options = new RasterOptions { Size = 32 };
        var body = r.RasterizeGlyph('H', options)!;
        var layer = r.RasterizeOutlineLayer('H', options, width, OutlineShape.Round)!;

        layer.ShouldNotBeNull();
        layer.Format.ShouldBe(PixelFormat.Grayscale8);
        layer.Metrics.BearingX.ShouldBe(body.Metrics.BearingX - width);
        layer.Metrics.BearingY.ShouldBe(body.Metrics.BearingY + width);
        layer.Width.ShouldBe(body.Width + (2 * width));
        layer.Height.ShouldBe(body.Height + (2 * width));
        layer.Metrics.Advance.ShouldBe(body.Metrics.Advance);

        // Every body pixel is solid in the layer: the fill and the stroke unite, with no seam.
        for (int y = 0; y < body.Height; y++)
        {
            for (int x = 0; x < body.Width; x++)
            {
                if (At(body, x, y) > 0)
                    At(layer, x + width, y + width).ShouldBe((byte)255, $"body pixel ({x},{y})");
            }
        }

        // H's outer stem edges are the body's own edges moved out by exactly the width.
        int row = body.Height / 4;
        Math.Abs(At(layer, 0, row + width) - At(body, 0, row)).ShouldBeLessThanOrEqualTo(1, "left edge");
        Math.Abs(At(layer, layer.Width - 1, row + width) - At(body, body.Width - 1, row)).ShouldBeLessThanOrEqualTo(1, "right edge");
    }

    [Fact]
    public void Layer_Pens_FollowTheOutlineShape()
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        var options = new RasterOptions { Size = 32 };

        // H's top-left corner is a right angle: a round pen leaves the canvas corner empty, a
        // square pen fills it, a diamond (plus) pen cuts it.
        var round = r.RasterizeOutlineLayer('H', options, 4, OutlineShape.Round)!;
        var miter = r.RasterizeOutlineLayer('H', options, 4, OutlineShape.Square)!;
        var bevel = r.RasterizeOutlineLayer('H', options, 4, OutlineShape.Plus)!;

        At(round, 0, 0).ShouldBe((byte)0);
        At(bevel, 0, 0).ShouldBe((byte)0);
        // The square pen fills the corner square, so its pixel is covered by (column) x (row).
        At(miter, 0, 0).ShouldBeGreaterThan((byte)0);
        Math.Abs(At(miter, 0, 0) - (At(miter, 0, 3) * At(miter, 3, 0) / 255.0)).ShouldBeLessThan(6.0, "the square pen fills the corner");
        At(round, 1, 1).ShouldBeGreaterThan(At(bevel, 1, 1), "a round pen reaches further into the corner than a diamond");
    }

    [Theory]
    [InlineData(OutlineShape.Round, 1)]
    [InlineData(OutlineShape.Round, 3)]
    [InlineData(OutlineShape.Square, 2)]
    [InlineData(OutlineShape.Plus, 2)]
    public void Layer_Coverage_MatchesAnEightTimesRender(OutlineShape shape, int width)
    {
        // The same layer drawn 8x larger and box-filtered down is (to an eighth of a pixel) the
        // exact area of each pixel inside the dilation. Filling stroke pieces separately
        // over-darkened edge pixels on the inner side of every curve by up to 166 levels here.
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        int worst = 0;
        foreach (char c in "o@S")
        {
            var layer = r.RasterizeOutlineLayer(c, new RasterOptions { Size = 32 }, width, shape)!;
            var big = r.RasterizeOutlineLayer(c, new RasterOptions { Size = 256 }, width * 8, shape)!;
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < layer.Width; x++)
                {
                    int sum = 0;
                    for (int sy = 0; sy < 8; sy++)
                    {
                        int by = big.Metrics.BearingY - (8 * (layer.Metrics.BearingY - y)) + sy;
                        for (int sx = 0; sx < 8; sx++)
                        {
                            int bx = (8 * (layer.Metrics.BearingX + x)) - big.Metrics.BearingX + sx;
                            if (bx >= 0 && by >= 0 && bx < big.Width && by < big.Height)
                                sum += big.BitmapData[(by * big.Pitch) + bx];
                        }
                    }

                    worst = Math.Max(worst, Math.Abs(At(layer, x, y) - (sum / 64)));
                }
            }
        }

        worst.ShouldBeLessThanOrEqualTo(16);
    }

    [Fact]
    public void Layer_ForAntiAliasNone_IsHardEdged()
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        var layer = r.RasterizeOutlineLayer('O', new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.None }, 2, OutlineShape.Round)!;

        layer.BitmapData.ShouldAllBe(v => v == 0 || v == 255);
        layer.BitmapData.ShouldContain((byte)255);
    }

    [Fact]
    public void Layer_ForLcd_IsSingleChannelCoverage()
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        var layer = r.RasterizeOutlineLayer('O', new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Lcd }, 2, OutlineShape.Round)!;

        layer.Format.ShouldBe(PixelFormat.Grayscale8);
        layer.Pitch.ShouldBe(layer.Width);
    }

    [Fact]
    public void Layer_ForACffOutline_CoversTheBody()
    {
        // CFF outer contours wind the other way from TrueType's; the stroke must still unite
        // with the fill instead of cancelling it.
        using var rasterizer = Load(TestFonts.SourceSerifOtfBytes());
        IRasterizer r = rasterizer;
        var options = new RasterOptions { Size = 32 };
        foreach (char c in "oBg")
        {
            var body = r.RasterizeGlyph(c, options)!;
            var layer = r.RasterizeOutlineLayer(c, options, 2, OutlineShape.Round)!;
            int dx = body.Metrics.BearingX - layer.Metrics.BearingX;
            int dy = layer.Metrics.BearingY - body.Metrics.BearingY;
            for (int y = 0; y < body.Height; y++)
            {
                for (int x = 0; x < body.Width; x++)
                {
                    if (At(body, x, y) == 255)
                        At(layer, x + dx, y + dy).ShouldBe((byte)255, $"'{c}' body pixel ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public void Layer_HeightScale_DrawsTheRingShorterByTheStretch()
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        var options = new RasterOptions { Size = 32 };
        var body = r.RasterizeGlyph('H', options)!;
        var layer = r.RasterizeOutlineLayer('H', options, 4, OutlineShape.Round, heightScale: 2f)!;

        layer.Metrics.BearingX.ShouldBe(body.Metrics.BearingX - 4, "the pen still reaches 4 px sideways");
        layer.Metrics.BearingY.ShouldBe(body.Metrics.BearingY + 2, "and 4 / 2 px vertically");
        layer.Height.ShouldBe(body.Height + 4);
    }

    [Fact]
    public void Layer_ForLight_StrokesTheHintedOutline()
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        var light = new RasterOptions { Size = 13, AntiAlias = AntiAliasMode.Light };
        var body = r.RasterizeGlyph('x', light)!;
        var layer = r.RasterizeOutlineLayer('x', light, 1, OutlineShape.Round)!;
        var unhinted = r.RasterizeGlyph('x', light with { AntiAlias = AntiAliasMode.Grayscale })!;
        body.BitmapData.ShouldNotBe(unhinted.BitmapData, "the request must actually be hinted for this test to mean anything");

        layer.Metrics.BearingY.ShouldBe(body.Metrics.BearingY + 1, "the ring sits on the hinted top edge");
        layer.Height.ShouldBe(body.Height + 2);
    }

    [Fact]
    public void Layer_Declines_DistanceFieldsAndColorFaces()
    {
        using var roboto = Load();
        IRasterizer r = roboto;
        r.RasterizeOutlineLayer('A', new RasterOptions { Size = 32, Sdf = true }, 2, OutlineShape.Round).ShouldBeNull();
        r.RasterizeOutlineLayer(' ', new RasterOptions { Size = 32 }, 2, OutlineShape.Round).ShouldBeNull("a blank glyph has nothing to outline");
        r.RasterizeOutlineLayer(0x10FFFF, new RasterOptions { Size = 32 }, 2, OutlineShape.Round).ShouldBeNull();

        using var bungee = Load(TestFonts.BungeeColorBytes());
        IRasterizer b = bungee;
        b.RasterizeOutlineLayer('A', new RasterOptions { Size = 32, ColorFont = true }, 2, OutlineShape.Round)
            .ShouldBeNull("a color face keeps the bitmap outline for every glyph");
        b.RasterizeOutlineLayer('A', new RasterOptions { Size = 32 }, 2, OutlineShape.Round).ShouldNotBeNull();
    }

    [Fact]
    public void Layer_RejectsANonPositiveWidthOrScale()
    {
        using var rasterizer = Load();
        IRasterizer r = rasterizer;
        Should.Throw<ArgumentOutOfRangeException>(() => r.RasterizeOutlineLayer('A', new RasterOptions { Size = 32 }, 0, OutlineShape.Round));
        Should.Throw<ArgumentOutOfRangeException>(() => r.RasterizeOutlineLayer('A', new RasterOptions { Size = 32 }, 2, OutlineShape.Round, 0f));
    }

    // ---- Vector vs effect, end to end --------------------------------------------------------

    private const string Sample = "HOAgeWs@&8%";

    private static BmFontResult Generate(OutlineMode mode, int width, AntiAliasMode antiAlias = AntiAliasMode.Grayscale, int? shadow = null) =>
        BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(Sample),
            Backend = RasterizerBackend.Native,
            Outline = width,
            OutlineMode = mode,
            AntiAlias = antiAlias,
            ShadowOffsetX = shadow ?? 0,
            ShadowOffsetY = shadow ?? 0,
        });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void VectorMode_KeepsEveryCellAndPlacement_OfTheEffect(int width)
    {
        var effect = Generate(OutlineMode.Effect, width);
        var vector = Generate(OutlineMode.Vector, width);

        foreach (var e in effect.Model.Characters)
        {
            var v = vector.Model.Characters.Single(c => c.Id == e.Id);
            (v.Width, v.Height, v.XOffset, v.YOffset, v.XAdvance)
                .ShouldBe((e.Width, e.Height, e.XOffset, e.YOffset, e.XAdvance), $"'{(char)e.Id}' at {width}px");
        }
    }

    /// <summary>
    /// Vector and effect rings are the same dilation, but the effect measures from pixel centres
    /// and fades over 1.5 px where the vector ring has the body's own exact-area edge, and aliased
    /// rings light slightly different pixels on curves, so SSIM is a floor against gross breakage
    /// (a misplaced or missing layer scores far lower), not a likeness target.
    /// </summary>
    [Theory]
    [InlineData(AntiAliasMode.Grayscale, 0.80)]
    [InlineData(AntiAliasMode.None, 0.65)]
    public void VectorMode_LooksLikeTheEffect(AntiAliasMode antiAlias, double floor)
    {
        var report = new StringBuilder();
        foreach (int width in new[] { 1, 2, 3, 4 })
        {
            var effect = Generate(OutlineMode.Effect, width, antiAlias);
            var vector = Generate(OutlineMode.Vector, width, antiAlias);
            var (ssim, worst, inkDelta) = Compare(effect, vector);
            report.Append(CultureInfo.InvariantCulture, $"{antiAlias} {width}px: mean SSIM {ssim:0.000}, worst {worst.Ssim:0.000} ('{worst.Char}'), max ink-bounds delta {inkDelta}px\n");

            ssim.ShouldBeGreaterThan(floor, report.ToString());
            ssim.ShouldBeLessThan(1.0, "the vector layer must actually replace the effect");
            inkDelta.ShouldBeLessThanOrEqualTo(1, report.ToString());
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }

    [Fact]
    public void VectorMode_WithShadow_CastsTheShadowFromTheVectorSilhouette()
    {
        var effect = Generate(OutlineMode.Effect, 2, shadow: 3);
        var vector = Generate(OutlineMode.Vector, 2, shadow: 3);
        var (ssim, _, inkDelta) = Compare(effect, vector);

        ssim.ShouldBeGreaterThan(0.80);
        inkDelta.ShouldBeLessThanOrEqualTo(1);
        foreach (var e in effect.Model.Characters)
        {
            var v = vector.Model.Characters.Single(c => c.Id == e.Id);
            (v.Width, v.Height, v.XOffset, v.YOffset).ShouldBe((e.Width, e.Height, e.XOffset, e.YOffset));
        }
    }

    /// <summary>Mean and worst per-glyph SSIM of the composited alpha, and the largest move of any ink edge.</summary>
    private static (double Mean, (double Ssim, char Char) Worst, int InkDelta) Compare(BmFontResult a, BmFontResult b)
    {
        double total = 0;
        var worst = (Ssim: 1.0, Char: ' ');
        int inkDelta = 0;
        int count = 0;
        foreach (var ca in a.Model.Characters)
        {
            var cb = b.Model.Characters.Single(c => c.Id == ca.Id);
            if (ca.Width == 0 || cb.Width == 0)
                continue;

            int left = Math.Min(ca.XOffset, cb.XOffset), top = Math.Min(ca.YOffset, cb.YOffset);
            int w = Math.Max(ca.XOffset + ca.Width, cb.XOffset + cb.Width) - left;
            int h = Math.Max(ca.YOffset + ca.Height, cb.YOffset + cb.Height) - top;
            var ia = Canvas(a, ca, left, top, w, h);
            var ib = Canvas(b, cb, left, top, w, h);
            double ssim = ImageSimilarity.Ssim(ia, ib, w, h);
            total += ssim;
            count++;
            if (ssim < worst.Ssim)
                worst = (ssim, (char)ca.Id);

            var ba = Ink(ia, w, h);
            var bb = Ink(ib, w, h);
            inkDelta = Math.Max(inkDelta, new[] { Math.Abs(ba.L - bb.L), Math.Abs(ba.T - bb.T), Math.Abs(ba.R - bb.R), Math.Abs(ba.B - bb.B) }.Max());
        }

        return (total / count, worst, inkDelta);
    }

    private static byte[] Canvas(BmFontResult result, CharEntry c, int left, int top, int w, int h)
    {
        var page = result.Pages[c.Page];
        var canvas = new byte[w * h];
        for (int y = 0; y < c.Height; y++)
        {
            for (int x = 0; x < c.Width; x++)
                canvas[((c.YOffset - top + y) * w) + (c.XOffset - left + x)] = page.PixelData[((((c.Y + y) * page.Width) + c.X + x) * 4) + 3];
        }

        return canvas;
    }

    /// <summary>Bounds of the pixels at least half covered.</summary>
    private static (int L, int T, int R, int B) Ink(byte[] alpha, int w, int h)
    {
        int l = w, t = h, r = -1, b = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (alpha[(y * w) + x] < 128)
                    continue;

                l = Math.Min(l, x);
                r = Math.Max(r, x);
                t = Math.Min(t, y);
                b = Math.Max(b, y);
            }
        }

        return (l, t, r, b);
    }
}
