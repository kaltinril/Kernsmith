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

        // Step 2: Binarize alpha so the EDT measures distance from the opaque glyph core.
        // Semi-transparent antialiased edge pixels (alpha < 128) are treated as "outside",
        // which lets the outline extend under the fringe and eliminates edge gaps.
        var binaryAlpha = new byte[dstW * dstH];
        for (var i = 0; i < expandedAlpha.Length; i++)
            binaryAlpha[i] = expandedAlpha[i] >= 32 ? (byte)255 : (byte)0;

        var squaredDist = EuclideanDistanceTransform.Compute(binaryAlpha, dstW, dstH);

        // Step 2b: Flood-fill from edges to identify exterior zero-alpha pixels.
        // Counter pixels (holes in glyphs like 'e', 'o') are NOT exterior and must not receive outline.
        // Uses binarized alpha so the fringe is treated as exterior.
        var exterior = FloodFillExterior(binaryAlpha, dstW, dstH);

        // Step 3: Build RGBA output with outline color and anti-aliased alpha.
        var dst = new byte[dstW * dstH * 4];

        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                var pixelIdx = y * dstW + x;

                if (!exterior[pixelIdx])
                    continue;

                var dist = MathF.Sqrt(squaredDist[pixelIdx]);
                // Smooth falloff: fully opaque up to ow, then linear fade over 1.5 pixels.
                var outlineAlpha = Math.Clamp(255f * (ow + 0.75f - dist) / 1.5f, 0f, 255f);

                if (outlineAlpha <= 0)
                    continue;

                var idx = pixelIdx * 4;
                dst[idx + 0] = _outlineR;
                dst[idx + 1] = _outlineG;
                dst[idx + 2] = _outlineB;
                dst[idx + 3] = (byte)outlineAlpha;
            }
        }

        return new GlyphLayer(dst, dstW, dstH, OffsetX: -ow, OffsetY: -ow, ZOrder);
    }

    private static bool[] FloodFillExterior(byte[] alpha, int width, int height)
    {
        var exterior = new bool[width * height];
        var queue = new Queue<int>();

        // Seed all edge pixels that have zero alpha.
        for (var x = 0; x < width; x++)
        {
            if (alpha[x] == 0)
            {
                exterior[x] = true;
                queue.Enqueue(x);
            }

            var bottomIdx = (height - 1) * width + x;
            if (alpha[bottomIdx] == 0)
            {
                exterior[bottomIdx] = true;
                queue.Enqueue(bottomIdx);
            }
        }

        for (var y = 1; y < height - 1; y++)
        {
            var leftIdx = y * width;
            if (alpha[leftIdx] == 0)
            {
                exterior[leftIdx] = true;
                queue.Enqueue(leftIdx);
            }

            var rightIdx = y * width + width - 1;
            if (alpha[rightIdx] == 0)
            {
                exterior[rightIdx] = true;
                queue.Enqueue(rightIdx);
            }
        }

        // BFS through 4-connected zero-alpha neighbors.
        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var x = idx % width;
            var y = idx / width;

            ReadOnlySpan<(int dx, int dy)> neighbors = [(-1, 0), (1, 0), (0, -1), (0, 1)];
            foreach (var (dx, dy) in neighbors)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;

                var nIdx = ny * width + nx;
                if (!exterior[nIdx] && alpha[nIdx] == 0)
                {
                    exterior[nIdx] = true;
                    queue.Enqueue(nIdx);
                }
            }
        }

        return exterior;
    }
}
