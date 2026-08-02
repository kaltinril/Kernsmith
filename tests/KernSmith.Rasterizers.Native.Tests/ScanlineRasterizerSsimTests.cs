using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 164 — measures the scanline rasterizer against stb_truetype, the reference
/// implementation of the same signed-area algorithm.
/// </summary>
/// <remarks>
/// Both are rendered into the identical box so the comparison is pixel-for-pixel with no
/// resampling: stb reports its bitmap box as bearings relative to the baseline, and the native
/// side is rasterized through a transform whose baseline sits on row 0, which is exactly stb's
/// own convention. Any residual difference is genuine coverage difference, not alignment.
/// </remarks>
public class ScanlineRasterizerSsimTests
{
    [Theory]
    [InlineData('I', 12f)]
    [InlineData('I', 32f)]
    [InlineData('I', 96f)]
    [InlineData('O', 12f)]
    [InlineData('O', 16f)]
    [InlineData('O', 24f)]
    [InlineData('O', 32f)]
    [InlineData('O', 48f)]
    [InlineData('O', 96f)]
    [InlineData('X', 12f)]
    [InlineData('X', 32f)]
    [InlineData('X', 96f)]
    [InlineData('e', 16f)]
    [InlineData('W', 24f)]
    [InlineData('8', 48f)]
    public void MatchesStbTrueType(char character, float pixelSize)
    {
        var (mine, reference, width, height) = RenderBoth(character, pixelSize);

        // Same box, so a mismatch here means the two disagree about the glyph's extent.
        mine.Length.ShouldBe(reference.Length);
        mine.ShouldContain(v => v > 0, "native output is blank");
        reference.ShouldContain(v => v > 0, "reference output is blank");

        // Straight-edged glyphs ('I', 'X', 'W') score above 0.999 - the coverage maths agrees
        // with stb to within rounding. The curved ones sit in the 0.95-0.99 band because the
        // two tessellate curves differently (Phase 163's adaptive cubic flattening versus stb's
        // quadratic subdivision), not because coverage differs; tightening the flattening
        // tolerance moves the score away from stb, not towards it.
        double ssim = Ssim(mine, reference, width, height);
        ssim.ShouldBeGreaterThan(0.95, $"'{character}' at {pixelSize}px scored {ssim:F4}");
    }

    [Fact]
    public void SsimIsNotTriviallyOne_ForMismatchedGlyphs()
    {
        // Guards the metric itself: comparing 'O' against 'X' must score badly, otherwise the
        // 0.95 threshold above would pass for anything.
        var (o, _, width, height) = RenderBoth('O', 32f);
        var (x, _, xWidth, xHeight) = RenderBoth('X', 32f);

        // Crop both to the smaller box so the metric is well defined.
        int w = Math.Min(width, xWidth);
        int h = Math.Min(height, xHeight);
        var oCrop = Crop(o, width, w, h);
        var xCrop = Crop(x, xWidth, w, h);

        Ssim(oCrop, xCrop, w, h).ShouldBeLessThan(0.9);
    }

    /// <summary>
    /// Renders one glyph with both rasterizers into stb's bitmap box.
    /// </summary>
    private static (byte[] Mine, byte[] Reference, int Width, int Height) RenderBoth(char character, float pixelSize)
    {
        var bytes = TestFonts.RobotoRegularBytes();

        using var stb = new StbTrueTypeRasterizer();
        stb.LoadFont(bytes);
        var reference = stb.RasterizeGlyph(character, new RasterOptions { Size = pixelSize, Dpi = 72 });
        reference.ShouldNotBeNull();

        var face = NativeFontFace.Load(bytes);
        var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(character)));

        // Baseline on row 0 is stb's own glyph space: a font-unit Y maps to -y * scale.
        var transform = new OutlineTransform(pixelSize / face.Head.UnitsPerEm, 0f);
        var edges = OutlineFlattener.Flatten(outline, transform);

        // stb's box origin, recovered from the bearings it reports.
        var result = ScanlineRasterizer.Rasterize(
            edges,
            reference.Width,
            reference.Height,
            reference.Metrics.BearingX,
            -reference.Metrics.BearingY);

        return (result.Bitmap, reference.BitmapData, reference.Width, reference.Height);
    }

    private static byte[] Crop(byte[] source, int sourceWidth, int width, int height)
    {
        var cropped = new byte[width * height];
        for (int y = 0; y < height; y++)
            Array.Copy(source, y * sourceWidth, cropped, y * width, width);

        return cropped;
    }

    /// <summary>
    /// Mean structural similarity over 8x8 windows, the usual formulation with C1 = (0.01*255)^2
    /// and C2 = (0.03*255)^2. Images smaller than one window are scored as a single window.
    /// </summary>
    private static double Ssim(byte[] a, byte[] b, int width, int height)
    {
        const double C1 = 6.5025;    // (0.01 * 255)^2
        const double C2 = 58.5225;   // (0.03 * 255)^2
        int window = 8;

        double total = 0;
        int windows = 0;

        for (int wy = 0; wy < height; wy += window)
        {
            for (int wx = 0; wx < width; wx += window)
            {
                int x1 = Math.Min(wx + window, width);
                int y1 = Math.Min(wy + window, height);
                int n = (x1 - wx) * (y1 - wy);
                if (n == 0)
                    continue;

                double meanA = 0, meanB = 0;
                for (int y = wy; y < y1; y++)
                {
                    for (int x = wx; x < x1; x++)
                    {
                        meanA += a[(y * width) + x];
                        meanB += b[(y * width) + x];
                    }
                }

                meanA /= n;
                meanB /= n;

                double varA = 0, varB = 0, covariance = 0;
                for (int y = wy; y < y1; y++)
                {
                    for (int x = wx; x < x1; x++)
                    {
                        double da = a[(y * width) + x] - meanA;
                        double db = b[(y * width) + x] - meanB;
                        varA += da * da;
                        varB += db * db;
                        covariance += da * db;
                    }
                }

                double divisor = Math.Max(1, n - 1);
                varA /= divisor;
                varB /= divisor;
                covariance /= divisor;

                total += (((2 * meanA * meanB) + C1) * ((2 * covariance) + C2))
                    / (((meanA * meanA) + (meanB * meanB) + C1) * (varA + varB + C2));
                windows++;
            }
        }

        return windows == 0 ? 1.0 : total / windows;
    }
}
