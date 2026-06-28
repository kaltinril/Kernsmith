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

    /// <summary>
    /// Finds the topmost (smallest-y) layer row that has any outline coverage in the given
    /// layer column, or the layer height if the column is empty.
    /// </summary>
    private static int TopmostOutlineRow(GlyphLayer layer, int layerX)
    {
        for (var y = 0; y < layer.Height; y++)
        {
            if (layer.RgbaData[(y * layer.Width + layerX) * 4 + 3] > 0)
                return y;
        }

        return layer.Height;
    }

    /// <summary>
    /// Builds the shared synthetic glyph: a solid bar (alpha 255) across all columns on rows
    /// 3, 4, 5 of a 9x6 grayscale buffer, plus a single apex spike at (4, 2) whose value
    /// varies per test. The spike is what exercises the 50%-coverage binarization threshold.
    /// </summary>
    private static byte[] MakeApexGlyph(byte spikeValue)
    {
        const int width = 9;
        const int height = 6;
        const int pitch = 9;

        var alpha = new byte[pitch * height];

        // Solid bar across all columns for rows 3, 4, 5.
        for (var y = 3; y <= 5; y++)
            for (var x = 0; x < width; x++)
                alpha[y * pitch + x] = 255;

        // Apex spike: one pixel above the bar at x=4.
        alpha[2 * pitch + 4] = spikeValue;

        return alpha;
    }

    /// <summary>
    /// Regression for issue #127: the EDT binarizes at 50% coverage (alpha &gt;= 128), so faint
    /// sub-50% fringe at a curved apex is treated as outside the true geometric edge and must
    /// NOT be traced by the outline. A spike at alpha 64 (above 0, below 128) sits in that
    /// fringe: with the historical <c>&gt; 0</c> threshold it was traced (spike topmost 2 vs
    /// neighbor 3), the <c>&gt;= 128</c> fix excludes it so both columns share the same top (3).
    /// </summary>
    [Fact]
    public void Generate_SubFiftyPercentFringe_NotTracedByOutline()
    {
        // Arrange -- a solid bar (rows 3-5) plus a sub-50% apex spike at (4,2), alpha 64.
        const int width = 9;
        const int height = 6;
        const int pitch = 9;
        const int ow = 2;

        var alpha = MakeApexGlyph(spikeValue: 64);
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 9, Width: 9, Height: 6);

        // Act
        var layer = new OutlineEffect(ow).Generate(alpha, width, height, pitch, metrics);

        // Assert -- layer is sized width/height + 2*ow.
        layer.Width.ShouldBe(width + 2 * ow);   // 13
        layer.Height.ShouldBe(height + 2 * ow); // 10

        // Source x=4 maps to layer x=6 (spike column); source x=2 maps to layer x=4 (bar-only neighbor).
        var spikeTop = TopmostOutlineRow(layer, 4 + ow);
        var neighborTop = TopmostOutlineRow(layer, 2 + ow);

        // The sub-50% fringe must NOT pull the outline higher than the plain neighbor: it is
        // outside the glyph's true geometric edge, so both columns share the same topmost row.
        spikeTop.ShouldBe(neighborTop,
            "a sub-50% fringe spike must not be traced by the outline (issue #127)");
    }

    /// <summary>
    /// Sanity check that real edges are still followed: a spike at alpha 200 (&gt;= 128) is true
    /// coverage inside the glyph's geometric edge, so the outline must rise to follow it.
    /// </summary>
    [Fact]
    public void Generate_StrongApexCoverage_TracedByOutline()
    {
        // Arrange -- a solid bar (rows 3-5) plus a strong apex spike at (4,2), alpha 200.
        const int width = 9;
        const int height = 6;
        const int pitch = 9;
        const int ow = 2;

        var alpha = MakeApexGlyph(spikeValue: 200);
        var metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 9, Width: 9, Height: 6);

        // Act
        var layer = new OutlineEffect(ow).Generate(alpha, width, height, pitch, metrics);

        // Assert -- layer is sized width/height + 2*ow.
        layer.Width.ShouldBe(width + 2 * ow);   // 13
        layer.Height.ShouldBe(height + 2 * ow); // 10

        // Source x=4 maps to layer x=6 (spike column); source x=2 maps to layer x=4 (bar-only neighbor).
        var spikeTop = TopmostOutlineRow(layer, 4 + ow);
        var neighborTop = TopmostOutlineRow(layer, 2 + ow);

        // Strong coverage IS traced: the spike column reaches a higher (smaller-y) row than the
        // flat-bar neighbor, so the outline follows the apex.
        spikeTop.ShouldBeLessThan(neighborTop,
            "a >=50% apex spike must be traced by the outline (the outline rises to follow it)");
    }
}
