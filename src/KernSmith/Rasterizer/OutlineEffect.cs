using KernSmith.Atlas;
using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Generates an outline ring layer using Euclidean Distance Transform.
/// Z-order 1: renders behind the body, in front of shadow.
/// The layer does NOT include the glyph body -- just the outline ring.
/// </summary>
internal sealed class OutlineEffect : IGlyphEffect
{
    public int ZOrder => 1;

    private readonly int _outlineWidth;
    private readonly byte _outlineR;
    private readonly byte _outlineG;
    private readonly byte _outlineB;

    public OutlineEffect(int outlineWidth, byte outlineR = 0, byte outlineG = 0, byte outlineB = 0)
    {
        _outlineWidth = outlineWidth;
        _outlineR = outlineR;
        _outlineG = outlineG;
        _outlineB = outlineB;
    }

    public GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics)
    {
        var ow = _outlineWidth;
        var dstW = width + 2 * ow;
        var dstH = height + 2 * ow;

        // Step 1: Extract source alpha into an expanded buffer, centered.
        var expandedAlpha = new byte[dstW * dstH];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var srcIdx = y * pitch + x;
                var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
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

        return new GlyphLayer(dst, dstW, dstH, OffsetX: -ow, OffsetY: -ow, ZOrder);
    }
}
