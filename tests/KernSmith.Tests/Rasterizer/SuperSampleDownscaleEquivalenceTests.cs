using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Holds <see cref="PreparedRasterization.SuperSampleDownscale"/> to a frozen copy of its
/// per-pixel box filter over randomized glyphs (grayscale, RGBA premultiplied, RGBA per
/// channel; padded pitch; short buffers; glyphs narrower than the level): byte-identical.
/// </summary>
public sealed class SuperSampleDownscaleEquivalenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Downscale_RandomGlyphs_MatchesReference(int seed)
    {
        var rng = new Random(seed);
        for (var trial = 0; trial < 300; trial++)
        {
            var level = rng.Next(2, 9);
            var rgba = rng.Next(2) == 0;
            var perChannel = rng.Next(2) == 0;
            var bpp = rgba ? 4 : 1;
            var w = rng.Next(1, 60);
            var h = rng.Next(1, 60);
            var pitch = w * bpp + rng.Next(0, 3) * bpp;
            var length = Math.Max(1, pitch * h - (rng.Next(4) == 0 ? rng.Next(0, pitch * 2) : 0));
            var data = new byte[length];
            for (var i = 0; i < data.Length; i++)
                data[i] = rng.Next(4) == 0 ? (byte)0 : (byte)rng.Next(256);

            var glyph = new RasterizedGlyph
            {
                Codepoint = 66,
                GlyphIndex = 5,
                BitmapData = data,
                Width = w,
                Height = h,
                Pitch = pitch,
                Metrics = new GlyphMetrics(rng.Next(-20, 20), rng.Next(-20, 40), rng.Next(0, 80), w, h),
                Format = rgba ? PixelFormat.Rgba32 : PixelFormat.Grayscale8,
            };

            var actual = PreparedRasterization.SuperSampleDownscale(glyph, level, perChannel);
            var expected = Reference(glyph, level, perChannel);

            var context = $"seed {seed} trial {trial} level {level} rgba {rgba} perChannel {perChannel}";
            actual.Width.ShouldBe(expected.Width, context);
            actual.Height.ShouldBe(expected.Height, context);
            actual.Pitch.ShouldBe(expected.Pitch, context);
            actual.Metrics.ShouldBe(expected.Metrics, context);
            actual.Format.ShouldBe(expected.Format, context);
            actual.BitmapData.ShouldBe(expected.BitmapData, context);
        }
    }

    private static RasterizedGlyph Reference(RasterizedGlyph glyph, int level, bool perChannel)
    {
        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var dstW = Math.Max(1, srcW / level);
        var dstH = Math.Max(1, srcH / level);
        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var dst = new byte[dstW * dstH * bpp];
        var srcPitch = glyph.Pitch;
        var area = level * level;

        for (var dy = 0; dy < dstH; dy++)
        {
            for (var dx = 0; dx < dstW; dx++)
            {
                if (perChannel && bpp == 4)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        int sum = 0;
                        for (var sy = 0; sy < level; sy++)
                        {
                            for (var sx = 0; sx < level; sx++)
                            {
                                var srcIdx = (dy * level + sy) * srcPitch + (dx * level + sx) * 4 + c;
                                if (srcIdx < glyph.BitmapData.Length)
                                    sum += glyph.BitmapData[srcIdx];
                            }
                        }
                        dst[(dy * dstW + dx) * 4 + c] = (byte)(sum / area);
                    }
                }
                else if (bpp == 1)
                {
                    int sum = 0;
                    for (var sy = 0; sy < level; sy++)
                    {
                        for (var sx = 0; sx < level; sx++)
                        {
                            var srcIdx = (dy * level + sy) * srcPitch + (dx * level + sx);
                            if (srcIdx < glyph.BitmapData.Length)
                                sum += glyph.BitmapData[srcIdx];
                        }
                    }
                    dst[dy * dstW + dx] = (byte)(sum / area);
                }
                else
                {
                    float sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                    for (var sy = 0; sy < level; sy++)
                    {
                        for (var sx = 0; sx < level; sx++)
                        {
                            var srcIdx = (dy * level + sy) * srcPitch + (dx * level + sx) * 4;
                            if (srcIdx + 3 < glyph.BitmapData.Length)
                            {
                                var a = glyph.BitmapData[srcIdx + 3] / 255f;
                                sumR += glyph.BitmapData[srcIdx] * a;
                                sumG += glyph.BitmapData[srcIdx + 1] * a;
                                sumB += glyph.BitmapData[srcIdx + 2] * a;
                                sumA += a;
                            }
                        }
                    }
                    var dstIdx = (dy * dstW + dx) * 4;
                    if (sumA > 0)
                    {
                        dst[dstIdx] = (byte)Math.Min(255, sumR / sumA);
                        dst[dstIdx + 1] = (byte)Math.Min(255, sumG / sumA);
                        dst[dstIdx + 2] = (byte)Math.Min(255, sumB / sumA);
                    }
                    dst[dstIdx + 3] = (byte)Math.Min(255, sumA * 255 / area);
                }
            }
        }

        var m = glyph.Metrics;
        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstW * bpp,
            Metrics = new GlyphMetrics(m.BearingX / level, m.BearingY / level, m.Advance / level, dstW, dstH),
            Format = glyph.Format
        };
    }
}
