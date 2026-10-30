using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Holds the shadow box blur to a frozen copy of the naive per-output summation — every
/// output's float bits, on the vector and the scalar path — and the shadow effect and
/// post-processor outputs built on it.
/// </summary>
public sealed class BoxBlurEquivalenceTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void Blur_RandomBuffers_MatchReferenceBitForBit(int seed, bool vector)
    {
        var rng = new Random(seed);
        for (var trial = 0; trial < 200; trial++)
        {
            var width = rng.Next(1, 70);
            var height = rng.Next(1, 70);
            var radius = rng.Next(1, 14);
            var src = new float[width * height];
            for (var i = 0; i < src.Length; i++)
            {
                src[i] = rng.Next(4) switch
                {
                    0 => 0f,
                    1 => rng.Next(256) / 255f * (float)rng.NextDouble(),
                    2 => (float)rng.NextDouble(),
                    _ => (float)(rng.NextDouble() * 1e-3),
                };
            }

            var expected = ReferenceBlur(src, width, height, radius);
            var temp = new float[src.Length];
            var actual = new float[src.Length];
            BoxBlurFilter.Blur(src, temp, actual, width, height, radius, vector);

            for (var i = 0; i < src.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(actual[i]) != BitConverter.SingleToInt32Bits(expected[i]))
                    throw new ShouldAssertException(
                        $"seed {seed} trial {trial} {width}x{height} r{radius} index {i}: {actual[i]:R} vs {expected[i]:R}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ShadowEffect_RandomGlyphs_MatchReference(int seed)
    {
        var rng = new Random(seed);
        for (var trial = 0; trial < 60; trial++)
        {
            var w = rng.Next(1, 30);
            var h = rng.Next(1, 30);
            var alpha = new byte[w * h];
            for (var i = 0; i < alpha.Length; i++)
                alpha[i] = rng.Next(3) == 0 ? (byte)0 : (byte)rng.Next(256);
            int ox = rng.Next(-4, 5), oy = rng.Next(-4, 5), blur = rng.Next(0, 6), passes = rng.Next(1, 4), kernel = rng.Next(0, 3);
            var opacity = (float)rng.NextDouble();
            var hard = rng.Next(3) == 0;

            var actual = new ShadowEffect(ox, oy, blur, 10, 20, 30, opacity, hard, passes, kernel)
                .Generate(alpha, w, h, w, new GlyphMetrics(0, 0, w, w, h));
            var expected = ReferenceShadow(alpha, w, h, ox, oy, blur, opacity, hard, passes, kernel);
            actual.RgbaData.ShouldBe(expected, $"seed {seed} trial {trial}");
        }
    }

    [Fact]
    public void ShadowPostProcessor_RandomGlyphs_MatchReferenceBlur()
    {
        // The post-processor's own blur feeds the same RGBA build as before; compare its alpha
        // channel with the reference blur of the same placed shadow.
        var rng = new Random(7);
        for (var trial = 0; trial < 40; trial++)
        {
            var w = rng.Next(1, 30);
            var h = rng.Next(1, 30);
            var data = new byte[w * h];
            for (var i = 0; i < data.Length; i++)
                data[i] = rng.Next(3) == 0 ? (byte)0 : (byte)rng.Next(256);
            var radius = rng.Next(1, 6);
            var glyph = new RasterizedGlyph
            {
                Codepoint = 65,
                GlyphIndex = 1,
                BitmapData = data,
                Width = w,
                Height = h,
                Pitch = w,
                Metrics = new GlyphMetrics(0, 0, w, w, h),
                Format = PixelFormat.Grayscale8,
            };

            var processor = new ShadowPostProcessor(0, 0, radius, 0, 0, 0, 1f);
            var result = processor.Process(glyph);

            // Shadow canvas with zero offset: the glyph sits at (radius, radius).
            var dstW = w + 2 * radius;
            var dstH = h + 2 * radius;
            result.Width.ShouldBe(dstW);
            result.Height.ShouldBe(dstH);
            var placed = new float[dstW * dstH];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    placed[(y + radius) * dstW + x + radius] = data[y * w + x] / 255f * 1f;
            var blurred = ReferenceBlur(placed, dstW, dstH, radius);

            // Outside the glyph the result pixel is the shadow alone (glyph alpha 0 there).
            for (var y = 0; y < dstH; y++)
            {
                for (var x = 0; x < dstW; x++)
                {
                    var gx = x - radius;
                    var gy = y - radius;
                    if (gx >= 0 && gx < w && gy >= 0 && gy < h && data[gy * w + gx] != 0)
                        continue;
                    var sa = blurred[y * dstW + x];
                    var expectedA = sa <= 0 ? 0 : (byte)Math.Min(255, (int)(sa * 255));
                    result.BitmapData[(y * dstW + x) * 4 + 3].ShouldBe((byte)expectedA, $"trial {trial} ({x},{y})");
                }
            }
        }
    }

    // ---------------------------------------------------------------- frozen reference

    private static float[] ReferenceBlur(float[] src, int width, int height, int radius)
    {
        var temp = new float[width * height];
        var dst = new float[width * height];
        var invKernel = 1f / (radius * 2 + 1);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                    sum += src[y * width + Math.Clamp(x + k, 0, width - 1)];
                temp[y * width + x] = sum * invKernel;
            }
        }
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                    sum += temp[Math.Clamp(y + k, 0, height - 1) * width + x];
                dst[y * width + x] = sum * invKernel;
            }
        }
        return dst;
    }

    private static byte[] ReferenceShadow(byte[] alphaData, int width, int height, int offsetX, int offsetY,
        int blurRadius, float opacity, bool hardShadow, int blurPasses, int blurKernelSize)
    {
        blurRadius = Math.Max(0, blurRadius);
        blurPasses = Math.Max(1, blurPasses);
        blurKernelSize = Math.Max(0, blurKernelSize);
        opacity = Math.Clamp(opacity, 0f, 1f);
        var effectiveRadius = blurRadius + blurKernelSize;
        var blurMargin = effectiveRadius * blurPasses;
        var expandLeft = Math.Max(0, -offsetX) + blurMargin;
        var expandRight = Math.Max(0, offsetX) + blurMargin;
        var expandTop = Math.Max(0, -offsetY) + blurMargin;
        var expandBottom = Math.Max(0, offsetY) + blurMargin;
        var dstW = width + expandLeft + expandRight;
        var dstH = height + expandTop + expandBottom;
        var shadow = new float[dstW * dstH];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var srcIdx = y * width + x;
                var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] / 255f : 0f;
                if (hardShadow && alpha > 0f)
                    alpha = 1f;
                var dx = expandLeft + offsetX + x;
                var dy = expandTop + offsetY + y;
                if (dx >= 0 && dx < dstW && dy >= 0 && dy < dstH)
                    shadow[dy * dstW + dx] = alpha * opacity;
            }
        }
        if (effectiveRadius > 0)
        {
            for (var pass = 0; pass < blurPasses; pass++)
                shadow = ReferenceBlur(shadow, dstW, dstH, effectiveRadius);
        }
        var dst = new byte[dstW * dstH * 4];
        for (var i = 0; i < dstW * dstH; i++)
        {
            var sa = shadow[i];
            if (sa <= 0) continue;
            dst[i * 4 + 0] = 10;
            dst[i * 4 + 1] = 20;
            dst[i * 4 + 2] = 30;
            dst[i * 4 + 3] = (byte)Math.Min(255, (int)(sa * 255));
        }
        return dst;
    }
}
