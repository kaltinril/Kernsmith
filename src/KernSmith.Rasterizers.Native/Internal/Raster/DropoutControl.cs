using System.Buffers;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// Dropout control for thresholded (<see cref="AntiAliasMode.None"/>) output: restores ink that
/// a stroke thinner than a pixel, or one straddling two pixels evenly, loses to the 50% coverage
/// threshold.
/// </summary>
/// <remarks>
/// <para>
/// The rule follows the scan-conversion dropout rules in the TrueType specification
/// (<c>SCANCTRL</c>/<c>SCANTYPE</c>, "smart" dropout control including stubs): along each
/// scanline through pixel centres, horizontal (one per row) and vertical (one per column), the
/// outline's interior is a set of spans between an on-transition and an off-transition. A span
/// that lit none of the pixels it passes through has dropped out, and the pixel containing its
/// midpoint is turned on. The specification tests for "no pixel centre inside the span" because
/// it samples centres; this renderer thresholds area coverage instead, so the test is "no lit
/// pixel along the span", which also catches a one-pixel stroke split 50/50 across two columns.
/// Spans that already lit a pixel are left alone, so glyphs without dropouts render exactly as
/// plain thresholding does.
/// </para>
/// <para>
/// Cost is linear in edges plus scanline crossings: each edge is bucketed once under the
/// scanlines its extent reaches, so a scanline visits only the edges that can cross it. All
/// scratch is pooled; nothing is allocated per glyph.
/// </para>
/// </remarks>
internal static class DropoutControl
{
    /// <summary>
    /// Turns on the midpoint pixel of every interior span that lit nothing.
    /// </summary>
    /// <param name="bitmap">The thresholded bitmap (0 or 255), row-major, pitch = width.</param>
    /// <param name="edges">The outline's edges in pixel space.</param>
    /// <param name="width">Bitmap width.</param>
    /// <param name="height">Bitmap height.</param>
    /// <param name="originX">Pixel-space X of bitmap column 0.</param>
    /// <param name="originY">Pixel-space Y of bitmap row 0.</param>
    public static void Apply(byte[] bitmap, EdgeSegment[] edges, int width, int height, float originX, float originY)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(edges);

        if (width <= 0 || height <= 0 || edges.Length < 2)
            return;

        // Rows first: the column pass must see the pixels the row pass turned on.
        Pass(bitmap, edges, width, height, originX, originY, horizontal: true);
        Pass(bitmap, edges, width, height, originX, originY, horizontal: false);
    }

    /// <summary>
    /// Runs every scanline of one direction: rows (horizontal scanlines, crossed at some x) or
    /// columns (vertical scanlines, crossed at some y).
    /// </summary>
    private static void Pass(byte[] bitmap, EdgeSegment[] edges, int width, int height, float originX, float originY, bool horizontal)
    {
        int lines = horizontal ? height : width;
        float lineOrigin = horizontal ? originY : originX;

        // Bucket every edge under the scanlines it can reach (a counting sort). The pair count
        // comes first so one rental holds both the per-scanline offsets and the buckets: with
        // one array per element type in flight, every rental returns to the pool's per-thread
        // cache and the next glyph allocates nothing.
        long pairs = 0;
        foreach (var edge in edges)
        {
            if (Reach(edge, horizontal, lineOrigin, lines, out int first, out int last))
                pairs += last - first + 1;
        }

        if (pairs < 2)
            return;

        int[] index = ArrayPool<int>.Shared.Rent(checked(lines + 1 + (int)pairs));
        float[]? positions = null;
        Step[]? steps = null;
        try
        {
            // Count per scanline, prefix-sum the counts into start offsets, then fill.
            var starts = index.AsSpan(0, lines + 1);
            var bucket = index.AsSpan(lines + 1, (int)pairs);
            starts.Clear();
            foreach (var edge in edges)
            {
                if (Reach(edge, horizontal, lineOrigin, lines, out int first, out int last))
                {
                    for (int line = first; line <= last; line++)
                        starts[line + 1]++;
                }
            }

            int widest = 0;
            for (int line = 0; line < lines; line++)
            {
                widest = Math.Max(widest, starts[line + 1]);
                starts[line + 1] += starts[line];
            }

            if (widest < 2)
                return;

            positions = ArrayPool<float>.Shared.Rent(widest);
            steps = ArrayPool<Step>.Shared.Rent(widest);

            // Filling advances each scanline's start to the next one's, so shift them back after.
            for (int i = 0; i < edges.Length; i++)
            {
                if (Reach(edges[i], horizontal, lineOrigin, lines, out int first, out int last))
                {
                    for (int line = first; line <= last; line++)
                        bucket[starts[line]++] = i;
                }
            }

            for (int line = lines; line > 0; line--)
                starts[line] = starts[line - 1];
            starts[0] = 0;

            for (int line = 0; line < lines; line++)
            {
                int from = starts[line], to = starts[line + 1];
                if (to - from < 2)
                    continue;

                float at = line + 0.5f + lineOrigin;
                int count = Crossings(edges, bucket.Slice(from, to - from), at, horizontal, positions, steps);
                if (count < 2)
                    continue;

                // Ties need no particular order: Sweep applies equal positions together.
                positions.AsSpan(0, count).Sort(steps.AsSpan(0, count));
                Sweep(bitmap, positions.AsSpan(0, count), steps.AsSpan(0, count), line, width, height, originX, originY, horizontal);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(index);
            if (positions is not null)
                ArrayPool<float>.Shared.Return(positions);
            if (steps is not null)
                ArrayPool<Step>.Shared.Return(steps);
        }
    }

    /// <summary>
    /// The scanlines an edge's extent may contain, padded by one either side against float
    /// rounding; <see cref="Crossings"/> applies the exact test.
    /// </summary>
    /// <returns>False when the edge reaches no scanline of the bitmap.</returns>
    private static bool Reach(EdgeSegment e, bool horizontal, float lineOrigin, int lines, out int first, out int last)
    {
        float a0 = horizontal ? e.Y0 : e.X0, a1 = horizontal ? e.Y1 : e.X1;
        first = last = 0;

        // An edge running along the scanlines crosses none of them. Non-finite edges are
        // dropped, as the coverage fill drops them.
        if (a0.Equals(a1) || !float.IsFinite(a0) || !float.IsFinite(a1)
            || !float.IsFinite(horizontal ? e.X0 : e.Y0) || !float.IsFinite(horizontal ? e.X1 : e.Y1))
        {
            return false;
        }

        // Scanline n lies at n + 0.5 + lineOrigin. Doubles keep far-off coordinates from
        // overflowing the int conversion.
        double lo = Math.Floor(Math.Min(a0, a1) - (double)lineOrigin - 0.5) - 1;
        double hi = Math.Ceiling(Math.Max(a0, a1) - (double)lineOrigin - 0.5) + 1;
        if (hi < 0 || lo > lines - 1)
            return false;

        first = (int)Math.Max(lo, 0);
        last = (int)Math.Min(hi, lines - 1);
        return true;
    }

    /// <summary>
    /// Writes where the bucketed edges cross the scanline at <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// Each crossing carries two winding steps: one with the edge's extent half-open at its low
    /// end, one half-open at its high end, and only where both windings are non-zero is interior.
    /// The two agree everywhere except where the scanline runs exactly along the outline's
    /// boundary, so a scanline lying on an edge contributes no span of zero thickness, while a
    /// vertex sitting on the scanline is still counted exactly once.
    /// </remarks>
    /// <returns>The number of crossings written.</returns>
    private static int Crossings(EdgeSegment[] edges, ReadOnlySpan<int> bucket, float at, bool horizontal, float[] positions, Step[] steps)
    {
        int count = 0;
        foreach (int index in bucket)
        {
            var e = edges[index];

            // For a horizontal scanline the edge is parameterized by y and crossed at some x;
            // for a vertical one the roles swap.
            float a0 = horizontal ? e.Y0 : e.X0, a1 = horizontal ? e.Y1 : e.X1;
            float b0 = horizontal ? e.X0 : e.Y0, b1 = horizontal ? e.X1 : e.Y1;

            float min = MathF.Min(a0, a1), max = MathF.Max(a0, a1);
            bool low = at >= min && at < max;
            bool high = at > min && at <= max;
            if (!low && !high)
                continue;

            sbyte winding = a1 > a0 ? (sbyte)1 : (sbyte)-1;
            positions[count] = b0 + ((at - a0) * (b1 - b0) / (a1 - a0));
            steps[count] = new Step(low ? winding : (sbyte)0, high ? winding : (sbyte)0);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Walks one scanline's sorted crossings under the non-zero rule and turns on the midpoint
    /// pixel of every interior span that lit nothing.
    /// </summary>
    /// <remarks>
    /// Crossings at the same position are applied together, so contours that abut exactly (one
    /// ends where the next begins) form one span rather than two touching ones.
    /// </remarks>
    private static void Sweep(byte[] bitmap, ReadOnlySpan<float> positions, ReadOnlySpan<Step> steps, int line, int width, int height, float originX, float originY, bool horizontal)
    {
        int low = 0, high = 0;
        float start = 0f;
        int i = 0;
        while (i < positions.Length)
        {
            float position = positions[i];
            bool wasInside = low != 0 && high != 0;
            do
            {
                low += steps[i].Low;
                high += steps[i].High;
                i++;
            }
            while (i < positions.Length && positions[i].Equals(position));

            bool inside = low != 0 && high != 0;
            if (!wasInside && inside)
            {
                start = position;
            }
            else if (wasInside && !inside && position > start)
            {
                if (horizontal)
                    RestoreRowSpan(bitmap, line, start - originX, position - originX, width);
                else
                    RestoreColumnSpan(bitmap, line, start - originY, position - originY, width, height);
            }
        }
    }

    private static void RestoreRowSpan(byte[] bitmap, int row, float from, float to, int width)
    {
        int first = Math.Max(0, (int)MathF.Floor(from));
        int last = Math.Min(width - 1, (int)MathF.Ceiling(to) - 1);
        if (first > last || AnyLit(bitmap, (row * width) + first, 1, last - first + 1))
            return;

        int column = Math.Clamp((int)MathF.Floor((from + to) / 2f), first, last);
        bitmap[(row * width) + column] = 255;
    }

    private static void RestoreColumnSpan(byte[] bitmap, int column, float from, float to, int width, int height)
    {
        int first = Math.Max(0, (int)MathF.Floor(from));
        int last = Math.Min(height - 1, (int)MathF.Ceiling(to) - 1);
        if (first > last || AnyLit(bitmap, (first * width) + column, width, last - first + 1))
            return;

        int row = Math.Clamp((int)MathF.Floor((from + to) / 2f), first, last);
        bitmap[(row * width) + column] = 255;
    }

    private static bool AnyLit(byte[] bitmap, int index, int stride, int count)
    {
        for (int i = 0; i < count; i++, index += stride)
        {
            if (bitmap[index] != 0)
                return true;
        }

        return false;
    }

    /// <summary>One crossing's winding step under the low-open and the high-open extent.</summary>
    private readonly record struct Step(sbyte Low, sbyte High);
}
