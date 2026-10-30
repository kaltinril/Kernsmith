using System.Buffers;
using KernSmith.Atlas;
using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Generates the outline layer via a Euclidean Distance Transform: an anti-aliased ring
/// around the glyph on both the exterior and the interior counter walls, plus a solid
/// (full-opacity) backing beneath the glyph body so the body composites on top without a seam.
/// Z-order 1: renders behind the body, in front of the shadow.
/// </summary>
internal sealed class OutlineEffect : IGlyphEffect
{
    public int ZOrder => 1;

    private readonly int _outlineWidth;
    private readonly byte _outlineR;
    private readonly byte _outlineG;
    private readonly byte _outlineB;

    private readonly bool _smooth;
    private readonly OutlineShape _shape;

    /// <summary>
    /// Creates the effect. <paramref name="smooth"/> true gives the soft anti-aliased ring; false
    /// (AntiAlias = None) gives a hard threshold: pixels within <paramref name="outlineWidth"/> of
    /// the glyph are fully opaque, the rest transparent. <paramref name="shape"/> picks the distance
    /// metric (round, square or plus corners).
    /// </summary>
    public OutlineEffect(int outlineWidth, byte outlineR = 0, byte outlineG = 0, byte outlineB = 0, bool smooth = true,
        OutlineShape shape = OutlineShape.Round)
    {
        _outlineWidth = outlineWidth;
        _outlineR = outlineR;
        _outlineG = outlineG;
        _outlineB = outlineB;
        _smooth = smooth;
        _shape = shape;
    }

    public GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics)
    {
        var ow = _outlineWidth;
        var dstW = width + 2 * ow;
        var dstH = height + 2 * ow;
        var size = dstW * dstH;

        // Step 1: Extract source alpha into an expanded buffer, centered, and binarize it so
        // the EDT measures distance from the opaque glyph core. Semi-transparent antialiased
        // edge pixels (alpha < 32) are treated as "outside", which lets the outline extend
        // under the fringe and eliminates edge gaps. Both buffers are read in full later and
        // their borders are never written here, so the rented buffers are cleared first.
        var expandedAlpha = ArrayPool<byte>.Shared.Rent(size);
        var binaryAlpha = ArrayPool<byte>.Shared.Rent(size);
        var squaredDist = ArrayPool<float>.Shared.Rent(size);
        try
        {
            Array.Clear(expandedAlpha, 0, size);
            Array.Clear(binaryAlpha, 0, size);

            for (var y = 0; y < height; y++)
            {
                var rowStart = (y + ow) * dstW + ow;
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = y * pitch + x;
                    var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                    expandedAlpha[rowStart + x] = alpha;
                    binaryAlpha[rowStart + x] = alpha >= 32 ? (byte)255 : (byte)0;
                }
            }

            OutlineDistanceTransform.ComputeSquared(binaryAlpha, dstW, dstH, _shape, squaredDist);

            // Step 2: Build RGBA output with outline color and anti-aliased alpha.
            // Two cases per pixel:
            //   - Body (under the glyph fill): full-opacity backing so alpha-over compositing
            //     has no seam behind the body's anti-aliased edge.
            //   - Everything else (exterior ring AND interior counters): a distance-based
            //     anti-aliased ring. The falloff fades to zero beyond ~ow pixels from the
            //     glyph core, so the open center of a large counter stays transparent on its
            //     own -- no flood-fill needed -- while pixels near an inner stroke wall get
            //     a proper inner outline.
            var dst = new byte[size * 4];
            var ring = _smooth ? SmoothRingAlpha(ow) : null;
            var hardLimit = (float)ow * ow;

            for (var pixelIdx = 0; pixelIdx < size; pixelIdx++)
            {
                float outlineAlpha;
                if (expandedAlpha[pixelIdx] > 0)
                {
                    // Body area: fill with full outline alpha so there's no seam
                    // when the body composites on top.
                    outlineAlpha = 255f;
                }
                else if (ring != null)
                {
                    // Exterior or counter: distance-based anti-aliased ring.
                    var sq = squaredDist[pixelIdx];
                    if (sq >= ring.Length)
                        continue; // at or past ow + 0.75 pixels: transparent
                    var k = (int)sq;
                    outlineAlpha = k == sq ? ring[k] : SmoothAlpha(ow, sq);
                }
                else
                {
                    // No anti-aliasing: hard threshold so Outline = N is exactly N solid pixels.
                    outlineAlpha = squaredDist[pixelIdx] <= hardLimit ? 255f : 0f;
                }

                if (outlineAlpha <= 0)
                    continue;

                var idx = pixelIdx * 4;
                dst[idx + 0] = _outlineR;
                dst[idx + 1] = _outlineG;
                dst[idx + 2] = _outlineB;
                dst[idx + 3] = (byte)outlineAlpha;
            }

            return new GlyphLayer(dst, dstW, dstH, OffsetX: -ow, OffsetY: -ow, ZOrder);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(expandedAlpha);
            ArrayPool<byte>.Shared.Return(binaryAlpha);
            ArrayPool<float>.Shared.Return(squaredDist);
        }
    }

    /// <summary>
    /// The smooth ring's alpha at a squared distance: fully opaque up to ow, then a linear fade
    /// over 1.5 pixels. Every smooth ring alpha comes from this expression, directly or through
    /// <see cref="SmoothRingAlpha"/>.
    /// </summary>
    private static float SmoothAlpha(int ow, float squaredDistance) =>
        Math.Clamp(255f * (ow + 0.75f - MathF.Sqrt(squaredDistance)) / 1.5f, 0f, 255f);

    [ThreadStatic]
    private static float[]? t_ringTable;

    [ThreadStatic]
    private static int t_ringTableWidth;

    /// <summary>
    /// <see cref="SmoothAlpha"/> at each whole squared distance below the first whose square
    /// root reaches <c>ow + 0.75</c>. The distance transforms yield whole squared distances
    /// (squared integer offsets plus whole column distances), so a lookup returns exactly what
    /// the expression would; at or past the table's length the root is at least
    /// <c>ow + 0.75</c> (the square root is monotone), where the expression is 0 or -0 and the
    /// pixel stays transparent. A fractional squared distance, should one occur, is evaluated
    /// directly.
    /// </summary>
    private static float[] SmoothRingAlpha(int ow)
    {
        if (t_ringTable != null && t_ringTableWidth == ow)
            return t_ringTable;

        var limit = ow + 0.75f;
        var length = 0;
        while (MathF.Sqrt(length) < limit)
            length++;

        var table = new float[length];
        for (var i = 0; i < length; i++)
            table[i] = SmoothAlpha(ow, i);

        t_ringTable = table;
        t_ringTableWidth = ow;
        return table;
    }
}
