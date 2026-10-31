using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Holds <see cref="GlyphCompositor.Composite"/> to a frozen copy of its straightforward
/// implementation (alpha extracted into a fresh array, a materialized default body layer,
/// float alpha-over for every pixel) over randomized glyphs and effect stacks: the composited
/// glyph must be byte-identical.
/// </summary>
public sealed class GlyphCompositorEquivalenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Composite_RandomGlyphsAndEffects_MatchesReference(int seed)
    {
        var rng = new Random(seed);
        var shapes = new[] { OutlineShape.Round, OutlineShape.Square, OutlineShape.Plus };
        for (var trial = 0; trial < 150; trial++)
        {
            var glyph = RandomGlyph(rng);
            var effects = new List<IGlyphEffect>();
            if (rng.Next(2) == 0)
                effects.Add(new ShadowEffect(rng.Next(-4, 5), rng.Next(-4, 5), rng.Next(0, 4),
                    (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (float)rng.NextDouble(),
                    rng.Next(3) == 0, rng.Next(1, 3)));
            if (rng.Next(2) == 0)
                effects.Add(new OutlineEffect(rng.Next(1, 5), (byte)rng.Next(256), (byte)rng.Next(256),
                    (byte)rng.Next(256), rng.Next(4) != 0, shapes[rng.Next(3)]));
            if (rng.Next(3) == 0)
                effects.Add(new GradientEffect((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256),
                    (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), rng.Next(0, 360)));
            if (rng.Next(2) == 0)
                effects.Reverse();

            byte fr = 255, fg = 255, fb = 255, fa = 255;
            if (rng.Next(3) == 0)
            {
                fr = (byte)rng.Next(256);
                fg = (byte)rng.Next(256);
                fb = (byte)rng.Next(256);
                fa = (byte)rng.Next(256);
            }

            var actual = GlyphCompositor.Composite(glyph, effects, fr, fg, fb, fa);
            var expected = ReferenceComposite(glyph, effects, fr, fg, fb, fa);

            var context = $"seed {seed} trial {trial}";
            actual.Width.ShouldBe(expected.Width, context);
            actual.Height.ShouldBe(expected.Height, context);
            actual.Pitch.ShouldBe(expected.Pitch, context);
            actual.Format.ShouldBe(expected.Format, context);
            actual.Metrics.ShouldBe(expected.Metrics, context);
            actual.BitmapData.ShouldBe(expected.BitmapData, context);
        }
    }

    [Fact]
    public void BlendOver_EverySourceAndDestinationAlpha_MatchesFloatAlphaOver()
    {
        // Exhaustive over source channel x source alpha x destination alpha; the destination
        // channels vary with the indices so the empty-destination path sees non-zero colors.
        var actual = new byte[4];
        for (var srcA = 1; srcA < 256; srcA++)
        {
            for (var dstA = 0; dstA < 256; dstA++)
            {
                for (var c = 0; c < 256; c++)
                {
                    byte dr = (byte)(c * 7 + dstA), dg = (byte)(srcA * 3 + c), db = (byte)(dstA ^ c);
                    byte sg = (byte)(255 - c), sb = (byte)(c ^ srcA);
                    actual[0] = dr; actual[1] = dg; actual[2] = db; actual[3] = (byte)dstA;
                    GlyphCompositor.BlendOver(actual, 0, (byte)c, sg, sb, (byte)srcA);

                    var expected = new byte[] { dr, dg, db, (byte)dstA };
                    ReferenceBlend(expected, 0, (byte)c, sg, sb, (byte)srcA);
                    if (actual[0] != expected[0] || actual[1] != expected[1] || actual[2] != expected[2] || actual[3] != expected[3])
                        throw new ShouldAssertException(
                            $"srcA {srcA} dstA {dstA} c {c}: [{string.Join(",", actual)}] vs [{string.Join(",", expected)}]");
                }
            }
        }
    }

    private static RasterizedGlyph RandomGlyph(Random rng)
    {
        var w = rng.Next(0, 24);
        var h = rng.Next(0, 24);
        var rgba = rng.Next(4) == 0;
        var bpp = rgba ? 4 : 1;
        var pitch = w * bpp + rng.Next(0, 3) * bpp;
        var data = new byte[Math.Max(0, pitch * h - (rng.Next(6) == 0 ? rng.Next(0, pitch + 1) : 0))];
        var cx = rng.NextDouble() * w;
        var cy = rng.NextDouble() * h;
        var rad = 2 + rng.NextDouble() * 8;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                var a = (byte)Math.Clamp((rad - d) * 100 + 128, 0, 255);
                var i = y * pitch + x * bpp;
                if (i + bpp - 1 >= data.Length)
                    continue;
                if (rgba)
                {
                    data[i] = (byte)rng.Next(256);
                    data[i + 1] = (byte)rng.Next(256);
                    data[i + 2] = (byte)rng.Next(256);
                    data[i + 3] = a;
                }
                else
                {
                    data[i] = a;
                }
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = 65,
            GlyphIndex = 3,
            BitmapData = data,
            Width = w,
            Height = h,
            Pitch = pitch,
            Metrics = new GlyphMetrics(rng.Next(-3, 3), rng.Next(0, 20), w + 1, w, h),
            Format = rgba ? PixelFormat.Rgba32 : PixelFormat.Grayscale8,
        };
    }

    // ---------------------------------------------------------------- frozen reference

    private static void ReferenceBlend(byte[] dst, int di, byte srcR, byte srcG, byte srcB, byte srcA)
    {
        var dstA = dst[di + 3];
        var sA = srcA / 255f;
        var dA = dstA / 255f;
        var outA = sA + dA * (1f - sA);
        if (outA > 0)
        {
            dst[di + 0] = (byte)((srcR * sA + dst[di + 0] * dA * (1f - sA)) / outA);
            dst[di + 1] = (byte)((srcG * sA + dst[di + 1] * dA * (1f - sA)) / outA);
            dst[di + 2] = (byte)((srcB * sA + dst[di + 2] * dA * (1f - sA)) / outA);
            dst[di + 3] = (byte)Math.Min(255, (int)(outA * 255));
        }
    }

    private static RasterizedGlyph ReferenceComposite(RasterizedGlyph sourceGlyph, IReadOnlyList<IGlyphEffect> effects,
        byte fillR, byte fillG, byte fillB, byte fillA)
    {
        var hasFillTint = fillR != 255 || fillG != 255 || fillB != 255 || fillA != 255;
        if (effects.Count == 0 && !hasFillTint)
            return sourceGlyph;
        if (sourceGlyph.Width == 0 || sourceGlyph.Height == 0 || sourceGlyph.BitmapData.Length == 0)
            return sourceGlyph;

        var srcW = sourceGlyph.Width;
        var srcH = sourceGlyph.Height;
        var alphaData = ReferenceExtractAlpha(sourceGlyph);
        var alphaPitch = srcW;

        var layers = new List<GlyphLayer>(effects.Count);
        var hasBodyEffect = false;
        IGlyphEffect? outlineEffect = null;
        IGlyphEffect? shadowEffect = null;
        var otherEffects = new List<IGlyphEffect>();
        foreach (var effect in effects)
        {
            if (effect.ZOrder == 0) shadowEffect = effect;
            else if (effect.ZOrder == 1) outlineEffect = effect;
            else otherEffects.Add(effect);
            if (effect.ZOrder == 2) hasBodyEffect = true;
        }

        GlyphLayer? outlineLayer = null;
        if (outlineEffect != null)
        {
            outlineLayer = outlineEffect.Generate(alphaData, srcW, srcH, alphaPitch, sourceGlyph.Metrics);
            layers.Add(outlineLayer);
        }

        if (shadowEffect != null)
        {
            if (outlineLayer != null)
            {
                var mergedSize = outlineLayer.Width * outlineLayer.Height;
                var mergedAlpha = new byte[mergedSize];
                for (var i = 0; i < mergedSize; i++)
                    mergedAlpha[i] = outlineLayer.RgbaData[i * 4 + 3];
                var glyphOffX = -outlineLayer.OffsetX;
                var glyphOffY = -outlineLayer.OffsetY;
                for (var y = 0; y < srcH; y++)
                {
                    for (var x = 0; x < srcW; x++)
                    {
                        var dx = glyphOffX + x;
                        var dy = glyphOffY + y;
                        if (dx < 0 || dx >= outlineLayer.Width || dy < 0 || dy >= outlineLayer.Height) continue;
                        var srcIdx = y * alphaPitch + x;
                        var srcAlpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                        var dstIdx = dy * outlineLayer.Width + dx;
                        mergedAlpha[dstIdx] = Math.Max(mergedAlpha[dstIdx], srcAlpha);
                    }
                }

                var outlineMetrics = new GlyphMetrics(
                    sourceGlyph.Metrics.BearingX + outlineLayer.OffsetX,
                    sourceGlyph.Metrics.BearingY - outlineLayer.OffsetY,
                    sourceGlyph.Metrics.Advance, outlineLayer.Width, outlineLayer.Height);
                var shadowLayer = shadowEffect.Generate(mergedAlpha, outlineLayer.Width, outlineLayer.Height,
                    outlineLayer.Width, outlineMetrics);
                layers.Add(shadowLayer with
                {
                    OffsetX = shadowLayer.OffsetX + outlineLayer.OffsetX,
                    OffsetY = shadowLayer.OffsetY + outlineLayer.OffsetY
                });
            }
            else
            {
                layers.Add(shadowEffect.Generate(alphaData, srcW, srcH, alphaPitch, sourceGlyph.Metrics));
            }
        }

        foreach (var effect in otherEffects)
            layers.Add(effect.Generate(alphaData, srcW, srcH, alphaPitch, sourceGlyph.Metrics));

        if (!hasBodyEffect)
            layers.Add(ReferenceBodyLayer(alphaData, srcW, srcH, alphaPitch, sourceGlyph, fillR, fillG, fillB, fillA));

        layers.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

        int minX = 0, minY = 0, maxX = srcW, maxY = srcH;
        foreach (var layer in layers)
        {
            minX = Math.Min(minX, layer.OffsetX);
            minY = Math.Min(minY, layer.OffsetY);
            maxX = Math.Max(maxX, layer.OffsetX + layer.Width);
            maxY = Math.Max(maxY, layer.OffsetY + layer.Height);
        }

        var canvasW = maxX - minX;
        var canvasH = maxY - minY;
        var dst = new byte[canvasW * canvasH * 4];
        foreach (var layer in layers)
        {
            var layerCanvasX = layer.OffsetX - minX;
            var layerCanvasY = layer.OffsetY - minY;
            for (var y = 0; y < layer.Height; y++)
            {
                for (var x = 0; x < layer.Width; x++)
                {
                    var si = (y * layer.Width + x) * 4;
                    if (si + 3 >= layer.RgbaData.Length) continue;
                    var srcA = layer.RgbaData[si + 3];
                    if (srcA == 0) continue;
                    var dx = layerCanvasX + x;
                    var dy = layerCanvasY + y;
                    if (dx < 0 || dx >= canvasW || dy < 0 || dy >= canvasH) continue;
                    ReferenceBlend(dst, (dy * canvasW + dx) * 4,
                        layer.RgbaData[si], layer.RgbaData[si + 1], layer.RgbaData[si + 2], srcA);
                }
            }
        }

        var metrics = sourceGlyph.Metrics;
        return new RasterizedGlyph
        {
            Codepoint = sourceGlyph.Codepoint,
            GlyphIndex = sourceGlyph.GlyphIndex,
            BitmapData = dst,
            Width = canvasW,
            Height = canvasH,
            Pitch = canvasW * 4,
            Metrics = new GlyphMetrics(metrics.BearingX + minX, metrics.BearingY - minY, metrics.Advance, canvasW, canvasH),
            Format = PixelFormat.Rgba32
        };
    }

    private static byte[] ReferenceExtractAlpha(RasterizedGlyph glyph)
    {
        var w = glyph.Width;
        var h = glyph.Height;
        var alpha = new byte[w * h];
        var rgba = glyph.Format == PixelFormat.Rgba32;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcIdx = rgba ? y * glyph.Pitch + x * 4 + 3 : y * glyph.Pitch + x;
                alpha[y * w + x] = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
            }
        }
        return alpha;
    }

    private static GlyphLayer ReferenceBodyLayer(byte[] alphaData, int width, int height, int pitch, RasterizedGlyph sourceGlyph,
        byte fillR, byte fillG, byte fillB, byte fillA)
    {
        var rgba = new byte[width * height * 4];
        if (sourceGlyph.Format == PixelFormat.Rgba32)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var si = y * sourceGlyph.Pitch + x * 4;
                    var di = (y * width + x) * 4;
                    if (si + 3 < sourceGlyph.BitmapData.Length)
                    {
                        rgba[di + 0] = sourceGlyph.BitmapData[si + 0];
                        rgba[di + 1] = sourceGlyph.BitmapData[si + 1];
                        rgba[di + 2] = sourceGlyph.BitmapData[si + 2];
                        rgba[di + 3] = sourceGlyph.BitmapData[si + 3];
                    }
                }
            }
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = y * pitch + x;
                    var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                    var di = (y * width + x) * 4;
                    rgba[di + 0] = fillR;
                    rgba[di + 1] = fillG;
                    rgba[di + 2] = fillB;
                    rgba[di + 3] = fillA == 255 ? alpha : (byte)(alpha * fillA / 255);
                }
            }
        }

        return new GlyphLayer(rgba, width, height, OffsetX: 0, OffsetY: 0, ZOrder: 2);
    }
}
