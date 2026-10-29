using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticStyleTransformTests;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 168 — the outline stroker, checked by rendering the stroke through the real fill at
/// 0.1px per font unit (one pixel = 10 units) and probing pixels whose answer is known.
/// </summary>
public class OutlineStrokerTests
{
    private const float Scale = 0.1f;
    private const float Tolerance = 0.5f;

    // ---- A square: ring, joins ---------------------------------------------------------------

    [Theory]
    [InlineData("Miter")]
    [InlineData("Round")]
    [InlineData("Bevel")]
    public void Square_StrokesARingOfTheRequestedWidth_CentredOnTheContour(string joinName)
    {
        var join = Enum.Parse<StrokeJoin>(joinName);
        var stroke = OutlineStroker.Stroke(Square(0, 0, 1000, 1000, clockwise: true), 100f, join, OutlineStroker.DefaultMiterLimit, Tolerance);
        var image = Render(stroke);

        image.At(500, 500).ShouldBe((byte)0, "the inside of the ring must stay empty");
        image.At(-25, 500).ShouldBe((byte)255, "50 units outside the contour is inside the stroke");
        image.At(25, 500).ShouldBe((byte)255, "50 units inside the contour is inside the stroke");
        image.At(-75, 500).ShouldBe((byte)0);
        image.At(75, 500).ShouldBe((byte)0);
    }

    [Fact]
    public void Square_MiterJoin_FillsTheCorner()
    {
        var image = Render(OutlineStroker.Stroke(Square(0, 0, 1000, 1000, true), 100f, StrokeJoin.Miter, 2f, Tolerance));

        // The pixel spanning (-50..-40, -50..-40) lies in the square miter corner.
        image.At(-45, -45).ShouldBe((byte)255);
    }

    [Fact]
    public void Square_RoundJoin_RoundsTheCorner()
    {
        var image = Render(OutlineStroker.Stroke(Square(0, 0, 1000, 1000, true), 100f, StrokeJoin.Round, 2f, Tolerance));

        image.At(-45, -45).ShouldBe((byte)0, "56+ units from the corner is outside a 50-unit round join");
        image.At(-25, -25).ShouldBe((byte)255, "under 43 units from the corner is inside it");
    }

    [Fact]
    public void Square_BevelJoin_CutsTheCorner()
    {
        var image = Render(OutlineStroker.Stroke(Square(0, 0, 1000, 1000, true), 100f, StrokeJoin.Bevel, 2f, Tolerance));

        // The bevel is the line x + y = -50; this pixel lies wholly beyond it.
        image.At(-35, -35).ShouldBe((byte)0);
        image.At(-15, -15).ShouldBe((byte)255);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stroke_DoesNotDependOnWinding(bool clockwise)
    {
        var reference = Render(OutlineStroker.Stroke(Square(0, 0, 1000, 1000, true), 100f, StrokeJoin.Miter, 2f, Tolerance));
        var image = Render(OutlineStroker.Stroke(Square(0, 0, 1000, 1000, clockwise), 100f, StrokeJoin.Miter, 2f, Tolerance));

        image.Pixels.ShouldBe(reference.Pixels);
    }

    // ---- Miter limit -------------------------------------------------------------------------

    [Fact]
    public void MiterLimit_BevelsAnAcuteApex()
    {
        // A sliver with a ~6° apex: an unlimited miter would reach ~19 half-widths past it.
        var stroke = OutlineStroker.Stroke(Sliver(), 100f, StrokeJoin.Miter, OutlineStroker.DefaultMiterLimit, Tolerance);

        OutlineBounds.Tight(stroke).YMax.ShouldBeLessThanOrEqualTo(1000f + (OutlineStroker.DefaultMiterLimit * 50f));
    }

    [Fact]
    public void MiterLimit_IsWhatStopsTheSpike()
    {
        // Proves the test above can fail: with a generous limit the same apex does spike.
        var stroke = OutlineStroker.Stroke(Sliver(), 100f, StrokeJoin.Miter, 100f, Tolerance);

        OutlineBounds.Tight(stroke).YMax.ShouldBeGreaterThan(1000f + (10 * 50f));
    }

    // ---- Curves ------------------------------------------------------------------------------

    [Fact]
    public void Circle_StrokesAnAnnulus()
    {
        var image = Render(OutlineStroker.Stroke(Circle(500, 500, 400, clockwise: true), 100f, StrokeJoin.Round, 2f, Tolerance));

        // Covered area = π(450² − 350²) units², at 0.1px per unit.
        double expected = Math.PI * ((450.0 * 450) - (350.0 * 350)) * Scale * Scale;
        image.Coverage.ShouldBe(expected, expected * 0.01);
        image.At(500, 500).ShouldBe((byte)0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurveTighterThanHalfTheWidth_FillsSolid(bool clockwise)
    {
        // A circle of radius 30 stroked 100 wide: the inner offset collapses through the centre.
        // A two-sided offset would invert there and leave a hole; the stroke must be a disc.
        // Wound counter-clockwise, its join wedges overlap the edge rectangles, so they only
        // add up if every piece is wound the same way.
        var image = Render(OutlineStroker.Stroke(Circle(0, 0, 30, clockwise), 100f, StrokeJoin.Round, 2f, Tolerance));

        image.At(0, 0).ShouldBe((byte)255);
        double expected = Math.PI * 80 * 80 * Scale * Scale;
        image.Coverage.ShouldBe(expected, expected * 0.02);
    }

    [Fact]
    public void Cusp_IsStrokedWithoutHoles()
    {
        // A cubic whose controls cross makes a cusp at its middle.
        var cusp = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0, 0),
            OutlineCommand.Cubic(1000, 600, 0, 600, 1000, 0),
            OutlineCommand.CloseAt(0, 0),
        ]);

        var image = Render(OutlineStroker.Stroke(cusp, 100f, StrokeJoin.Round, 2f, Tolerance));

        // The cusp point is (500, 450); every point within the half-width is ink.
        image.At(500, 450).ShouldBe((byte)255);
        image.At(500, 480).ShouldBe((byte)255);
        foreach (var command in OutlineStroker.Stroke(cusp, 100f, StrokeJoin.Round, 2f, Tolerance).Commands)
        {
            float.IsFinite(command.EndX).ShouldBeTrue();
            float.IsFinite(command.EndY).ShouldBeTrue();
        }
    }

    [Fact]
    public void RobotoO_KeepsItsCounter_AndGrowsByTheHalfWidth()
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var outline = new TrueTypeOutlineDecoder(face).Decode(face.GetGlyphIndex('O'), new RasterOptions { Size = 32 }).Outline;
        var bounds = OutlineBounds.Tight(outline);

        var stroke = OutlineStroker.Stroke(outline, 64f, StrokeJoin.Round, 2f, Tolerance);
        var strokeBounds = OutlineBounds.Tight(stroke);

        strokeBounds.XMin.ShouldBe(bounds.XMin - 32f, 1f);
        strokeBounds.XMax.ShouldBe(bounds.XMax + 32f, 1f);
        strokeBounds.YMin.ShouldBe(bounds.YMin - 32f, 1f);
        strokeBounds.YMax.ShouldBe(bounds.YMax + 32f, 1f);

        var image = Render(stroke);
        float midX = (bounds.XMin + bounds.XMax) / 2, midY = (bounds.YMin + bounds.YMax) / 2;
        image.At(midX, midY).ShouldBe((byte)0, "the counter of 'O' must stay open");
    }

    // ---- Transform ---------------------------------------------------------------------------

    [Fact]
    public void StrokeTransform_ReplacesTheOutline_AndKeepsTheAdvance()
    {
        var glyph = new DecodedGlyph(Square(0, 0, 1000, 1000, true), 1200f, new OutlineBounds(0, 0, 1000, 1000));

        var stroked = new StrokeTransform(100f, StrokeJoin.Miter, 2f, Tolerance).Apply(glyph);

        stroked.AdvanceWidth.ShouldBe(1200f);
        stroked.DeclaredBounds.ShouldBeNull();
        OutlineBounds.Tight(stroked.Outline).ShouldBe(new OutlineBounds(-50, -50, 1050, 1050));
    }

    [Fact]
    public void StrokeTransform_FromPixels_ConvertsToFontUnits()
    {
        var transform = StrokeTransform.FromPixels(2f, StrokeJoin.Round, new RasterOptions { Size = 32 }, 2048);

        transform.Width.ShouldBe(128f, 0.001f); // 2px at 64 units per pixel
        transform.Tolerance.ShouldBe(3.2f, 0.001f);
        transform.MiterLimit.ShouldBe(OutlineStroker.DefaultMiterLimit);
    }

    [Theory]
    [InlineData(OutlineShape.Round, "Round")]
    [InlineData(OutlineShape.Square, "Miter")]
    [InlineData(OutlineShape.Plus, "Bevel")]
    public void OutlineShape_MapsToTheJoinWithTheSameCorners(OutlineShape shape, string joinName)
    {
        StrokeTransform.JoinFor(shape).ShouldBe(Enum.Parse<StrokeJoin>(joinName));
    }

    [Fact]
    public void Stroke_RejectsANonPositiveWidth()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OutlineStroker.Stroke(Square(0, 0, 10, 10, true), 0f, StrokeJoin.Round, 2f, Tolerance));
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static GlyphOutline Sliver() => GlyphOutline.FromCommands(
    [
        OutlineCommand.Move(0, 0),
        OutlineCommand.Line(50, 1000),
        OutlineCommand.Line(100, 0),
        OutlineCommand.CloseAt(0, 0),
    ]);

    private static Image Render(GlyphOutline outline)
    {
        var bounds = OutlineBounds.Tight(outline);
        int left = (int)MathF.Floor(bounds.XMin * Scale) - 2;
        int right = (int)MathF.Ceiling(bounds.XMax * Scale) + 2;
        int top = (int)MathF.Floor(-bounds.YMax * Scale) - 2;
        int bottom = (int)MathF.Ceiling(-bounds.YMin * Scale) + 2;

        var edges = OutlineFlattener.Flatten(outline, new OutlineTransform(Scale, 0f));
        var raster = ScanlineRasterizer.Rasterize(edges, right - left, bottom - top, left, top);
        return new Image(raster.Bitmap, right - left, left, top);
    }

    private sealed record Image(byte[] Pixels, int Width, int Left, int Top)
    {
        /// <summary>The pixel containing font-unit point (x, y).</summary>
        public byte At(float x, float y)
        {
            int column = (int)MathF.Floor(x * Scale) - Left;
            int row = (int)MathF.Floor(-y * Scale) - Top;
            return Pixels[(row * Width) + column];
        }

        /// <summary>Total coverage, in pixels.</summary>
        public double Coverage => Pixels.Sum(p => p / 255.0);
    }
}
