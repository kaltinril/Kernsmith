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

    private static RasterizedGlyph Glyph(int width, int height, int pitch, PixelFormat format, byte[] data) => new()
    {
        Codepoint = 'x',
        GlyphIndex = 1,
        BitmapData = data,
        Width = width,
        Height = height,
        Pitch = pitch,
        Metrics = new GlyphMetrics(BearingX: 0, BearingY: height, Advance: width, Width: width, Height: height),
        Format = format,
    };
}
