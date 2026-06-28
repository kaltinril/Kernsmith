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
