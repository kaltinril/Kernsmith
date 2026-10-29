using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 bitmap color on Noto Color Emoji (<c>CBDT</c>/<c>CBLC</c>, one 109 ppem strike,
/// index format 1 with PNG image format 17), compared with the FreeType backend.
/// </summary>
/// <remarks>
/// The FreeType backend cannot scale a bitmap strike: it selects the strike and returns its
/// bitmaps at 109 ppem whatever size is asked for. Native scales them to the requested size, so
/// at 109 px the two must agree exactly, and at other sizes Native is compared with FreeType's
/// strike bitmap scaled by an independent reference (supersampled point sampling).
/// </remarks>
public class NativeBitmapEmojiTests
{
    private const int StrikePpem = 109;
    private static readonly int[] Emoji = [0x1F600, 0x2764, 0x2B50, 0x1F680];

    [Fact]
    public void NotoColorEmoji_Loads_AndRendersColorGlyphs()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.NotoColorEmojiBytes());

        var glyph = native.RasterizeGlyph(0x1F600, new RasterOptions { Size = 32, ColorFont = true })!;

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.BitmapData.Chunk(4).ShouldContain(p => p[3] == 255 && (p[0] != p[1] || p[1] != p[2]));
    }

    [Theory]
    [InlineData(0x1F600)]
    [InlineData(0x2764)]
    [InlineData(0x2B50)]
    [InlineData(0x1F680)]
    public void AtTheStrikeSize_MatchesFreeType(int codepoint)
    {
        var options = new RasterOptions { Size = StrikePpem, ColorFont = true };
        var native = RenderNative(codepoint, options);
        var freeType = RenderFreeType(codepoint, options);

        native.Metrics.ShouldBe(freeType.Metrics);
        native.Width.ShouldBe(freeType.Width);
        native.Height.ShouldBe(freeType.Height);

        // FreeType returns premultiplied color; Native straight. Premultiplied, they agree to
        // the rounding of the premultiplication.
        var premultiplied = ColorComparison.Premultiply(native).BitmapData;
        int worst = premultiplied.Zip(freeType.BitmapData, (a, b) => Math.Abs(a - b)).Max();
        worst.ShouldBeLessThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(16f)]
    [InlineData(32f)]
    [InlineData(64f)]
    public void ScaledSizes_MatchFreeTypesStrikeBitmapScaledIndependently(float size)
    {
        foreach (int codepoint in Emoji)
        {
            var native = RenderNative(codepoint, new RasterOptions { Size = size, ColorFont = true });
            var strike = RenderFreeType(codepoint, new RasterOptions { Size = StrikePpem, ColorFont = true });
            var reference = Supersample(strike, size / StrikePpem);

            // Same placement: the box of the scaled strike rectangle.
            native.Metrics.BearingX.ShouldBe(reference.Metrics.BearingX, $"U+{codepoint:X}");
            native.Metrics.BearingY.ShouldBe(reference.Metrics.BearingY, $"U+{codepoint:X}");
            native.Width.ShouldBe(reference.Width, $"U+{codepoint:X}");
            native.Height.ShouldBe(reference.Height, $"U+{codepoint:X}");

            // The advance is the hmtx advance at the requested size; at the strike it equals
            // FreeType's bitmap advance, so scaling FreeType's lands within a pixel.
            Math.Abs(native.Metrics.Advance - (strike.Metrics.Advance * (double)size / StrikePpem)).ShouldBeLessThanOrEqualTo(1.0, $"U+{codepoint:X}");

            var scores = ColorComparison.ChannelSsim(ColorComparison.Premultiply(native), reference);
            scores.Min().ShouldBeGreaterThan(0.97, $"U+{codepoint:X} at {size}px: SSIM R/G/B/A = {string.Join(", ", scores.Select(s => s.ToString("0.000")))}");
        }
    }

    [Theory]
    [InlineData(16f)]
    [InlineData(109f)]
    public void Metrics_EqualTheRenderedGlyph(float size)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.NotoColorEmojiBytes());
        var options = new RasterOptions { Size = size, ColorFont = true };

        foreach (int codepoint in Emoji.Append(0x20))
            native.GetGlyphMetrics(codepoint, options).ShouldBe(native.RasterizeGlyph(codepoint, options)!.Metrics);
    }

    [Fact]
    public void WithoutColor_RendersInkCoverage_LikeFreeTypesGrayConversion()
    {
        // FreeType converts a color bitmap to gray when color is not requested; both measure how
        // dark the glyph makes a white background, so the silhouettes agree closely.
        foreach (int codepoint in Emoji)
        {
            var options = new RasterOptions { Size = StrikePpem };
            var native = RenderNative(codepoint, options);
            var freeType = RenderFreeType(codepoint, options);

            native.Format.ShouldBe(PixelFormat.Grayscale8);
            freeType.Format.ShouldBe(PixelFormat.Grayscale8);
            native.Metrics.ShouldBe(freeType.Metrics);
            ImageSimilarity.Ssim(native.BitmapData, freeType.BitmapData, native.Width, native.Height)
                .ShouldBeGreaterThan(0.95, $"U+{codepoint:X}");
        }
    }

    [Fact]
    public void Generate_NotoColorEmoji_WithNative_ProducesAColorAtlas()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(Emoji),
            Backend = RasterizerBackend.Native,
            ColorFont = true
        };

        var result = BmFont.Generate(TestFonts.NotoColorEmojiBytes(), options);

        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
        result.Model.Characters.Count.ShouldBe(Emoji.Length);
        result.Pages[0].PixelData.Chunk(4).ShouldContain(p => p[3] == 255 && (p[0] != p[1] || p[1] != p[2]));
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static RasterizedGlyph RenderNative(int codepoint, RasterOptions options)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.NotoColorEmojiBytes());
        return native.RasterizeGlyph(codepoint, options)!;
    }

    private static RasterizedGlyph RenderFreeType(int codepoint, RasterOptions options)
    {
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(TestFonts.NotoColorEmojiBytes());
        return freeType.RasterizeGlyph(codepoint, options)!;
    }

    /// <summary>
    /// Scales a premultiplied RGBA glyph by <paramref name="scale"/> onto the box of its scaled
    /// rectangle, averaging 8x8 point samples per output pixel (outside the bitmap = transparent).
    /// Deliberately a different method from Native's exact area weights.
    /// </summary>
    private static RasterizedGlyph Supersample(RasterizedGlyph source, double scale)
    {
        double left = source.Metrics.BearingX * scale, right = (source.Metrics.BearingX + source.Width) * scale;
        double top = source.Metrics.BearingY * scale, bottom = (source.Metrics.BearingY - source.Height) * scale;
        int minX = (int)Math.Floor(left + 1e-6), maxX = (int)Math.Ceiling(right - 1e-6);
        int maxY = (int)Math.Ceiling(top - 1e-6), minY = (int)Math.Floor(bottom + 1e-6);
        int width = maxX - minX, height = maxY - minY;
        var data = new byte[width * height * 4];
        const int n = 8;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var sum = new double[4];
                for (int sy = 0; sy < n; sy++)
                {
                    for (int sx = 0; sx < n; sx++)
                    {
                        double px = minX + x + ((sx + 0.5) / n);
                        double pyUp = maxY - y - ((sy + 0.5) / n);
                        int col = (int)Math.Floor((px / scale) - source.Metrics.BearingX);
                        int row = (int)Math.Floor(source.Metrics.BearingY - (pyUp / scale));
                        if (col < 0 || row < 0 || col >= source.Width || row >= source.Height)
                            continue;
                        for (int c = 0; c < 4; c++)
                            sum[c] += source.BitmapData[(row * source.Pitch) + (col * 4) + c];
                    }
                }

                for (int c = 0; c < 4; c++)
                    data[(((y * width) + x) * 4) + c] = (byte)Math.Round(sum[c] / (n * n));
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = source.Codepoint,
            GlyphIndex = source.GlyphIndex,
            BitmapData = data,
            Width = width,
            Height = height,
            Pitch = width * 4,
            Metrics = new GlyphMetrics(BearingX: minX, BearingY: maxY, Advance: 0, Width: width, Height: height),
            Format = PixelFormat.Rgba32
        };
    }
}
