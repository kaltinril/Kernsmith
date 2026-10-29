using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// FreeType hands back BGRA color glyphs with premultiplied color channels, while every
/// <see cref="PixelFormat.Rgba32"/> consumer in KernSmith (atlas pages, compositing, encoders,
/// the premultiplied export helpers) treats them as straight alpha. Uncorrected, every
/// partly transparent edge pixel is darkened by its own alpha a second time.
/// </summary>
public class FreeTypeColorAlphaTests
{
    // U+1F600 GRINNING FACE: a CBDT bitmap emoji in the committed Noto Color Emoji fixture,
    // rendered at its native 109 px strike so no scaling touches the pixels.
    private const int GrinningFace = 0x1F600;

    private static RasterizedGlyph RasterizeEmoji()
    {
        var fontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoColorEmoji.ttf"));
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(fontData);
        var glyph = rasterizer.RasterizeGlyph(GrinningFace, new RasterOptions { Size = 109, Dpi = 72, ColorFont = true });
        glyph.ShouldNotBeNull();
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        return glyph;
    }

    [Fact]
    public void ColorGlyph_EdgePixels_AreStraightAlpha()
    {
        var glyph = RasterizeEmoji();
        var px = glyph.BitmapData;

        // In premultiplied data no color channel can exceed alpha. The emoji's anti-aliased rim
        // is a bright yellow-orange, so in straight alpha almost every edge pixel has a channel
        // well above its alpha.
        int edge = 0, brighterThanAlpha = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a is 0 or 255)
                continue;
            edge++;
            if (Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])) > a)
                brighterThanAlpha++;
        }

        edge.ShouldBeGreaterThan(100);
        brighterThanAlpha.ShouldBeGreaterThan(edge * 3 / 4);
    }

    [Fact]
    public void ColorGlyph_FirstRimPixel_HasTheRimColorAtLowAlpha()
    {
        var glyph = RasterizeEmoji();
        var px = glyph.BitmapData;

        // FreeType's first partly covered pixel in scan order is premultiplied (31, 20, 4, 32);
        // straight, that is the rim color (247, 159, 32) at alpha 32.
        var i = 0;
        while (px[i + 3] == 0)
            i += 4;

        (px[i], px[i + 1], px[i + 2], px[i + 3]).ShouldBe(((byte)247, (byte)159, (byte)32, (byte)32));
    }

    [Fact]
    public void ColorGlyph_TransparentPixels_StayTransparentBlack()
    {
        var glyph = RasterizeEmoji();
        var px = glyph.BitmapData;

        var transparent = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] != 0)
                continue;
            transparent++;
            (px[i], px[i + 1], px[i + 2]).ShouldBe(((byte)0, (byte)0, (byte)0));
        }

        transparent.ShouldBeGreaterThan(0);
    }
}
