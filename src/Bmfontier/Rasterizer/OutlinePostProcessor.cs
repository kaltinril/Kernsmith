using Bmfontier.Atlas;
using Bmfontier.Font.Models;

namespace Bmfontier.Rasterizer;

/// <summary>
/// Post-processor that adds an anti-aliased outline (border) around glyphs
/// using a Euclidean Distance Transform. Supports configurable outline color
/// and accepts both grayscale and RGBA input.
/// </summary>
public sealed class OutlinePostProcessor : IGlyphPostProcessor
{
    private readonly int _outlineWidth;
    private readonly byte _outlineR;
    private readonly byte _outlineG;
    private readonly byte _outlineB;

    internal int OutlineWidth => _outlineWidth;
    internal byte OutlineR => _outlineR;
    internal byte OutlineG => _outlineG;
    internal byte OutlineB => _outlineB;

    public OutlinePostProcessor(int outlineWidth, byte outlineR = 0, byte outlineG = 0, byte outlineB = 0)
    {
        _outlineWidth = outlineWidth;
        _outlineR = outlineR;
        _outlineG = outlineG;
        _outlineB = outlineB;
    }

    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (_outlineWidth <= 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var ow = _outlineWidth;

        var dstW = srcW + 2 * ow;
        var dstH = srcH + 2 * ow;

        // Step 1: Extract source alpha into an expanded buffer, centered.
        var expandedAlpha = new byte[dstW * dstH];

        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                byte alpha;
                if (glyph.Format == PixelFormat.Rgba32)
                {
                    var srcIdx = y * glyph.Pitch + x * 4 + 3;
                    alpha = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                }
                else
                {
                    var srcIdx = y * glyph.Pitch + x;
                    alpha = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                }

                expandedAlpha[(y + ow) * dstW + (x + ow)] = alpha;
            }
        }

        // Step 2: Compute EDT on the expanded alpha.
        var squaredDist = EuclideanDistanceTransform.Compute(expandedAlpha, dstW, dstH);

        // Step 3: Build RGBA output with outline color and anti-aliased alpha.
        var dst = new byte[dstW * dstH * 4];

        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                var dist = MathF.Sqrt(squaredDist[y * dstW + x]);
                // Anti-aliased outline alpha: smooth transition at the outer edge.
                var outlineAlpha = Math.Clamp(255f * (ow - dist + 0.5f), 0f, 255f);

                if (outlineAlpha <= 0)
                    continue;

                var idx = (y * dstW + x) * 4;
                dst[idx + 0] = _outlineR;
                dst[idx + 1] = _outlineG;
                dst[idx + 2] = _outlineB;
                dst[idx + 3] = (byte)outlineAlpha;
            }
        }

        // Step 4: Composite original glyph on top using alpha-over blending.
        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                byte srcR, srcG, srcB, srcA;

                if (glyph.Format == PixelFormat.Rgba32)
                {
                    var si = y * glyph.Pitch + x * 4;
                    if (si + 3 >= glyph.BitmapData.Length) continue;
                    srcR = glyph.BitmapData[si];
                    srcG = glyph.BitmapData[si + 1];
                    srcB = glyph.BitmapData[si + 2];
                    srcA = glyph.BitmapData[si + 3];
                }
                else
                {
                    var si = y * glyph.Pitch + x;
                    if (si >= glyph.BitmapData.Length) continue;
                    srcR = 255;
                    srcG = 255;
                    srcB = 255;
                    srcA = glyph.BitmapData[si];
                }

                if (srcA == 0) continue;

                var di = ((y + ow) * dstW + (x + ow)) * 4;

                // Alpha-over blending.
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
        }

        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX - ow,
            BearingY: metrics.BearingY + ow,
            Advance: metrics.Advance,
            Width: metrics.Width + 2 * ow,
            Height: metrics.Height + 2 * ow);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstW * 4,
            Metrics = newMetrics,
            Format = PixelFormat.Rgba32
        };
    }
}
