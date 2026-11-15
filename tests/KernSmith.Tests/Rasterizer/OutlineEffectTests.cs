using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Isolated unit tests for <see cref="OutlineEffect"/>, focusing on how the outline ring
/// behaves around enclosed counters (the holes inside letters like o/e/a/d/b/g/0/8).
/// </summary>
public class OutlineEffectTests
{
    private const int Size = 40;       // source glyph dimensions
    private const int Pitch = 40;      // grayscale, 1 byte/pixel
    private const int Ow = 2;          // outline width
    private const int LayerWidth = Size + 2 * Ow;  // 44

    /// <summary>
    /// Builds a filled annulus (donut) alpha buffer: a glyph with a large enclosed counter.
    /// alpha = 255 where 8 &lt;= distance-from-center &lt;= 16, else 0.
    /// </summary>
    private static byte[] MakeDonut()
    {
        var alpha = new byte[Pitch * Size];
        const double cx = 20.0;
        const double cy = 20.0;

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                alpha[y * Pitch + x] = (d >= 8.0 && d <= 16.0) ? (byte)255 : (byte)0;
            }
        }

        return alpha;
    }

    /// <summary>Reads the output alpha byte at source coordinates (mapped by +ow into the layer).</summary>
    private static byte LayerAlphaAtSource(GlyphLayer layer, int srcX, int srcY)
    {
        var lx = srcX + Ow;
        var ly = srcY + Ow;
        return layer.RgbaData[(ly * LayerWidth + lx) * 4 + 3];
    }

    [Fact]
    public void Generate_Donut_DrawsInnerRingInsideCounter()
    {
        // Arrange -- a glyph with a large enclosed counter.
        var alpha = MakeDonut();
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 40, Width: 40, Height: 40);

        // Act
        var layer = new OutlineEffect(Ow).Generate(alpha, Size, Size, Pitch, metrics);

        // Assert -- layer is sized width/height + 2*ow.
        layer.Width.ShouldBe(LayerWidth);
        layer.Height.ShouldBe(LayerWidth);

        // A counter pixel ~1px inside the inner wall must receive an outline ring.
        // Source (27,20) sits just inside the hole edge (inner wall at d==8 -> x==12 and x==28).
        var innerRing = LayerAlphaAtSource(layer, 27, 20);
        innerRing.ShouldBeGreaterThan((byte)0, "the inner counter wall must get an outline ring");

        // The dead-center pixel (8px from any wall) is beyond the falloff and must stay transparent;
        // this guards against flooding the entire counter.
        var center = LayerAlphaAtSource(layer, 20, 20);
        center.ShouldBe((byte)0, "the open center of a large counter must not be flooded");
    }

    /// <summary>A single opaque pixel at the center of a 3x3 source: every outline pixel's distance is its offset.</summary>
    private static byte[] MakeSinglePixel() => [0, 0, 0, 0, 255, 0, 0, 0, 0];

    private static byte[] LayerAlphas(GlyphLayer layer)
    {
        var alphas = new byte[layer.Width * layer.Height];
        for (var i = 0; i < alphas.Length; i++)
            alphas[i] = layer.RgbaData[i * 4 + 3];
        return alphas;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Generate_NoSmoothing_OutlineIsBinaryAndFullyOpaqueAtExactWidth(int ow)
    {
        // Arrange -- one pixel; with smoothing off, every outline pixel must be 0 or 255.
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 3, Width: 3, Height: 3);

        // Act
        var layer = new OutlineEffect(ow, smooth: false).Generate(MakeSinglePixel(), 3, 3, 3, metrics);

        // Assert
        var alphas = LayerAlphas(layer);
        alphas.ShouldAllBe(a => a == 0 || a == 255, "no partial-alpha ring when anti-aliasing is off");

        // Pixel exactly ow away on the axis is fully opaque (was 127 with the 1.5px ramp).
        var cx = 1 + ow;
        var cy = 1 + ow;
        alphas[cy * layer.Width + cx + ow].ShouldBe((byte)255, "pixel at distance == ow must be solid");
        // One further out is transparent.
        alphas[cy * layer.Width + cx + ow + 1].ShouldBe((byte)0);
    }

    [Fact]
    public void Generate_NoSmoothing_DiagonalFollowsEuclideanDistance()
    {
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 3, Width: 3, Height: 3);

        // ow=1: diagonal neighbour is sqrt(2) > 1 away -> transparent (round outline).
        var ow1 = new OutlineEffect(1, smooth: false).Generate(MakeSinglePixel(), 3, 3, 3, metrics);
        LayerAlphas(ow1)[1 * ow1.Width + 1].ShouldBe((byte)0);

        // ow=2: diagonal neighbour (sqrt(2)) is inside, (1,2) offset (sqrt(5) = 2.24) is outside.
        var ow2 = new OutlineEffect(2, smooth: false).Generate(MakeSinglePixel(), 3, 3, 3, metrics);
        var a2 = LayerAlphas(ow2);
        a2[(3 - 1) * ow2.Width + (3 - 1)].ShouldBe((byte)255);
        a2[(3 - 2) * ow2.Width + (3 - 1)].ShouldBe((byte)0);
    }

    // Single pixel at the center of the 3x3 source; layer is (3 + 2*ow) wide and the pixel sits at (1 + ow).
    private static byte[] ShapeAlphas(int ow, OutlineShape shape, bool smooth = false)
    {
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 3, Width: 3, Height: 3);
        var layer = new OutlineEffect(ow, smooth: smooth, shape: shape).Generate(MakeSinglePixel(), 3, 3, 3, metrics);
        return LayerAlphas(layer);
    }

    private static byte AtOffset(byte[] alphas, int ow, int dx, int dy)
    {
        var width = 3 + 2 * ow;
        var c = 1 + ow;
        return alphas[(c + dy) * width + c + dx];
    }

    [Fact]
    public void Generate_NoSmoothing_SquareOw1_FillsAllEightNeighbours()
    {
        var a = ShapeAlphas(1, OutlineShape.Square);

        foreach (var (dx, dy) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) })
            AtOffset(a, 1, dx, dy).ShouldBe((byte)255, $"offset ({dx},{dy}) must be filled");
    }

    [Fact]
    public void Generate_NoSmoothing_PlusOw1_FillsOnlyFourNeighbours()
    {
        var a = ShapeAlphas(1, OutlineShape.Plus);

        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
            AtOffset(a, 1, dx, dy).ShouldBe((byte)255, $"offset ({dx},{dy}) must be filled");
        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            AtOffset(a, 1, dx, dy).ShouldBe((byte)0, $"diagonal ({dx},{dy}) must be empty");
    }

    [Fact]
    public void Generate_NoSmoothing_SquareOw2_IsAFilledSquare()
    {
        var a = ShapeAlphas(2, OutlineShape.Square);

        // Corner (2,2) is inside; the whole 5x5 block is solid.
        AtOffset(a, 2, 2, 2).ShouldBe((byte)255);
        AtOffset(a, 2, -2, 2).ShouldBe((byte)255);
        a.Count(x => x == 255).ShouldBe(25);
    }

    [Fact]
    public void Generate_NoSmoothing_PlusOw2_IsADiamond()
    {
        var a = ShapeAlphas(2, OutlineShape.Plus);

        AtOffset(a, 2, 1, 1).ShouldBe((byte)255, "|dx|+|dy| == 2 is inside");
        AtOffset(a, 2, 2, 1).ShouldBe((byte)0, "|dx|+|dy| == 3 is outside");
        AtOffset(a, 2, 2, 2).ShouldBe((byte)0);
        a.Count(x => x == 255).ShouldBe(13); // diamond of radius 2
    }

    [Fact]
    public void Generate_NoSmoothing_RoundIsDefaultAndUnchanged()
    {
        ShapeAlphas(1, OutlineShape.Round).ShouldBe(ShapeAlphas(1, OutlineShape.Round));
        AtOffset(ShapeAlphas(1, OutlineShape.Round), 1, 1, 1).ShouldBe((byte)0);
    }

    [Theory]
    [InlineData(OutlineShape.Square, 1, 1, 255)]  // dist 1 == ow -> solid, as Round is on the axis
    [InlineData(OutlineShape.Round, 1, 1, 149)]   // dist sqrt(2): 59% of the pixel is within ow of the edge
    [InlineData(OutlineShape.Plus, 1, 1, 0)]       // Manhattan 2 == ow + 1 -> transparent
    public void Generate_Smoothing_AppliesShapeMetricToRamp(OutlineShape shape, int dx, int dy, int expected)
    {
        var a = ShapeAlphas(1, shape, smooth: true);

        AtOffset(a, 1, dx, dy).ShouldBe((byte)expected);
    }

    [Fact]
    public void Generate_Smoothing_SolidEdgeGetsTheFullWidth_CornersStayAntiAliased()
    {
        // Guards the default (smooth) look: across a pixel-aligned edge the outline is ow solid
        // pixels (a fully covered pixel's edge is half a pixel past its centre, so the pixel ow
        // away lies wholly within ow of it), and round corners fade by distance.
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 3, Width: 3, Height: 3);

        var layer = new OutlineEffect(1).Generate(MakeSinglePixel(), 3, 3, 3, metrics);

        LayerAlphas(layer)[2 * layer.Width + 3].ShouldBe((byte)255);
        LayerAlphas(layer)[1 * layer.Width + 3].ShouldBe((byte)149); // diagonal neighbour, sqrt(2) away
    }

    [Fact]
    public void OutlinePostProcessor_NoSmoothing_OutlineIsBinary()
    {
        var glyph = new RasterizedGlyph
        {
            Codepoint = 'L',
            GlyphIndex = 0,
            BitmapData = MakeSinglePixel(),
            Width = 3,
            Height = 3,
            Pitch = 3,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 3, Width: 3, Height: 3),
            Format = PixelFormat.Grayscale8,
        };

        var result = new OutlinePostProcessor(1) { Smooth = false }.Process(glyph);

        var alphas = new byte[result.Width * result.Height];
        for (var i = 0; i < alphas.Length; i++)
            alphas[i] = result.BitmapData[i * 4 + 3];
        alphas.ShouldAllBe(a => a == 0 || a == 255);
        alphas[2 * result.Width + 3].ShouldBe((byte)255, "pixel at distance == ow must be solid");
    }

    [Fact]
    public void Generate_Donut_DrawsOuterRingOutsideGlyph()
    {
        // Arrange
        var alpha = MakeDonut();
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 40, Width: 40, Height: 40);

        // Act
        var layer = new OutlineEffect(Ow).Generate(alpha, Size, Size, Pitch, metrics);

        // Assert -- an exterior pixel just outside the outer wall (outer wall at d==16 -> x==36)
        // still gets the outline ring.
        var outerRing = LayerAlphaAtSource(layer, 37, 20);
        outerRing.ShouldBeGreaterThan((byte)0, "the exterior outline ring must still be drawn");
    }
}
