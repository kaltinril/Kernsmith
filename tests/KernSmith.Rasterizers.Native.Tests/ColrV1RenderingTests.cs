using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColrV1;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 — COLR version 1 paint graphs on Roboto with hand-built tables. At 64px Roboto's
/// 2048-unit em is 32 font units per pixel, and a clip box of (0, 0)–(2048, 2048) gives a 64×64
/// canvas whose pixel (col, row) is centered on font point ((col + ½)·32, 2048 − (row + ½)·32).
/// </summary>
/// <remarks>
/// The COLR spec requires gradient interpolation and compositing on linear-light, premultiplied
/// values, so expected colors here are computed that way (see <see cref="Srgb"/>).
/// </remarks>
public class ColrV1RenderingTests
{
    private const uint Red = 0xFF0000FFu;
    private const uint Blue = 0x0000FFFFu;
    private const uint Slate = 0x4080C0FFu;
    private const int Unit = 32; // font units per pixel at Size 64

    private static readonly RasterOptions Color64 = new() { Size = 64, ColorFont = true };
    private static readonly RasterOptions Plain64 = new() { Size = 64 };

    private static readonly NativeFontFace Roboto = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
    private static readonly int O = Roboto.GetGlyphIndex('O');
    private static readonly int I = Roboto.GetGlyphIndex('I');
    private static readonly int W = Roboto.GetGlyphIndex('W');

    private static readonly Clip FullCanvas = new(0, 0, 0, 0, 2048, 2048);

    private static readonly Line RedToBlue = new(Extend.Pad, new Stop(0, 0), new Stop(1, 1));

    // ---- Solid fills, clip boxes and bounds ------------------------------------------------

    [Fact]
    public void Solid_FillsTheClipBox()
    {
        using var native = Load(Colr(O, new Solid(0), ClipFor(O)), Slate);

        var glyph = native.RasterizeGlyph('O', Color64)!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Metrics.BearingX.ShouldBe(0);
        glyph.Metrics.BearingY.ShouldBe(64);
        glyph.Width.ShouldBe(64);
        glyph.Height.ShouldBe(64);
        glyph.Metrics.Advance.ShouldBe(native.RasterizeGlyph('O', Plain64)!.Metrics.Advance);
        AllPixels(glyph).Where(p => !(p == (0x40, 0x80, 0xC0, 255))).ShouldBeEmpty();
    }

    [Fact]
    public void Solid_AlphaMultipliesThePaletteAlpha_AndColorStaysStraight()
    {
        using var native = Load(Colr(O, new Solid(0, Alpha: 0.5), ClipFor(O)), 0x4080C080u);

        var (r, g, b, a) = Pixel(native.RasterizeGlyph('O', Color64)!, 10, 10);
        (r, g, b).ShouldBe((0x40, 0x80, 0xC0));
        a.ShouldBeInRange(63, 65); // 0x80/255 * 0.5
    }

    [Fact]
    public void Solid_ForegroundEntry_IsBlack()
    {
        using var native = Load(Colr(O, new Solid(Foreground), ClipFor(O)), Red);
        Pixel(native.RasterizeGlyph('O', Color64)!, 5, 5).ShouldBe((0, 0, 0, 255));
    }

    [Fact]
    public void UnboundedGraph_WithoutAClipBox_RendersNothing()
    {
        // A solid fill with no glyph clip and no clip box is unbounded; the spec forbids drawing it.
        using var native = Load(Colr(O, new Solid(0)), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Width.ShouldBe(0);
        glyph.Height.ShouldBe(0);
        glyph.BitmapData.ShouldBeEmpty();
        glyph.Metrics.Advance.ShouldBe(native.RasterizeGlyph('O', Plain64)!.Metrics.Advance);
        native.GetGlyphMetrics('O', Color64).ShouldBe(glyph.Metrics);
    }

    [Fact]
    public void ClipBox_CropsContentDrawnOutsideIt()
    {
        // 'W' is far wider than this 512-unit (16px) square box.
        using var native = Load(Colr(O, new Glyph(W, new Solid(0)), new Clip(O, O, 512, 0, 1024, 512)), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        glyph.Metrics.BearingX.ShouldBe(16);
        glyph.Metrics.BearingY.ShouldBe(16);
        glyph.Width.ShouldBe(16);
        glyph.Height.ShouldBe(16);
    }

    // ---- PaintGlyph and PaintColrLayers ----------------------------------------------------

    [Fact]
    public void Glyph_ClipsItsPaintToTheOutline_AndBoundsTheCanvas()
    {
        using var native = Load(Colr(O, new Glyph(O, new Solid(0))), Red);

        var color = native.RasterizeGlyph('O', Color64)!;
        var plain = native.RasterizeGlyph('O', Plain64)!;

        color.Format.ShouldBe(PixelFormat.Rgba32);
        color.Metrics.ShouldBe(plain.Metrics);
        Alpha(color).ShouldBe(plain.BitmapData);
        AllPixels(color).Where(p => p.A > 0).Where(p => !(p.R == 255 && p.G == 0 && p.B == 0)).ShouldBeEmpty();
    }

    [Fact]
    public void Layers_MatchTheVersion0Glyph_InShapeAndInFullyCoveredColors()
    {
        // Alpha composites the same in any color space, and fully covered pixels are a single
        // layer's color either way; only partially covered overlaps differ (linear light).
        using var v1 = Load(Colr(O, new Layers(new Glyph(I, new Solid(1)), new Glyph(O, new Solid(0)))), Red, Blue);
        using var v0 = Load(SyntheticFonts.RobotoWith(
            ("COLR", SyntheticColorTables.Colr((O, [new(I, 1), new(O, 0)]))),
            ("CPAL", SyntheticColorTables.Cpal([[Red, Blue]]))));

        var mine = v1.RasterizeGlyph('O', Color64)!;
        var reference = v0.RasterizeGlyph('O', Color64)!;

        mine.Metrics.ShouldBe(reference.Metrics);
        var a = Alpha(mine);
        var b = Alpha(reference);
        for (int i = 0; i < a.Length; i++)
            ((int)a[i]).ShouldBeInRange(b[i] - 1, b[i] + 1);
        for (int i = 0; i < mine.BitmapData.Length; i += 4)
        {
            if (reference.BitmapData[i + 3] == 255 && IsPrimary(reference, i))
                mine.BitmapData.AsSpan(i, 4).ToArray().ShouldBe(reference.BitmapData.AsSpan(i, 4).ToArray());
        }
    }

    [Fact]
    public void AntiAliasNone_ThresholdsTheGlyphClip()
    {
        using var native = Load(Colr(O, new Glyph(O, new Solid(0))), Red);

        var glyph = native.RasterizeGlyph('O', Color64 with { AntiAlias = AntiAliasMode.None })!;
        Alpha(glyph).ShouldAllBe(a => a == 0 || a == 255);
    }

    // ---- Gradients -------------------------------------------------------------------------

    [Fact]
    public void LinearGradient_InterpolatesInLinearLight_AlongP0P1()
    {
        using var native = Load(Colr(O, new Linear(RedToBlue, 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        foreach (int col in new[] { 0, 7, 20, 32, 45, 63 })
        {
            double t = X(col) / 2048.0;
            foreach (int row in new[] { 0, 31, 63 })
                ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, t));
        }
    }

    [Fact]
    public void LinearGradient_P2_SetsTheDirectionColorsAreProjectedIn()
    {
        // With p2 on the diagonal, color is constant along 45° lines: t = (x − y) / 2048.
        using var native = Load(Colr(O, new Linear(RedToBlue, 0, 0, 2048, 0, 2048, 2048), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        foreach (var (col, row) in new[] { (40, 10), (50, 30), (63, 60), (32, 0), (10, 10) })
        {
            double t = Math.Max(0, (X(col) - Y(row)) / 2048.0);
            ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, t));
        }
    }

    [Theory]
    [InlineData(0, 40, 1.0)]
    [InlineData(1, 40, 0.53125)]
    [InlineData(2, 40, 0.46875)]
    [InlineData(0, 8, 0.0)]
    [InlineData(1, 8, 0.53125)]
    [InlineData(2, 8, 0.46875)]
    public void ColorLine_ExtendModes(int extend, int col, double expectedT)
    {
        // The color line covers x = 512..1024; column 40 is x = 1296 (t = 1.53125) and column 8
        // is x = 272 (t = −0.46875).
        var line = new Line((Extend)extend, new Stop(0, 0), new Stop(1, 1));
        using var native = Load(Colr(O, new Linear(line, 512, 0, 1024, 0, 512, 2048), ClipFor(O)), Red, Blue);

        ShouldBeClose(Pixel(native.RasterizeGlyph('O', Color64)!, col, 20), Mix(Red, Blue, expectedT));
    }

    [Fact]
    public void ColorLine_StopsAreSortedByOffset_AndEqualOffsetsMakeAHardEdge()
    {
        var line = new Line(Extend.Pad, new Stop(1, 1), new Stop(0.5, 1), new Stop(0.5, 0), new Stop(0, 0));
        using var native = Load(Colr(O, new Linear(line, 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        // Sorted (stable): red@0, blue@0.5 (font order), red@0.5, blue@1. Below 0.5 the first
        // stop at 0.5 (blue) is used; at and above it, the last (red).
        ShouldBeClose(Pixel(glyph, 31, 5), Mix(Red, Blue, X(31) / 1024.0));
        ShouldBeClose(Pixel(glyph, 32, 5), Mix(Red, Blue, (X(32) - 1024) / 1024.0));
    }

    [Fact]
    public void ColorLine_TranslucentStops_InterpolatePremultiplied()
    {
        // Red fading to transparent blue: premultiplied, the blue never shows.
        var line = new Line(Extend.Pad, new Stop(0, 0), new Stop(1, 1, Alpha: 0));
        using var native = Load(Colr(O, new Linear(line, 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        for (int col = 0; col < 64; col += 7)
        {
            var (r, g, b, a) = Pixel(glyph, col, 10);
            ((int)a).ShouldBeInRange((int)Math.Round((1 - (X(col) / 2048.0)) * 255) - 1, (int)Math.Round((1 - (X(col) / 2048.0)) * 255) + 1);
            (r, g, b).ShouldBe((255, 0, 0));
        }
    }

    [Fact]
    public void ColorLine_OneStop_IsSolid_AndNoStopsIsTransparent()
    {
        using var one = Load(Colr(O, new Linear(new Line(Extend.Repeat, new Stop(0.3, 1)), 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        AllPixels(one.RasterizeGlyph('O', Color64)!).Where(p => !(p == (0, 0, 255, 255))).ShouldBeEmpty();

        using var none = Load(Colr(O, new Linear(new Line(Extend.Pad), 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        AllPixels(none.RasterizeGlyph('O', Color64)!).Where(p => !(p.A == 0)).ShouldBeEmpty();
    }

    [Fact]
    public void ColorLine_AllStopsAtOneOffset_IsTwoColorsWhenPadded_AndNothingOtherwise()
    {
        var pad = new Line(Extend.Pad, new Stop(0.5, 0), new Stop(0.5, 1));
        using var padded = Load(Colr(O, new Linear(pad, 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        var glyph = padded.RasterizeGlyph('O', Color64)!;
        Pixel(glyph, 31, 0).ShouldBe((255, 0, 0, 255));
        Pixel(glyph, 32, 0).ShouldBe((0, 0, 255, 255));

        var repeat = pad with { Extend = Extend.Repeat };
        using var repeated = Load(Colr(O, new Linear(repeat, 0, 0, 2048, 0, 0, 2048), ClipFor(O)), Red, Blue);
        AllPixels(repeated.RasterizeGlyph('O', Color64)!).Where(p => !(p.A == 0)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 0, 2048, 0, 0, 0)]       // p2 == p0
    [InlineData(0, 0, 0, 0, 0, 2048)]       // p1 == p0
    [InlineData(0, 0, 2048, 0, 1024, 0)]    // p0p2 parallel to p0p1
    public void LinearGradient_IllFormed_RendersNothing(int x0, int y0, int x1, int y1, int x2, int y2)
    {
        using var native = Load(Colr(O, new Linear(RedToBlue, x0, y0, x1, y1, x2, y2), ClipFor(O)), Red, Blue);
        AllPixels(native.RasterizeGlyph('O', Color64)!).Where(p => !(p.A == 0)).ShouldBeEmpty();
    }

    [Fact]
    public void RadialGradient_Concentric_TIsTheDistanceOverTheRadius()
    {
        using var native = Load(Colr(O, new Radial(RedToBlue, 1024, 1024, 0, 1024, 1024, 1024), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        foreach (var (col, row) in new[] { (32, 32), (40, 20), (10, 50), (63, 0), (5, 31) })
        {
            double t = Math.Min(1, Math.Sqrt(Math.Pow(X(col) - 1024, 2) + Math.Pow(Y(row) - 1024, 2)) / 1024);
            ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, t));
        }
    }

    [Fact]
    public void RadialGradient_EqualRadii_PaintsOnlyTheStripBetweenTheCircles()
    {
        // Circles of radius 256 at x = 512 and x = 1536: a horizontal strip |y − 1024| < 256.
        using var native = Load(Colr(O, new Radial(RedToBlue, 512, 1024, 256, 1536, 1024, 256), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        // Inside: the largest ω with |q − c(ω)| = 256, i.e. ω = (x − 512 + √(256² − dy²)) / 1024.
        foreach (var (col, row) in new[] { (32, 31), (20, 28), (45, 34) })
        {
            double dy = Y(row) - 1024;
            double t = Math.Clamp((X(col) - 512 + Math.Sqrt((256 * 256) - (dy * dy))) / 1024, 0, 1);
            ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, t));
        }

        Pixel(glyph, 32, 10).A.ShouldBe(0);
        Pixel(glyph, 32, 55).A.ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(300, 300)]
    public void RadialGradient_Degenerate_RendersNothing(int r0, int r1)
    {
        // r0 = r1 = 0 always paints nothing; identical circles likewise.
        int x1 = r0 == 0 ? 1536 : 1024;
        using var native = Load(Colr(O, new Radial(RedToBlue, 1024, 1024, r0, x1, 1024, r1), ClipFor(O)), Red, Blue);
        AllPixels(native.RasterizeGlyph('O', Color64)!).Where(p => !(p.A == 0)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-1.0, 1.0, false)]  // 0° → 360°
    [InlineData(1.0, -1.0, true)]   // 360° → 0°: the color line runs clockwise
    public void SweepGradient_RunsCounterClockwiseFromStartToEnd(double start, double end, bool inverted)
    {
        using var native = Load(Colr(O, new Sweep(RedToBlue, 1024, 1024, start, end), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        foreach (var (col, row) in new[] { (50, 20), (20, 10), (10, 40), (40, 60), (60, 33) })
        {
            double degrees = Math.Atan2(Y(row) - 1024, X(col) - 1024) * 180 / Math.PI;
            if (degrees < 0)
                degrees += 360;
            double t = degrees / 360;
            ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, inverted ? 1 - t : t));
        }
    }

    [Fact]
    public void SweepGradient_PartialRange_PadsOutsideIt()
    {
        // 90° (0.5 − 1 = −0.5) to 180° (0.0).
        using var native = Load(Colr(O, new Sweep(RedToBlue, 1024, 1024, -0.5, 0.0), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        Pixel(glyph, 50, 13).ShouldBe((255, 0, 0, 255), "45° is before the start: padded with the first stop");
        Pixel(glyph, 60, 33).ShouldBe((0, 0, 255, 255), "357° is past the end: padded with the last stop");
        ShouldBeClose(Pixel(glyph, 13, 13), Mix(Red, Blue, 0.5), "135° is halfway");
    }

    // ---- Transforms ------------------------------------------------------------------------

    [Fact]
    public void Translate_MovesTheGlyphClipByWholePixels()
    {
        using var native = Load(Colr(O, new Translate(2 * Unit, -3 * Unit, new Glyph(I, new Solid(0)))), Red);

        var moved = native.RasterizeGlyph('O', Color64)!;
        var plainI = native.RasterizeGlyph('I', Plain64)!;

        moved.Metrics.BearingX.ShouldBe(plainI.Metrics.BearingX + 2);
        moved.Metrics.BearingY.ShouldBe(plainI.Metrics.BearingY - 3);
        moved.Width.ShouldBe(plainI.Width);
        moved.Height.ShouldBe(plainI.Height);
        Alpha(moved).ShouldBe(plainI.BitmapData);
    }

    [Fact]
    public void Rotate_AroundCenter_IsCounterClockwise()
    {
        // A gradient along +x turned 90° about the canvas center runs along +y: t = y / 2048.
        var gradient = new Linear(RedToBlue, 0, 0, 2048, 0, 0, 2048);
        using var native = Load(Colr(O, new Rotate(0.5, gradient, (1024, 1024)), ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        foreach (var (col, row) in new[] { (5, 5), (30, 20), (60, 50) })
            ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, Y(row) / 2048.0));
    }

    public static TheoryData<string> TransformCases => new()
    {
        "translate", "transform", "scale", "scaleAroundCenter", "scaleUniform", "scaleUniformAroundCenter",
        "rotate", "skewX", "skewXAroundCenter", "skewY",
    };

    [Theory]
    [MemberData(nameof(TransformCases))]
    public void Transforms_MapTheGradient(string name)
    {
        var alongX = new Linear(RedToBlue, 0, 0, 2048, 0, 0, 2048);
        var alongY = new Linear(RedToBlue, 0, 0, 0, 2048, 2048, 0);
        (Paint, Func<double, double, double>) selected = name switch
        {
            "translate" => (new Translate(512, 0, alongX), (x, y) => (x - 512) / 2048),
            "transform" => (new Transform(0.5, 0, 0, 1, 512, 0, alongX), (x, y) => (x - 512) / 1024),
            "scale" => (new Scale(0.5, 1, alongX), (x, y) => x / 1024),
            "scaleAroundCenter" => (new Scale(0.5, 1.5, alongX, (1024, 1024)), (x, y) => (x - 512) / 1024),
            "scaleUniform" => (new ScaleUniform(0.5, alongX), (x, y) => x / 1024),
            "scaleUniformAroundCenter" => (new ScaleUniform(0.5, alongX, (1024, 1024)), (x, y) => (x - 512) / 1024),
            "rotate" => (new Rotate(-0.5, alongY), (x, y) => x / 2048),            // 90° clockwise: +y becomes +x
            "skewX" => (new Skew(0.25, 0, alongX), (x, y) => (x + y) / 2048),      // xy = −tan 45°
            "skewXAroundCenter" => (new Skew(0.25, 0, alongX, (1024, 1024)), (x, y) => (x + y - 1024) / 2048),
            "skewY" => (new Skew(0, 0.25, alongY), (x, y) => (y - x) / 2048),      // yx = tan 45°
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        var (paint, t) = selected;

        using var native = Load(Colr(O, paint, ClipFor(O)), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;

        foreach (var (col, row) in new[] { (3, 60), (20, 40), (33, 33), (50, 12), (61, 2) })
            ShouldBeClose(Pixel(glyph, col, row), Mix(Red, Blue, Math.Clamp(t(X(col), Y(row)), 0, 1)), name);
    }

    [Fact]
    public void VariableForms_WithoutVariationData_RenderLikeTheStaticForms()
    {
        var g = new Glyph(O, new Linear(RedToBlue, 0, 0, 2048, 0, 0, 2048));
        Paint[] statics =
        [
            new Translate(100, 50, g), new Scale(0.75, 1.25, g, (900, 700)), new ScaleUniform(0.8, g),
            new Rotate(0.1, g, (1000, 800)), new Skew(0.1, -0.05, g, (300, 400)),
            new Transform(0.9, 0.1, -0.2, 1.1, 30, 40, g),
        ];

        foreach (var paint in statics)
        {
            var variable = paint switch
            {
                Translate t => (Paint)(t with { Var = 0 }),
                Scale s => s with { Var = 0 },
                ScaleUniform s => s with { Var = 0 },
                Rotate r => r with { Var = 0 },
                Skew k => k with { Var = 0 },
                Transform t => t with { Var = 0 },
                _ => throw new InvalidOperationException(),
            };

            using var a = Load(Colr(O, paint), Red, Blue);
            using var b = Load(Colr(O, variable), Red, Blue);
            var expected = a.RasterizeGlyph('O', Color64)!;
            var actual = b.RasterizeGlyph('O', Color64)!;
            actual.Metrics.ShouldBe(expected.Metrics, paint.GetType().Name);
            actual.BitmapData.ShouldBe(expected.BitmapData, paint.GetType().Name);
        }
    }

    // ---- PaintComposite --------------------------------------------------------------------

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)] [InlineData(27)]
    [InlineData(200)] // unrecognized: COMPOSITE_CLEAR
    public void Composite_MatchesTheW3CFormula(int mode)
    {
        const uint Backdrop = 0x3366CCFFu;
        const uint Source = 0xE0A040FFu;
        const double BackdropAlpha = 0.75, SourceAlpha = 0.5;

        var root = new Composite(mode, new Solid(1, SourceAlpha), new Solid(0, BackdropAlpha));
        using var native = Load(Colr(O, root, ClipFor(O)), Backdrop, Source);
        var actual = Pixel(native.RasterizeGlyph('O', Color64)!, 20, 20);

        var expected = W3C.Composite(mode, Srgb.Premultiplied(Source, SourceAlpha), Srgb.Premultiplied(Backdrop, BackdropAlpha));
        ShouldBeClose(actual, Ints(Srgb.ToStraightBytes(expected)), $"mode {mode}");
    }

    [Fact]
    public void Composite_DestOut_CutsTheSourceShapeOutOfTheBackdrop()
    {
        var root = new Composite(8, new Glyph(I, new Solid(1)), new Glyph(O, new Solid(0)));
        using var native = Load(Colr(O, root), Red, Blue);
        var glyph = native.RasterizeGlyph('O', Color64)!;
        var plainO = native.RasterizeGlyph('O', Plain64)!;
        var plainI = native.RasterizeGlyph('I', Plain64)!;

        int checkedPixels = 0;
        for (int row = 0; row < plainO.Height; row++)
        {
            for (int col = 0; col < plainO.Width; col++)
            {
                int x = plainO.Metrics.BearingX + col, y = plainO.Metrics.BearingY - row;
                byte o = plainO.BitmapData[(row * plainO.Width) + col];
                int i = Coverage(plainI, x, y);
                var p = PixelAtPen(glyph, x, y);
                if (o == 255 && i == 255)
                {
                    p.A.ShouldBe(0);
                    checkedPixels++;
                }
                else if (o == 255 && i == 0)
                {
                    p.ShouldBe((255, 0, 0, 255));
                    checkedPixels++;
                }
            }
        }

        checkedPixels.ShouldBeGreaterThan(50);
    }

    // ---- PaintColrGlyph, cycles and malformed paints ---------------------------------------

    [Fact]
    public void ColrGlyph_ReusesAnotherBaseGlyphsPaint()
    {
        var colr = Colr([(O, new ColrGlyph(I)), (I, new Glyph(I, new Solid(0)))]);
        using var native = Load(colr, Red);

        var o = native.RasterizeGlyph('O', Color64)!;
        var i = native.RasterizeGlyph('I', Color64)!;

        o.Width.ShouldBe(i.Width);
        o.Height.ShouldBe(i.Height);
        o.Metrics.BearingX.ShouldBe(i.Metrics.BearingX);
        o.BitmapData.ShouldBe(i.BitmapData);
        o.Metrics.Advance.ShouldBe(native.RasterizeGlyph('O', Plain64)!.Metrics.Advance, "the advance is the base glyph's");
    }

    [Fact]
    public void ColrGlyph_AppliesTheReferencedGlyphsClipBox()
    {
        var colr = Colr([(O, new ColrGlyph(W)), (W, new Glyph(W, new Solid(0)))], [new Clip(W, W, 0, 0, 512, 512)]);
        using var native = Load(colr, Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        glyph.Width.ShouldBeLessThanOrEqualTo(16);
        glyph.Height.ShouldBeLessThanOrEqualTo(16);
        glyph.Width.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Cycle_ThroughColrGlyph_DropsOnlyTheCyclicPaint()
    {
        using var native = Load(Colr(O, new Layers(new Glyph(I, new Solid(0)), new ColrGlyph(O))), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        var plainI = native.RasterizeGlyph('I', Plain64)!;
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Width.ShouldBe(plainI.Width);
        Alpha(glyph).ShouldBe(plainI.BitmapData);
    }

    [Fact]
    public void Cycle_BetweenTwoGlyphs_RendersNothing()
    {
        using var native = Load(Colr([(O, new ColrGlyph(I)), (I, new ColrGlyph(O))]), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.Width.ShouldBe(0);
        glyph.BitmapData.ShouldBeEmpty();
    }

    [Fact]
    public void Cycle_ThroughTheLayerList_DropsOnlyTheCyclicPaint()
    {
        // Layer 1 is a PaintColrLayers over layers 0–1: layer 0 again, then itself (dropped).
        // So 'I' is drawn twice, source-over: alpha = 1 − (1 − coverage)².
        using var native = Load(Colr(O, new Layers(new Glyph(I, new Solid(0)), new RawLayers(0, 2))), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        var coverage = native.RasterizeGlyph('I', Plain64)!.BitmapData;
        var alpha = Alpha(glyph);
        alpha.Length.ShouldBe(coverage.Length);
        for (int i = 0; i < alpha.Length; i++)
        {
            double a = coverage[i] / 255.0;
            int expected = (int)Math.Round((1 - ((1 - a) * (1 - a))) * 255);
            ((int)alpha[i]).ShouldBeInRange(expected - 1, expected + 1);
        }
    }

    [Fact]
    public void InvalidPaints_AreIgnored_AndTheRestRenders()
    {
        var root = new Layers(
            new Glyph(I, new Solid(0)),
            new Raw([200, 0, 0, 0]),                    // unknown format
            new Glyph(O, new Solid(7)),                 // palette entry past the palette
            new Glyph(60000, new Solid(0)),             // glyph past maxp.numGlyphs
            new ColrGlyph(W),                           // no BaseGlyphPaintRecord
            new RawLayers(100, 3),                      // past the end of the LayerList
            new Linear(new Line(Extend.Pad, new Stop(0, 9)), 0, 0, 2048, 0, 0, 2048));  // bad stop color
        using var native = Load(Colr(O, root), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        var plainI = native.RasterizeGlyph('I', Plain64)!;
        glyph.Metrics.BearingX.ShouldBe(plainI.Metrics.BearingX);
        glyph.Width.ShouldBe(plainI.Width);
        Alpha(glyph).ShouldBe(plainI.BitmapData);
    }

    [Fact]
    public void HugeSharedGraph_FallsBackToPlainCoverage()
    {
        // Each level references the one below twice: 2^24 leaves once expanded.
        Paint level = new Glyph(I, new Solid(0));
        for (int i = 0; i < 24; i++)
            level = new Layers(level, level);
        using var native = Load(Colr(O, level), Red);

        var glyph = native.RasterizeGlyph('O', Color64)!;
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.BitmapData.ShouldBe(native.RasterizeGlyph('O', Plain64)!.BitmapData);
    }

    [Fact]
    public void DeeplyNestedGraph_FallsBackToPlainCoverage()
    {
        Paint paint = new Glyph(I, new Solid(0));
        for (int i = 0; i < 200; i++)
            paint = new Translate(0, 0, paint);
        using var native = Load(Colr(O, paint), Red);

        native.RasterizeGlyph('O', Color64)!.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    [Fact]
    public void Version1_TakesPrecedenceOverVersion0_ForTheSameGlyph()
    {
        var colr = Colr([(O, new Glyph(O, new Solid(1)))], v0: [(O, [(O, 0)]), (I, [(I, 0)])]);
        using var native = Load(colr, Red, Blue);

        var o = native.RasterizeGlyph('O', Color64)!;
        AllPixels(o).Where(p => p.A == 255).Where(p => !(p == (0, 0, 255, 255))).ShouldBeEmpty();

        var i = native.RasterizeGlyph('I', Color64)!;
        i.Format.ShouldBe(PixelFormat.Rgba32, "the version 0 record still serves glyphs without a version 1 one");
        AllPixels(i).Where(p => p.A == 255).Where(p => !(p == (255, 0, 0, 255))).ShouldBeEmpty();
    }

    [Fact]
    public void MalformedVersion1Lists_AreIgnored_AndVersion0StillWorks()
    {
        var colr = Colr([(O, new Glyph(O, new Solid(1)))], v0: [(I, [(I, 0)])]);
        int baseList = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(colr.AsSpan(14));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(colr.AsSpan(baseList), 0x00FFFFFF); // record count
        using var native = Load(colr, Red, Blue);

        native.RasterizeGlyph('O', Color64)!.Format.ShouldBe(PixelFormat.Grayscale8);
        native.RasterizeGlyph('I', Color64)!.Format.ShouldBe(PixelFormat.Rgba32);
    }

    // ---- Palettes and metrics --------------------------------------------------------------

    [Fact]
    public void SelectColorPalette_RecolorsPaints()
    {
        var cpal = SyntheticColorTables.Cpal([[Red], [Blue]]);
        using var native = new NativeRasterizer();
        native.LoadFont(SyntheticFonts.RobotoWith(("COLR", Colr(O, new Solid(0), ClipFor(O))), ("CPAL", cpal)));

        Pixel(native.RasterizeGlyph('O', Color64)!, 1, 1).ShouldBe((255, 0, 0, 255));
        native.SelectColorPalette(1);
        Pixel(native.RasterizeGlyph('O', Color64)!, 1, 1).ShouldBe((0, 0, 255, 255));
    }

    [Fact]
    public void GetGlyphMetrics_MatchesTheRenderedPaintGraph()
    {
        var root = new Layers(
            new Rotate(0.2, new Glyph(W, new Radial(RedToBlue, 500, 500, 0, 600, 600, 900)), (1000, 700)),
            new Composite(9, new Glyph(I, new Solid(1)), new Scale(1.3, 0.7, new Glyph(O, new Solid(0)))));
        using var native = Load(Colr(O, root), Red, Blue);

        foreach (var options in new[] { Color64, Color64 with { Size = 13 }, Color64 with { Size = 37, AntiAlias = AntiAliasMode.None } })
        {
            var glyph = native.RasterizeGlyph('O', options)!;
            glyph.Format.ShouldBe(PixelFormat.Rgba32);
            glyph.Width.ShouldBeGreaterThan(0);
            native.GetGlyphMetrics('O', options).ShouldBe(glyph.Metrics);
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static Clip ClipFor(int glyph) => FullCanvas with { StartGlyph = glyph, EndGlyph = glyph };

    private static NativeRasterizer Load(byte[] colr, params uint[] palette) =>
        Load(SyntheticFonts.RobotoWith(("COLR", colr), ("CPAL", SyntheticColorTables.Cpal([palette]))));

    private static NativeRasterizer Load(byte[] font)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    /// <summary>Font-unit X of a pixel center in the full-canvas clip box.</summary>
    private static double X(int col) => (col + 0.5) * Unit;

    /// <summary>Font-unit Y of a pixel center in the full-canvas clip box.</summary>
    private static double Y(int row) => 2048 - ((row + 0.5) * Unit);

    private static (int R, int G, int B, int A) Mix(uint from, uint to, double t) =>
        Ints(Srgb.ToStraightBytes(Srgb.Lerp(Srgb.Premultiplied(from, 1), Srgb.Premultiplied(to, 1), t)));

    private static (int R, int G, int B, int A) Ints((byte R, byte G, byte B, byte A) c) => (c.R, c.G, c.B, c.A);

    private static void ShouldBeClose((int R, int G, int B, int A) actual, (int R, int G, int B, int A) expected, string? because = null)
    {
        string message = $"{because} expected {expected} but was {actual}";
        actual.R.ShouldBeInRange(expected.R - 2, expected.R + 2, message);
        actual.G.ShouldBeInRange(expected.G - 2, expected.G + 2, message);
        actual.B.ShouldBeInRange(expected.B - 2, expected.B + 2, message);
        actual.A.ShouldBeInRange(expected.A - 1, expected.A + 1, message);
    }

    private static (int R, int G, int B, int A) Pixel(RasterizedGlyph glyph, int col, int row)
    {
        int i = (row * glyph.Pitch) + (col * 4);
        var d = glyph.BitmapData;
        return (d[i], d[i + 1], d[i + 2], d[i + 3]);
    }

    private static (int R, int G, int B, int A) PixelAtPen(RasterizedGlyph glyph, int x, int y)
    {
        int col = x - glyph.Metrics.BearingX;
        int row = glyph.Metrics.BearingY - y;
        return col < 0 || row < 0 || col >= glyph.Width || row >= glyph.Height ? (0, 0, 0, 0) : Pixel(glyph, col, row);
    }

    private static int Coverage(RasterizedGlyph glyph, int x, int y)
    {
        int col = x - glyph.Metrics.BearingX;
        int row = glyph.Metrics.BearingY - y;
        return col < 0 || row < 0 || col >= glyph.Width || row >= glyph.Height ? 0 : glyph.BitmapData[(row * glyph.Width) + col];
    }

    private static List<(int R, int G, int B, int A)> AllPixels(RasterizedGlyph glyph)
    {
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        var pixels = new List<(int R, int G, int B, int A)>();
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
            pixels.Add((glyph.BitmapData[i], glyph.BitmapData[i + 1], glyph.BitmapData[i + 2], glyph.BitmapData[i + 3]));
        return pixels;
    }

    private static bool IsPrimary(RasterizedGlyph glyph, int i) =>
        (glyph.BitmapData[i], glyph.BitmapData[i + 1], glyph.BitmapData[i + 2]) is (255, 0, 0) or (0, 0, 255);

    private static byte[] Alpha(RasterizedGlyph glyph)
    {
        var alpha = new byte[glyph.Width * glyph.Height];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = glyph.BitmapData[(i * 4) + 3];
        return alpha;
    }
}
