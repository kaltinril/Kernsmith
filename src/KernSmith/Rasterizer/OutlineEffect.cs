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

    public (int Left, int Top, int Right, int Bottom) Margins =>
        (_outlineWidth, _outlineWidth, _outlineWidth, _outlineWidth);

    public GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics)
    {
        var ow = _outlineWidth;
        var dstW = width + 2 * ow;
        var dstH = height + 2 * ow;
        var size = dstW * dstH;

        // Step 1: Extract source alpha into an expanded buffer, centered. The buffers are read
        // in full later and their borders are never written here, so the rented buffers are
        // cleared first.
        var expandedAlpha = ArrayPool<byte>.Shared.Rent(size);
        var binaryAlpha = _smooth ? null : ArrayPool<byte>.Shared.Rent(size);
        var squaredDist = ArrayPool<float>.Shared.Rent(size);
        try
        {
            Array.Clear(expandedAlpha, 0, size);
            if (binaryAlpha != null)
                Array.Clear(binaryAlpha, 0, size);

            for (var y = 0; y < height; y++)
            {
                var rowStart = (y + ow) * dstW + ow;
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = y * pitch + x;
                    var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                    expandedAlpha[rowStart + x] = alpha;
                    if (binaryAlpha != null)
                        binaryAlpha[rowStart + x] = alpha >= HardOutlineCut ? (byte)255 : (byte)0;
                }
            }

            if (binaryAlpha == null)
            {
                // Anti-aliased: distance to the edge the glyph's coverage describes, so the ring
                // follows a curve between pixel rows instead of a thresholded silhouette (#127).
                // Nothing at or past the end of the ramp is needed.
                OutlineDistanceTransform.ComputeSquaredFromCoverage(expandedAlpha, dstW, dstH, _shape, ow + SmoothFade, squaredDist);
            }
            else
            {
                // No anti-aliasing: whole-pixel distance from the glyph's 50% contour. A bilevel
                // glyph is all 0 and 255, so the cut is its ink; a glyph that is not (a color or
                // bitmap glyph) is measured from the silhouette a bilevel renderer would have drawn.
                OutlineDistanceTransform.ComputeSquared(binaryAlpha, dstW, dstH, _shape, squaredDist);
            }

            // Step 2: Build RGBA output with outline color and anti-aliased alpha.
            // Two cases per pixel:
            //   - Body (under the glyph fill): full-opacity backing so alpha-over compositing
            //     has no seam behind the body's anti-aliased edge.
            //   - Everything else (exterior ring AND interior counters): a distance-based
            //     anti-aliased ring. The falloff fades to zero ow pixels from the
            //     glyph's edge, so the open center of a large counter stays transparent on its
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
                        continue; // at or past ow + 1 pixels: transparent
                    var k = (int)sq;
                    outlineAlpha = ((float)k).Equals(sq) ? ring[k] : SmoothAlpha(ow, sq);
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
            if (binaryAlpha != null)
                ArrayPool<byte>.Shared.Return(binaryAlpha);
            ArrayPool<float>.Shared.Return(squaredDist);
        }
    }

    /// <summary>
    /// The alpha a pixel needs to be part of the silhouette the hard (AntiAlias = None) outline
    /// is measured from: the 50% contour. Any cut gives the same silhouette for a bilevel glyph;
    /// a lower one turns the faint row at a gray glyph's curved apex into a solid row, and the
    /// outline into a flat cap over it (#127).
    /// </summary>
    internal const byte HardOutlineCut = 128;

    /// <summary>
    /// The smooth ring is transparent from this far past the outline width. Distances are
    /// between pixel centres, with a fully covered pixel's edge half a pixel past its centre,
    /// so a pixel at distance d holds the edge of the outline when d is between ow and ow + 1.
    /// </summary>
    private const float SmoothFade = 1f;

    /// <summary>
    /// The smooth ring's alpha at a squared distance: the share of the pixel that lies within
    /// ow of the glyph's edge -- opaque up to ow, then a linear fade over one pixel, so the ring
    /// covers ow pixels past the edge and is as sharp as the glyph's own anti-aliased edge.
    /// Every smooth ring alpha comes from this expression, directly or through
    /// <see cref="SmoothRingAlpha"/>.
    /// </summary>
    private static float SmoothAlpha(int ow, float squaredDistance) =>
        Math.Clamp(255f * (ow + SmoothFade - MathF.Sqrt(squaredDistance)), 0f, 255f);

    [ThreadStatic]
    private static float[]? t_ringTable;

    [ThreadStatic]
    private static int t_ringTableWidth;

    /// <summary>
    /// <see cref="SmoothAlpha"/> at each whole squared distance below the first whose square
    /// root reaches <c>ow + 1</c>. A distance measured from fully covered pixels is a whole
    /// squared distance (squared integer offsets plus whole column distances), so a lookup
    /// returns exactly what the expression would; at or past the table's length the root is at
    /// least <c>ow + 1</c> (the square root is monotone), where the expression is 0 or -0 and
    /// the pixel stays transparent. A distance measured from a partly covered edge pixel is
    /// fractional and is evaluated directly.
    /// </summary>
    private static float[] SmoothRingAlpha(int ow)
    {
        if (t_ringTable != null && t_ringTableWidth == ow)
            return t_ringTable;

        var limit = ow + SmoothFade;
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
