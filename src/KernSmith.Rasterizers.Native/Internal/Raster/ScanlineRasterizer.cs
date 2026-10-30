using System.Buffers;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// Turns directed <see cref="EdgeSegment"/>s into an 8-bit coverage bitmap by exact signed-area
/// accumulation (the general technique of Raph Levien's public write-up on accumulation-buffer
/// font rendering).
/// </summary>
/// <remarks>
/// <para>
/// Derivation. A pixel's coverage is the integral of the winding number <c>w(x, y)</c> over its
/// unit cell. Along a horizontal line at height <c>y</c>, <c>w</c> steps by the edge's direction
/// (±1) at each edge crossing <c>x_e(y)</c> and holds to the right of it, so over the cell
/// <c>[i, i+1]</c> a crossing contributes ± the fraction of the cell right of it,
/// <c>clamp(i + 1 - x_e(y), 0, 1)</c>. Coverage is that, integrated over the part of each edge
/// inside pixel row <c>j</c>.
/// </para>
/// <para>
/// Split the edge's piece in the row at every integer x, so each sub-piece lies in one column
/// <c>c</c>. Inside a column the integrand <c>c + 1 - x_e(y)</c> is linear in <c>y</c>, so its
/// integral is the sub-piece's signed height <c>h</c> times <c>c + 1 - m</c>, where <c>m</c> is
/// its mean x — the midpoint, since x is linear along the segment. Every column right of
/// <c>c</c> gets the full <c>h</c>. The heights of the sub-pieces are proportional to their
/// x-extents. With <c>f = m - c</c>, adding <c>h(1 - f)</c> to a per-row delta cell <c>D[c]</c>
/// and <c>h·f</c> to <c>D[c + 1]</c> makes every pixel's coverage the running sum
/// <c>D[0] + ... + D[i]</c>: both "inside this column" and "carried right" live in one buffer,
/// read by one prefix sum.
/// </para>
/// <para>
/// Pieces left of column 0 cover every visible column fully, so their height folds into
/// <c>D[0]</c>; pieces right of the bitmap affect nothing visible and are dropped. Rows never
/// interact, so the buffer holds a band of rows and the edges are walked once per band in input
/// order — no edge copies, sorting or active-edge list. The fill is non-zero: <c>|sum|</c>
/// clamped to 1, so opposed contours cancel (holes) and overlapping same-direction contours
/// saturate.
/// </para>
/// </remarks>
internal static class ScanlineRasterizer
{
    /// <summary>Delta cells per band (256 KB of floats): every glyph up to roughly 250 px
    /// square fills in one band, and larger ones never need an unbounded buffer.</summary>
    private const int BandCells = 1 << 16;

    /// <summary>
    /// Added before truncating coverage to a level. The deltas and the running sum carry float
    /// rounding, so a fully covered pixel can come out as 0.99999994; without the bias that
    /// truncates to 254 (a few percent of solid pixels, measured). Smaller biases leave such
    /// 254s on 600 px glyphs, whose shallow edges spread one row's height over hundreds of
    /// cells. A thousandth of a level cannot lift an exact half (127.5) to 128.
    /// </summary>
    private const float LevelBias = 1f / 1024f;

    /// <summary>
    /// Rasterizes flattened edges into a fresh grayscale bitmap.
    /// </summary>
    /// <param name="edges">Directed edges in pixel space, as produced by <see cref="OutlineFlattener"/>.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="originX">Pixel-space X that maps to bitmap column 0.</param>
    /// <param name="originY">Pixel-space Y that maps to bitmap row 0.</param>
    /// <param name="antiAlias">
    /// <see cref="AntiAliasMode.None"/> thresholds coverage at 128, then restores dropped-out
    /// thin strokes (<see cref="DropoutControl"/>); every other mode writes the grayscale
    /// coverage through unchanged.
    /// </param>
    /// <param name="bearingX">Carried through to <see cref="RasterResult.BearingX"/>.</param>
    /// <param name="bearingY">Carried through to <see cref="RasterResult.BearingY"/>.</param>
    /// <remarks>Anything falling outside the bitmap is clipped, so oversized or misplaced
    /// outlines cannot overrun the buffers. The only allocation is the returned bitmap.</remarks>
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

        if (Fill(bitmap, edges, width, height, originX, originY, antiAlias) && antiAlias == AntiAliasMode.None)
            DropoutControl.Apply(bitmap, edges, width, height, originX, originY);

        return new RasterResult(bitmap, width, height, bearingX, bearingY);
    }

    /// <summary>
    /// Renders <see cref="AntiAliasMode.None"/> output — thresholded, with dropout control — in
    /// the given box and returns it cropped to its ink, bearings moved to match.
    /// </summary>
    /// <param name="edges">Directed edges in pixel space.</param>
    /// <param name="width">Width of the box to render in; it must hold every partially covered pixel.</param>
    /// <param name="height">Height of the box to render in.</param>
    /// <param name="originX">Pixel-space X of the box's column 0.</param>
    /// <param name="originY">Pixel-space Y of the box's row 0.</param>
    /// <param name="bearingX">The box's bearing; the result's moves right by the empty columns cropped.</param>
    /// <param name="bearingY">The box's bearing; the result's moves down by the empty rows cropped.</param>
    /// <remarks>
    /// The box an outline rounds out to keeps every partially covered pixel, but thresholding at
    /// 50% empties an edge row or column wherever the outline covers under half of it. Only
    /// empty rows and columns are cropped, so every lit pixel — dropout control's included —
    /// stays where it was; rounding the box to the nearest pixel instead would cut off a dropout
    /// pixel restored in a column the outline covers under half of. A render with no ink keeps
    /// its box. The only allocation is the returned bitmap.
    /// </remarks>
    public static RasterResult RasterizeMono(EdgeSegment[] edges, int width, int height, float originX, float originY, int bearingX, int bearingY) =>
        Mono(edges, width, height, originX, originY, bearingX, bearingY, keepBitmap: true);

    /// <summary>
    /// The size and bearings <see cref="RasterizeMono"/> would return, with an empty bitmap. It
    /// still renders to find the ink, into pooled scratch only.
    /// </summary>
    public static RasterResult MeasureMono(EdgeSegment[] edges, int width, int height, float originX, float originY, int bearingX, int bearingY) =>
        Mono(edges, width, height, originX, originY, bearingX, bearingY, keepBitmap: false);

    private static RasterResult Mono(EdgeSegment[] edges, int width, int height, float originX, float originY, int bearingX, int bearingY, bool keepBitmap)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        int size = width * height;
        byte[] scratch = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
        try
        {
            // Fill skips untouched bands, so the rented buffer must start zeroed.
            var full = scratch.AsSpan(0, size);
            full.Clear();
            if (size > 0 && edges.Length > 0 && Fill(scratch, edges, width, height, originX, originY, AntiAliasMode.None))
                DropoutControl.Apply(scratch, edges, width, height, originX, originY);

            int top = full.IndexOfAnyExcept((byte)0);
            if (top < 0)
                return new RasterResult(keepBitmap ? new byte[size] : [], width, height, bearingX, bearingY);

            top /= width;
            int bottom = full.LastIndexOfAnyExcept((byte)0) / width;
            int left = width, right = 0;
            for (int row = top; row <= bottom; row++)
            {
                var line = full.Slice(row * width, width);
                int first = line.IndexOfAnyExcept((byte)0);
                if (first < 0)
                    continue;
                left = Math.Min(left, first);
                right = Math.Max(right, line.LastIndexOfAnyExcept((byte)0));
            }

            int croppedWidth = right - left + 1, croppedHeight = bottom - top + 1;
            byte[] bitmap = [];
            if (keepBitmap)
            {
                bitmap = new byte[croppedWidth * croppedHeight];
                for (int row = 0; row < croppedHeight; row++)
                    full.Slice(((top + row) * width) + left, croppedWidth).CopyTo(bitmap.AsSpan(row * croppedWidth));
            }

            return new RasterResult(bitmap, croppedWidth, croppedHeight, bearingX + left, bearingY - top);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>
    /// Accumulates every edge into a pooled delta buffer one band of rows at a time and resolves
    /// each band into <paramref name="bitmap"/>.
    /// </summary>
    /// <returns>Whether any edge reached a row of the bitmap.</returns>
    private static bool Fill(byte[] bitmap, EdgeSegment[] edges, int width, int height, float originX, float originY, AntiAliasMode antiAlias)
    {
        // One spare cell per row: a piece in the last column parks its carry share past it.
        int stride = width + 1;
        int bandRows = Math.Clamp(BandCells / stride, 1, height);
        float[] cells = ArrayPool<float>.Shared.Rent(stride * bandRows);
        bool anyEdge = false;
        try
        {
            for (int bandTop = 0; bandTop < height; bandTop += bandRows)
            {
                int rows = Math.Min(bandRows, height - bandTop);
                var band = cells.AsSpan(0, rows * stride);

                // Rented arrays arrive with whatever the previous tenant left behind.
                band.Clear();

                bool touched = false;
                foreach (var edge in edges)
                    touched |= AccumulateEdge(band, stride, width, bandTop, rows, edge, originX, originY);

                // An untouched band resolves to zero, which the fresh bitmap already holds.
                if (touched)
                    Resolve(bitmap, band, stride, width, bandTop, rows, antiAlias == AntiAliasMode.None);

                anyEdge |= touched;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(cells);
        }

        return anyEdge;
    }

    /// <summary>
    /// Clips one edge to the band's rows and accumulates its piece in each row.
    /// </summary>
    /// <returns>Whether the edge reached any row of the band.</returns>
    private static bool AccumulateEdge(Span<float> band, int stride, int width, int bandTop, int rows, EdgeSegment edge, float originX, float originY)
    {
        float xTop = edge.X0 - originX, yTop = edge.Y0 - originY;
        float xBottom = edge.X1 - originX, yBottom = edge.Y1 - originY;

        // Non-finite coordinates would poison the running sum (inf - inf is NaN, and NaN spreads
        // across the row). Horizontal edges cross no row and carry no signed height. Exact
        // comparison is required: the fill relies on signed dY summing to zero over a closed
        // contour, so an epsilon here would break closure and cause fill artifacts. Equals is
        // bitwise `==` (plus NaN==NaN, unreachable past the finite check).
        if (!float.IsFinite(xTop) || !float.IsFinite(yTop) || !float.IsFinite(xBottom) || !float.IsFinite(yBottom)
            || yTop.Equals(yBottom))
        {
            return false;
        }

        float direction = 1f;
        if (yTop > yBottom)
        {
            (xTop, xBottom) = (xBottom, xTop);
            (yTop, yBottom) = (yBottom, yTop);
            direction = -1f;
        }

        int bandBottom = bandTop + rows;
        if (yBottom <= bandTop || yTop >= bandBottom)
            return false;

        float dxdy = (xBottom - xTop) / (yBottom - yTop);
        float xMin = MathF.Min(xTop, xBottom), xMax = MathF.Max(xTop, xBottom);

        // Clamped before the int conversion, so far-off coordinates cannot overflow it.
        float yEnter = MathF.Max(yTop, bandTop);
        int firstRow = (int)yEnter;
        int endRow = (int)MathF.Ceiling(MathF.Min(yBottom, bandBottom));

        // Exact endpoints where the edge starts or ends inside the band; interpolated (and kept
        // inside the edge's own X extent against rounding) where a row boundary cuts it.
        float xEnter = yEnter > yTop ? Math.Clamp(xTop + ((yEnter - yTop) * dxdy), xMin, xMax) : xTop;
        for (int row = firstRow; row < endRow; row++)
        {
            float yExit = MathF.Min(yBottom, row + 1);
            float xExit = yExit < yBottom ? Math.Clamp(xTop + ((yExit - yTop) * dxdy), xMin, xMax) : xBottom;

            AccumulatePiece(band.Slice((row - bandTop) * stride, stride), width, xEnter, xExit, direction * (yExit - yEnter));

            yEnter = yExit;
            xEnter = xExit;
        }

        return true;
    }

    /// <summary>
    /// Adds the deltas of one straight piece lying inside a single pixel row, running from
    /// <paramref name="xA"/> to <paramref name="xB"/> with signed height <paramref name="h"/>.
    /// </summary>
    private static void AccumulatePiece(Span<float> row, int width, float xA, float xB, float h)
    {
        // Only the x-extent matters: heights split in proportion to it whichever way it runs.
        float xLeft = MathF.Min(xA, xB), xRight = MathF.Max(xA, xB);

        if (xRight <= 0f)
        {
            row[0] += h;
            return;
        }

        if (xLeft >= width)
            return;

        if (xLeft < 0f)
        {
            float leftShare = h * (-xLeft / (xRight - xLeft));
            row[0] += leftShare;
            h -= leftShare;
            xLeft = 0f;
        }

        int column = (int)xLeft;
        if (xRight <= column + 1)
        {
            float f = (0.5f * (xLeft + xRight)) - column;
            row[column] += h * (1f - f);
            row[column + 1] += h * f;
            return;
        }

        float heightPerX = h / (xRight - xLeft);

        // First, partial column: from xLeft to its right boundary.
        float lead = column + 1 - xLeft;
        float leadHeight = heightPerX * lead;
        float leadF = 1f - (0.5f * lead);
        row[column] += leadHeight * (1f - leadF);
        row[column + 1] += leadHeight * leadF;

        // Fully crossed columns have f = 1/2: half of heightPerX lands in the column and half in
        // the next, so consecutive columns merge into one whole heightPerX each.
        float xEnd = MathF.Min(xRight, width);
        int fullEnd = (int)xEnd;
        int next = column + 1;
        if (next < fullEnd)
        {
            float half = 0.5f * heightPerX;
            row[next] += half;
            for (int k = next + 1; k < fullEnd; k++)
                row[k] += heightPerX;
            row[fullEnd] += half;
        }

        // Last, partial column: from its left boundary to xRight (nothing when past the bitmap).
        float tail = xEnd - fullEnd;
        if (tail > 0f)
        {
            float tailHeight = heightPerX * tail;
            float tailF = 0.5f * tail;
            row[fullEnd] += tailHeight * (1f - tailF);
            row[fullEnd + 1] += tailHeight * tailF;
        }
    }

    /// <summary>
    /// Runs each band row's prefix sum and writes the clamped non-zero coverage as bytes.
    /// </summary>
    private static void Resolve(byte[] bitmap, Span<float> band, int stride, int width, int bandTop, int rows, bool threshold)
    {
        // Four rows at a time: a row's running sum is one serial chain of float adds, so
        // interleaving independent rows keeps the adder busy (measured about 2x on the resolve
        // pass). A Vector<float> conversion pass on top measured within noise, so it is not used.
        int r = 0;
        for (; r + 4 <= rows; r += 4)
        {
            var d0 = band.Slice(r * stride, width);
            var d1 = band.Slice((r + 1) * stride, width);
            var d2 = band.Slice((r + 2) * stride, width);
            var d3 = band.Slice((r + 3) * stride, width);
            var o0 = bitmap.AsSpan((bandTop + r) * width, width);
            var o1 = bitmap.AsSpan((bandTop + r + 1) * width, width);
            var o2 = bitmap.AsSpan((bandTop + r + 2) * width, width);
            var o3 = bitmap.AsSpan((bandTop + r + 3) * width, width);
            float s0 = 0f, s1 = 0f, s2 = 0f, s3 = 0f;
            for (int x = 0; x < width; x++)
            {
                s0 += d0[x];
                s1 += d1[x];
                s2 += d2[x];
                s3 += d3[x];
                o0[x] = Level(s0);
                o1[x] = Level(s1);
                o2[x] = Level(s2);
                o3[x] = Level(s3);
            }
        }

        for (; r < rows; r++)
        {
            var deltas = band.Slice(r * stride, width);
            var output = bitmap.AsSpan((bandTop + r) * width, width);
            float sum = 0f;
            for (int x = 0; x < width; x++)
            {
                sum += deltas[x];
                output[x] = Level(sum);
            }
        }

        if (threshold)
        {
            // value >= 128 exactly when its top bit is set; the arithmetic shift spreads that bit
            // to 255 or 0 without a branch (measured about 2x the conditional on 72 px glyphs,
            // whose edge pixels defeat the branch predictor).
            foreach (ref byte value in bitmap.AsSpan(bandTop * width, rows * width))
                value = (byte)((sbyte)value >> 7);
        }
    }

    /// <summary>Non-zero coverage of a running sum, truncated to an 8-bit level.</summary>
    private static byte Level(float sum) => (byte)((MathF.Min(MathF.Abs(sum), 1f) * 255f) + LevelBias);
}
