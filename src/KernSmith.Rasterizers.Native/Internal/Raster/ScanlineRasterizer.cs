using System.Buffers;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// Turns directed <see cref="EdgeSegment"/>s into an 8-bit coverage bitmap using signed-area
/// trapezoid accumulation — the method stb_truetype v2 and font-rs use.
/// </summary>
/// <remarks>
/// Each scanline is a 1-pixel-tall band. Every edge crossing the band is clipped to it and its
/// signed height is distributed across the columns it sweeps: the fraction that falls inside a
/// column lands in <c>area</c>, and the whole height lands in <c>cover</c> at the first column
/// completely to its right. Sweeping the row left to right and running <c>cover</c> as a prefix
/// sum then yields each pixel's exact fractional coverage. The sign carries the winding, so
/// opposed contours cancel (holes) and the absolute value is the non-zero winding fill.
/// </remarks>
internal static class ScanlineRasterizer
{
    /// <summary>
    /// Rasterizes flattened edges into a fresh grayscale bitmap.
    /// </summary>
    /// <param name="edges">Directed edges in pixel space, as produced by <see cref="OutlineFlattener"/>.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="originX">Pixel-space X that maps to bitmap column 0.</param>
    /// <param name="originY">Pixel-space Y that maps to bitmap row 0.</param>
    /// <param name="antiAlias">
    /// <see cref="AntiAliasMode.None"/> thresholds coverage at 128; every other mode writes the
    /// grayscale coverage through unchanged.
    /// </param>
    /// <param name="bearingX">Carried through to <see cref="RasterResult.BearingX"/>.</param>
    /// <param name="bearingY">Carried through to <see cref="RasterResult.BearingY"/>.</param>
    /// <remarks>Anything falling outside the bitmap is clipped, so oversized or misplaced
    /// outlines cannot overrun the buffers.</remarks>
    public static RasterResult Rasterize(
        EdgeSegment[] edges,
        int width,
        int height,
        float originX = 0f,
        float originY = 0f,
        AntiAliasMode antiAlias = AntiAliasMode.Grayscale,
        int bearingX = 0,
        int bearingY = 0)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        // Never pooled: the caller keeps this for the glyph's lifetime.
        var bitmap = new byte[width * height];

        if (width == 0 || height == 0 || edges.Length == 0)
            return new RasterResult(bitmap, width, height, bearingX, bearingY);

        var prepared = Prepare(edges, originX, originY, height, out int count);
        if (count > 0)
            Fill(bitmap, prepared, count, width, height, antiAlias);

        return new RasterResult(bitmap, width, height, bearingX, bearingY);
    }

    /// <summary>
    /// Translates edges into bitmap space, drops the ones no scanline can see, and sorts what
    /// remains by top Y (then by X) so the active set can be filled by a single forward walk.
    /// </summary>
    private static RasterEdge[] Prepare(EdgeSegment[] edges, float originX, float originY, int height, out int count)
    {
        var prepared = new RasterEdge[edges.Length];
        count = 0;

        foreach (var edge in edges)
        {
            float x0 = edge.X0 - originX, y0 = edge.Y0 - originY;
            float x1 = edge.X1 - originX, y1 = edge.Y1 - originY;

            // Horizontal edges cross no scanline and carry no signed height. Non-finite ones
            // would poison the accumulator (inf/inf is NaN, and NaN spreads across the row).
            if (y0 == y1 || !float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1))
                continue;

            float direction = 1f;
            if (y0 > y1)
            {
                direction = -1f;
                (x0, y0, x1, y1) = (x1, y1, x0, y0);
            }

            if (y1 <= 0f || y0 >= height)
                continue;

            prepared[count++] = new RasterEdge(x0, y0, x1, y1, direction);
        }

        Array.Sort(prepared, 0, count, RasterEdge.ByTopY);
        return prepared;
    }

    private static void Fill(byte[] bitmap, RasterEdge[] edges, int count, int width, int height, AntiAliasMode antiAlias)
    {
        var pool = ArrayPool<float>.Shared;
        // Two spare cells: an edge ending in the last column parks its carry one past it.
        int bufferLength = width + 2;
        float[] area = pool.Rent(bufferLength);
        float[] cover = pool.Rent(bufferLength);
        var active = new RasterEdge[count];

        try
        {
            // Rented arrays arrive with whatever the previous tenant left behind.
            Array.Clear(area, 0, bufferLength);
            Array.Clear(cover, 0, bufferLength);

            int nextEdge = 0;
            int activeCount = 0;

            for (int y = 0; y < height; y++)
            {
                float bandTop = y;
                float bandBottom = y + 1f;

                while (nextEdge < count && edges[nextEdge].YTop < bandBottom)
                    active[activeCount++] = edges[nextEdge++];

                for (int i = 0; i < activeCount;)
                {
                    if (active[i].YBottom <= bandTop)
                        active[i] = active[--activeCount];
                    else
                        i++;
                }

                if (activeCount == 0)
                {
                    // Nothing left below either: the rest of the bitmap stays blank.
                    if (nextEdge >= count)
                        break;

                    continue;
                }

                for (int i = 0; i < activeCount; i++)
                {
                    ref readonly var edge = ref active[i];
                    float yEnter = MathF.Max(edge.YTop, bandTop);
                    float yExit = MathF.Min(edge.YBottom, bandBottom);
                    if (yExit <= yEnter)
                        continue;

                    Accumulate(
                        area,
                        cover,
                        width,
                        edge.XAt(yEnter),
                        edge.XAt(yExit),
                        (yExit - yEnter) * edge.Direction);
                }

                Sweep(bitmap, area, cover, y * width, width, antiAlias);
                // Sweep leaves the visible columns at zero; the carry cells still need clearing.
                area[width] = 0f;
                area[width + 1] = 0f;
                cover[width] = 0f;
                cover[width + 1] = 0f;
            }
        }
        finally
        {
            pool.Return(area);
            pool.Return(cover);
        }
    }

    /// <summary>
    /// Distributes one band-clipped edge's signed height <paramref name="d"/> across the columns
    /// it sweeps between <paramref name="xEnter"/> and <paramref name="xExit"/>.
    /// </summary>
    private static void Accumulate(float[] area, float[] cover, int width, float xEnter, float xExit, float d)
    {
        float x0 = MathF.Min(xEnter, xExit);
        float x1 = MathF.Max(xEnter, xExit);

        // Everything to the left of the bitmap covers every visible column completely.
        if (x1 <= 0f)
        {
            cover[0] += d;
            return;
        }

        // Everything to the right only affects columns that are not rendered.
        if (x0 >= width)
            return;

        float span = x1 - x0;
        if (span > 0f)
        {
            float low = MathF.Max(x0, 0f);
            float high = MathF.Min(x1, width);

            // Split rather than clamp: the piece left of the bitmap keeps its full weight,
            // the piece past the right edge is discarded, and only the middle is distributed.
            if (x0 < 0f)
                cover[0] += d * ((low - x0) / span);

            d *= (high - low) / span;
            x0 = low;
            x1 = high;
            span = x1 - x0;
        }

        float x0Floor = MathF.Floor(x0);
        int x0i = (int)x0Floor;
        float x1Ceiling = MathF.Ceiling(x1);
        int x1i = (int)x1Ceiling;

        if (x1i <= x0i + 1)
        {
            // Confined to one column: coverage is the mean distance from its right-hand edge.
            float midpoint = (0.5f * (x0 + x1)) - x0Floor;
            area[x0i] += d * (1f - midpoint);
            cover[x0i + 1] += d;
            return;
        }

        // Cumulative coverage fractions across the swept columns. `a0` is the triangle in the
        // first column, `am` the triangle in the last, and the columns between accumulate a
        // constant slice of the span each.
        float s = 1f / span;
        float x0Fraction = x0 - x0Floor;
        float a0 = 0.5f * s * (1f - x0Fraction) * (1f - x0Fraction);
        float x1Fraction = x1 - x1Ceiling + 1f;
        float am = 0.5f * s * x1Fraction * x1Fraction;

        area[x0i] += d * a0;

        if (x1i == x0i + 2)
        {
            area[x0i + 1] += d * (1f - am);
        }
        else
        {
            float a1 = s * (1.5f - x0Fraction);
            area[x0i + 1] += d * a1;

            for (int x = x0i + 2; x < x1i - 1; x++)
                area[x] += d * (a1 + ((x - x0i - 1) * s));

            area[x1i - 1] += d * (1f - am);
        }

        cover[x1i] += d;
    }

    /// <summary>
    /// Walks one row left to right, turning the accumulated area and running cover into bytes
    /// and resetting the buffers for the next scanline.
    /// </summary>
    private static void Sweep(byte[] bitmap, float[] area, float[] cover, int rowStart, int width, AntiAliasMode antiAlias)
    {
        float runningCover = 0f;
        bool threshold = antiAlias == AntiAliasMode.None;

        for (int x = 0; x < width; x++)
        {
            runningCover += cover[x];
            float coverage = MathF.Abs(area[x] + runningCover);

            // Overlapping contours push the winding number above 1, so clamp rather than wrap.
            int value = (int)MathF.Min(coverage * 255f, 255f);
            bitmap[rowStart + x] = threshold
                ? (value >= 128 ? (byte)255 : (byte)0)
                : (byte)value;

            area[x] = 0f;
            cover[x] = 0f;
        }
    }

    /// <summary>An edge normalized to run downward, with its winding kept in the sign.</summary>
    private readonly struct RasterEdge
    {
        /// <summary>Orders edges by the scanline they become active on, then left to right.</summary>
        public static readonly IComparer<RasterEdge> ByTopY = new TopYComparer();

        public RasterEdge(float xTop, float yTop, float xBottom, float yBottom, float direction)
        {
            XTop = xTop;
            YTop = yTop;
            XBottom = xBottom;
            YBottom = yBottom;
            Direction = direction;
            DxDy = (xBottom - xTop) / (yBottom - yTop);
        }

        public float XTop { get; }

        public float YTop { get; }

        public float XBottom { get; }

        public float YBottom { get; }

        /// <summary>+1 when the edge originally ran downward, -1 when it ran upward.</summary>
        public float Direction { get; }

        private float DxDy { get; }

        /// <summary>X where the edge crosses <paramref name="y"/>, kept inside its own X extent
        /// so a near-horizontal edge cannot drift outside the segment through rounding.</summary>
        public float XAt(float y) =>
            Math.Clamp(
                XTop + ((y - YTop) * DxDy),
                MathF.Min(XTop, XBottom),
                MathF.Max(XTop, XBottom));

        private sealed class TopYComparer : IComparer<RasterEdge>
        {
            public int Compare(RasterEdge x, RasterEdge y)
            {
                int byTop = x.YTop.CompareTo(y.YTop);
                return byTop != 0 ? byTop : x.XTop.CompareTo(y.XTop);
            }
        }
    }
}
