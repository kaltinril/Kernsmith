using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 167 — synthetic bold and italic as font-unit outline transforms, checked on outlines
/// whose exact answer is known.
/// </summary>
public class SyntheticStyleTransformTests
{
    private const float Tan12 = 0.21255656f; // tan(12°)

    // ---- Italic ----------------------------------------------------------------------------

    [Fact]
    public void Italic_ShearsEveryPoint_AboutTheBaseline_AndKeepsTheAdvance()
    {
        var glyph = new DecodedGlyph(Square(0, -200, 1000, 800, clockwise: true), 1200f, new OutlineBounds(0, -200, 1000, 800));

        var italic = ItalicTransform.Instance.Apply(glyph);

        var expected = glyph.Outline.Commands
            .Select(c => c with
            {
                EndX = c.EndX + (c.EndY * Tan12),
                Ctrl1X = c.Ctrl1X + (c.Ctrl1Y * Tan12),
                Ctrl2X = c.Ctrl2X + (c.Ctrl2Y * Tan12),
            })
            .ToArray();
        AssertCommands(italic.Outline.Commands, expected, tolerance: 0.01f);
        italic.AdvanceWidth.ShouldBe(1200f);
        italic.DeclaredBounds.ShouldBeNull();

        // Points on the baseline stay put; the top leans right, the descender left.
        var bounds = OutlineBounds.Tight(italic.Outline);
        bounds.XMin.ShouldBe(-200 * Tan12, 0.01f);
        bounds.XMax.ShouldBe(1000 + (800 * Tan12), 0.01f);
    }

    [Fact]
    public void Italic_ShearsCubicControlPoints_Too()
    {
        var glyph = new DecodedGlyph(Circle(500, 500, 400, clockwise: true), 1000f);

        var italic = ItalicTransform.Instance.Apply(glyph);

        var expected = glyph.Outline.Commands
            .Select(c => c with
            {
                EndX = c.EndX + (c.EndY * Tan12),
                Ctrl1X = c.Ctrl1X + (c.Ctrl1Y * Tan12),
                Ctrl2X = c.Ctrl2X + (c.Ctrl2Y * Tan12),
            })
            .ToArray();
        AssertCommands(italic.Outline.Commands, expected, tolerance: 0.01f);
    }

    // ---- Bold ------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bold_GrowsASquare_ByTheFullStrength_KeepingTheLowerLeftCorner(bool clockwise)
    {
        // FreeType's FT_Outline_EmboldenXY: every edge moves out by strength/2, then the whole
        // outline shifts up and right by strength/2 — so the left and bottom edges stay put
        // and the ink grows by the full strength to the right and upwards. Winding direction
        // must not matter.
        var glyph = new DecodedGlyph(Square(0, 0, 1000, 1000, clockwise), 1200f, new OutlineBounds(0, 0, 1000, 1000));

        var bold = new BoldTransform(100f).Apply(glyph);

        OutlineBounds.Tight(bold.Outline).ShouldBe(new OutlineBounds(0, 0, 1100, 1100));
        bold.AdvanceWidth.ShouldBe(1300f);
        bold.DeclaredBounds.ShouldBeNull();
        bold.Outline.Commands[0].Type.ShouldBe(OutlineCommandType.MoveTo);
        bold.Outline.Commands[^1].Type.ShouldBe(OutlineCommandType.Close);
        bold.Outline.Commands[^1].EndX.ShouldBe(bold.Outline.Commands[0].EndX);
        bold.Outline.Commands[^1].EndY.ShouldBe(bold.Outline.Commands[0].EndY);
    }

    [Fact]
    public void Bold_ShrinksCounters_WhileThickeningStems()
    {
        // An 'O'-like square ring: outer contour clockwise, counter counter-clockwise, as
        // TrueType draws them. Stems 300 wide, counter 400 wide.
        var outer = Square(0, 0, 1000, 1000, clockwise: true).Commands;
        var inner = Square(300, 300, 700, 700, clockwise: false).Commands;
        var glyph = new DecodedGlyph(GlyphOutline.FromCommands([.. outer, .. inner]), 1000f);

        var bold = new BoldTransform(100f).Apply(glyph);

        var contours = SplitContours(bold.Outline);
        contours.Count.ShouldBe(2);
        OutlineBounds.Tight(contours[0]).ShouldBe(new OutlineBounds(0, 0, 1100, 1100));

        // The counter closes by the full strength (stays open), and shifts with the outline.
        OutlineBounds.Tight(contours[1]).ShouldBe(new OutlineBounds(400, 400, 700, 700));
    }

    [Fact]
    public void Bold_GrowsACircle_ByTheStrength_WithoutDistortingIt()
    {
        var glyph = new DecodedGlyph(Circle(500, 500, 400, clockwise: true), 1000f);

        var bold = new BoldTransform(100f).Apply(glyph);

        var bounds = OutlineBounds.Tight(bold.Outline);
        (bounds.XMax - bounds.XMin).ShouldBe(900f, 0.5f);
        (bounds.YMax - bounds.YMin).ShouldBe(900f, 0.5f);

        // Centre moves by strength/2 up and right, like every FreeType-emboldened outline.
        ((bounds.XMin + bounds.XMax) / 2).ShouldBe(550f, 0.5f);
        ((bounds.YMin + bounds.YMax) / 2).ShouldBe(550f, 0.5f);

        // Still a circle: the area is that of radius 450 to within the cubic offset error.
        Area(bold.Outline).ShouldBe(Math.PI * 450 * 450, Math.PI * 450 * 450 * 0.01);
    }

    [Fact]
    public void Bold_DoesNotSpike_AtAnAcuteApex()
    {
        // A sliver with a ~6° apex. A plain miter join would push the apex out by
        // strength / sin(3°) ≈ 19× the strength; FreeType's rule leaves turns sharper than
        // ~160° unmitered, so the apex only takes the uniform shift.
        var sliver = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0, 0),
            OutlineCommand.Line(50, 1000),
            OutlineCommand.Line(100, 0),
            OutlineCommand.CloseAt(0, 0),
        ]);

        var bold = new BoldTransform(100f).Apply(new DecodedGlyph(sliver, 100f));

        var bounds = OutlineBounds.Tight(bold.Outline);
        bounds.YMax.ShouldBeLessThanOrEqualTo(1000f + 100f);
        foreach (var command in bold.Outline.Commands)
        {
            float.IsFinite(command.EndX).ShouldBeTrue();
            float.IsFinite(command.EndY).ShouldBeTrue();
        }
    }

    [Fact]
    public void Bold_HandlesCoincidentControlPoints()
    {
        // A cubic whose first control sits on its start point (zero-length control leg) must
        // not produce NaN, and must still be emboldened.
        var outline = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0, 0),
            OutlineCommand.Line(0, 1000),
            OutlineCommand.Cubic(0, 1000, 1000, 1000, 1000, 0),
            OutlineCommand.CloseAt(0, 0),
        ]);

        var bold = new BoldTransform(100f).Apply(new DecodedGlyph(outline, 1000f));

        foreach (var command in bold.Outline.Commands)
        {
            float.IsFinite(command.EndX).ShouldBeTrue();
            float.IsFinite(command.EndY).ShouldBeTrue();
            float.IsFinite(command.Ctrl1X).ShouldBeTrue();
            float.IsFinite(command.Ctrl1Y).ShouldBeTrue();
        }

        var bounds = OutlineBounds.Tight(bold.Outline);
        bounds.XMin.ShouldBe(0f, 0.01f);
        bounds.XMax.ShouldBe(1100f, 0.01f);
    }

    [Fact]
    public void Bold_GrowsTheAdvance_OfABlankGlyph()
    {
        // FreeType grows the advance even when there is no outline (a space).
        var bold = new BoldTransform(85f).Apply(new DecodedGlyph(GlyphOutline.Empty, 500f));

        bold.Outline.IsEmpty.ShouldBeTrue();
        bold.AdvanceWidth.ShouldBe(585f);
    }

    // ---- Strength --------------------------------------------------------------------------

    [Theory]
    // FreeType: strength = max(32, y_ppem * 64 / 24) in 26.6, y_ppem the rounded pixel size.
    [InlineData(32f, 72, 85f * 2048 / 64 / 32)]          // 85/64 px
    [InlineData(48f, 72, 128f * 2048 / 64 / 48)]         // 2 px
    [InlineData(6f, 72, 32f * 2048 / 64 / 6)]            // the 0.5 px floor
    [InlineData(17.5f, 72, 48f * 2048 / 64 / 17.5f)]     // y_ppem rounds to 18
    [InlineData(32f, 96, 114f * 2048 / 64 / (32f * 96 / 72))] // y_ppem 43
    public void BoldStrength_MatchesFreeTypesEmbolden_InFontUnits(float size, int dpi, float expected)
    {
        BoldTransform.StrengthInFontUnits(new RasterOptions { Size = size, Dpi = dpi }, 2048)
            .ShouldBe(expected, 0.001f);
    }

    // ---- Transform list --------------------------------------------------------------------

    [Fact]
    public void TransformList_HoldsBold_WithTheFreeTypeStrength()
    {
        var options = new RasterOptions { Size = 32, Bold = true };

        var transforms = GlyphTransforms.For(options, 2048);

        transforms.Count.ShouldBe(1);
        transforms[0].ShouldBeOfType<BoldTransform>().Strength
            .ShouldBe(BoldTransform.StrengthInFontUnits(options, 2048));
    }

    [Fact]
    public void TransformList_HoldsItalic()
    {
        var transforms = GlyphTransforms.For(new RasterOptions { Size = 32, Italic = true }, 2048);

        transforms.Count.ShouldBe(1);
        transforms[0].ShouldBeOfType<ItalicTransform>();
    }

    [Fact]
    public void TransformList_EmboldensBeforeShearing()
    {
        // FreeType's order: the expansion is uniform, then the whole result is slanted.
        var transforms = GlyphTransforms.For(new RasterOptions { Size = 32, Bold = true, Italic = true }, 2048);

        transforms.Count.ShouldBe(2);
        transforms[0].ShouldBeOfType<BoldTransform>();
        transforms[1].ShouldBeOfType<ItalicTransform>();
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>An axis-aligned square; clockwise (Y up) is TrueType's outer-contour direction.</summary>
    internal static GlyphOutline Square(float x0, float y0, float x1, float y1, bool clockwise)
    {
        OutlineCommand[] commands = clockwise
            ?
            [
                OutlineCommand.Move(x0, y0),
                OutlineCommand.Line(x0, y1),
                OutlineCommand.Line(x1, y1),
                OutlineCommand.Line(x1, y0),
                OutlineCommand.CloseAt(x0, y0),
            ]
            :
            [
                OutlineCommand.Move(x0, y0),
                OutlineCommand.Line(x1, y0),
                OutlineCommand.Line(x1, y1),
                OutlineCommand.Line(x0, y1),
                OutlineCommand.CloseAt(x0, y0),
            ];

        return GlyphOutline.FromCommands(commands);
    }

    /// <summary>A circle from four cubic quarter-arcs.</summary>
    internal static GlyphOutline Circle(float cx, float cy, float r, bool clockwise)
    {
        const float K = 0.5522847f;
        float k = K * r;

        // Counter-clockwise from the rightmost point.
        OutlineCommand[] ccw =
        [
            OutlineCommand.Move(cx + r, cy),
            OutlineCommand.Cubic(cx + r, cy + k, cx + k, cy + r, cx, cy + r),
            OutlineCommand.Cubic(cx - k, cy + r, cx - r, cy + k, cx - r, cy),
            OutlineCommand.Cubic(cx - r, cy - k, cx - k, cy - r, cx, cy - r),
            OutlineCommand.Cubic(cx + k, cy - r, cx + r, cy - k, cx + r, cy),
            OutlineCommand.CloseAt(cx + r, cy),
        ];

        if (!clockwise)
            return GlyphOutline.FromCommands(ccw);

        OutlineCommand[] cw =
        [
            OutlineCommand.Move(cx + r, cy),
            OutlineCommand.Cubic(cx + r, cy - k, cx + k, cy - r, cx, cy - r),
            OutlineCommand.Cubic(cx - k, cy - r, cx - r, cy - k, cx - r, cy),
            OutlineCommand.Cubic(cx - r, cy + k, cx - k, cy + r, cx, cy + r),
            OutlineCommand.Cubic(cx + k, cy + r, cx + r, cy + k, cx + r, cy),
            OutlineCommand.CloseAt(cx + r, cy),
        ];
        return GlyphOutline.FromCommands(cw);
    }

    /// <summary>Splits an outline into one outline per contour.</summary>
    internal static List<GlyphOutline> SplitContours(GlyphOutline outline)
    {
        var contours = new List<GlyphOutline>();
        var current = new List<OutlineCommand>();
        foreach (var command in outline.Commands)
        {
            current.Add(command);
            if (command.Type == OutlineCommandType.Close)
            {
                contours.Add(GlyphOutline.FromCommands([.. current]));
                current.Clear();
            }
        }

        return contours;
    }

    /// <summary>
    /// The unsigned enclosed area of the flattened outline, summed as trapezoids — the
    /// flattener drops horizontal edges, which contribute nothing to a trapezoid sum.
    /// </summary>
    internal static double Area(GlyphOutline outline)
    {
        double total = 0;
        foreach (var edge in OutlineFlattener.Flatten(outline, new OutlineTransform(1f, 0f)))
            total += (edge.X0 + (double)edge.X1) * (edge.Y1 - (double)edge.Y0) / 2;

        return Math.Abs(total);
    }

    private static void AssertCommands(OutlineCommand[] actual, OutlineCommand[] expected, float tolerance)
    {
        actual.Length.ShouldBe(expected.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            actual[i].Type.ShouldBe(expected[i].Type);
            actual[i].EndX.ShouldBe(expected[i].EndX, tolerance);
            actual[i].EndY.ShouldBe(expected[i].EndY, tolerance);
            if (actual[i].Type != OutlineCommandType.CubicTo)
                continue;

            actual[i].Ctrl1X.ShouldBe(expected[i].Ctrl1X, tolerance);
            actual[i].Ctrl1Y.ShouldBe(expected[i].Ctrl1Y, tolerance);
            actual[i].Ctrl2X.ShouldBe(expected[i].Ctrl2X, tolerance);
            actual[i].Ctrl2Y.ShouldBe(expected[i].Ctrl2Y, tolerance);
        }
    }
}
