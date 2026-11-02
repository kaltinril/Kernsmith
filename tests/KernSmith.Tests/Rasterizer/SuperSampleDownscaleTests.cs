using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Pins what <see cref="PreparedRasterization.SuperSampleDownscale"/> averages: only the glyph's
/// own pixels, each block anchored at the glyph's top-left corner.
/// </summary>
public sealed class SuperSampleDownscaleTests
{
    [Fact]
    public void GrayscaleGlyphNarrowerThanTheLevel_DoesNotReadTheNextRow()
    {
        // A 1x2 glyph at level 2: one destination pixel covering (0,0)=0 and (0,1)=255. Reading
        // two bytes per row would take row 1's pixel as row 0's second column, counting it twice.
        var glyph = Glyph(width: 1, height: 2, pitch: 1, PixelFormat.Grayscale8, [0, 255]);

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 2);

        scaled.Width.ShouldBe(1);
        scaled.Height.ShouldBe(1);
        scaled.BitmapData.ShouldBe(new byte[] { 255 / 4 });
    }

    [Fact]
    public void RgbaGlyphNarrowerThanTheLevel_DoesNotReadTheNextRow()
    {
        // Premultiplied RGBA: (0,0) is transparent, (0,1) opaque red. Alpha is one opaque pixel in
        // four, and the color is that pixel's.
        var glyph = Glyph(width: 1, height: 2, pitch: 4, PixelFormat.Rgba32, [0, 0, 0, 0, 255, 0, 0, 255]);

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 2);

        scaled.BitmapData.ShouldBe(new byte[] { 255, 0, 0, 255 / 4 });
    }

    [Fact]
    public void SizeRoundsUp_SoInkInTheLastPartialBlockIsKept()
    {
        // 5x3 at level 2: BMFont rounds the size up (3x2), keeping the fifth column and third row,
        // each averaged over a full block with the missing pixels transparent.
        byte[] data =
        [
            0, 0, 0, 0, 255,
            0, 0, 0, 0, 255,
            255, 255, 0, 0, 255,
        ];
        var glyph = Glyph(width: 5, height: 3, pitch: 5, PixelFormat.Grayscale8, data);

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 2);

        scaled.Width.ShouldBe(3);
        scaled.Height.ShouldBe(2);
        scaled.Pitch.ShouldBe(3);
        scaled.Metrics.Width.ShouldBe(3);
        scaled.Metrics.Height.ShouldBe(2);
        scaled.BitmapData.ShouldBe(new byte[] { 0, 0, 510 / 4, 510 / 4, 0, 255 / 4 });
    }

    [Fact]
    public void RgbaSizeRoundsUp_SoInkInTheLastPartialBlockIsKept()
    {
        // 3x1 at level 2: the third pixel (opaque green) lands in a second, half-empty block.
        byte[] data = [0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 0, 255];
        var glyph = Glyph(width: 3, height: 1, pitch: 12, PixelFormat.Rgba32, data);

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 2);

        scaled.Width.ShouldBe(2);
        scaled.Height.ShouldBe(1);
        scaled.BitmapData.ShouldBe(new byte[] { 0, 0, 0, 0, 0, 255, 0, 255 / 4 });
    }

    [Fact]
    public void MetricsRoundToTheNearestPixel_NotTowardZero()
    {
        // Level 4: an advance of 22 high-res pixels is 5.5 px (Roboto '#' at a 12px cell), which
        // rounds to 6; integer division gave 5, a pixel short on every glyph whose remainder was
        // at least half the level. Bearings round the same way: 3/4 = 0.75 -> 1, 7/4 = 1.75 -> 2.
        var glyph = Glyph(width: 8, height: 8, pitch: 8, PixelFormat.Grayscale8, new byte[64],
            new GlyphMetrics(BearingX: 3, BearingY: 7, Advance: 22, Width: 8, Height: 8));

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 4);

        scaled.Metrics.Advance.ShouldBe(6);
        scaled.Metrics.BearingX.ShouldBe(1);
        scaled.Metrics.BearingY.ShouldBe(2);
    }

    [Fact]
    public void NegativeBearingsRoundHalfAwayFromZero()
    {
        // -3/4 = -0.75 -> -1 (truncation toward zero gave 0, moving the ink right by most of a
        // pixel); -2/4 = -0.5 is a half and rounds away from zero to -1, as positive halves round up.
        var glyph = Glyph(width: 8, height: 8, pitch: 8, PixelFormat.Grayscale8, new byte[64],
            new GlyphMetrics(BearingX: -3, BearingY: -2, Advance: 8, Width: 8, Height: 8));

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 4);

        scaled.Metrics.BearingX.ShouldBe(-1);
        scaled.Metrics.BearingY.ShouldBe(-1);
        scaled.Metrics.Advance.ShouldBe(2);
    }

    [Fact]
    public void BlankGlyphMetricsRoundToo()
    {
        // The pixel-less path (space, and Roboto ',' when its ink is thresholded away): 7/4 = 1.75 -> 2.
        var glyph = Glyph(width: 0, height: 0, pitch: 0, PixelFormat.Grayscale8, [],
            new GlyphMetrics(BearingX: 2, BearingY: 0, Advance: 7, Width: 0, Height: 0));

        var scaled = PreparedRasterization.SuperSampleDownscale(glyph, 4);

        scaled.Metrics.Advance.ShouldBe(2);
        scaled.Metrics.BearingX.ShouldBe(1);
    }

    private static RasterizedGlyph Glyph(int width, int height, int pitch, PixelFormat format, byte[] data, GlyphMetrics? metrics = null) => new()
    {
        Codepoint = 'x',
        GlyphIndex = 1,
        BitmapData = data,
        Width = width,
        Height = height,
        Pitch = pitch,
        Metrics = metrics ?? new GlyphMetrics(BearingX: 0, BearingY: height, Advance: width, Width: width, Height: height),
        Format = format,
    };
}
