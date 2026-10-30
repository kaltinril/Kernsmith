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

        // Each edge is reduced once to its oriented form and scanline range; the counting sort
        // and every scanline read that instead of re-deriving it.
        Oriented[] oriented = ArrayPool<Oriented>.Shared.Rent(edges.Length);
        int[]? index = null;
        float[]? positions = null;
        Step[]? steps = null;
        try
        {
            long pairs = 0;
            int kept = 0;
            foreach (var edge in edges)
            {
                if (Reach(edge, horizontal, lineOrigin, lines, out int first, out int last))
                {
                    oriented[kept++] = new Oriented(edge, horizontal, first, last);
                    pairs += last - first + 1;
                }
            }

            if (pairs < 2)
                return;

            // The pair count comes first so one rental holds both the per-scanline offsets and
            // the buckets: with one array per element type in flight, every rental returns to
            // the pool's per-thread cache and the next glyph allocates nothing.
            index = ArrayPool<int>.Shared.Rent(checked(lines + 1 + (int)pairs));

            // Count per scanline as a difference array, then turn the running count into start
            // offsets in the same sweep.
            var starts = index.AsSpan(0, lines + 1);
            var bucket = index.AsSpan(lines + 1, (int)pairs);
            starts.Clear();
            for (int i = 0; i < kept; i++)
            {
                starts[oriented[i].First]++;
                starts[oriented[i].Last + 1]--;
            }

            int widest = 0, running = 0, offset = 0;
            for (int line = 0; line < lines; line++)
            {
                running += starts[line];
                starts[line] = offset;
                offset += running;
                widest = Math.Max(widest, running);
            }

            starts[lines] = offset;
            if (widest < 2)
                return;

            positions = ArrayPool<float>.Shared.Rent(widest);
            steps = ArrayPool<Step>.Shared.Rent(widest);

            // Filling advances each scanline's start to the next one's, so shift them back after.
            for (int i = 0; i < kept; i++)
            {
                for (int line = oriented[i].First, last = oriented[i].Last; line <= last; line++)
                    bucket[starts[line]++] = i;
            }

            for (int line = lines; line > 0; line--)
                starts[line] = starts[line - 1];
            starts[0] = 0;

            for (int line = 0; line < lines; line++)
            {
                int from = starts[line], to = starts[line + 1];
                if (to - from < 2)
                    continue;

                float at = ScanlineAt(line, lineOrigin);
                int count = Crossings(oriented, bucket.Slice(from, to - from), at, positions, steps);
                if (count < 2)
                    continue;

                // Ties need no particular order: Sweep applies equal positions together.
                Sort(positions.AsSpan(0, count), steps.AsSpan(0, count));
                Sweep(bitmap, positions.AsSpan(0, count), steps.AsSpan(0, count), line, width, height, originX, originY, horizontal);
            }
        }
        finally
        {
            ArrayPool<Oriented>.Shared.Return(oriented);
            if (index is not null)
                ArrayPool<int>.Shared.Return(index);
            if (positions is not null)
                ArrayPool<float>.Shared.Return(positions);
            if (steps is not null)
                ArrayPool<Step>.Shared.Return(steps);
        }
    }

    /// <summary>
    /// The scanlines whose position lies inside an edge's extent, the only ones
    /// <see cref="Crossings"/> can find it crossing.
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

        // The estimate is padded by one either side against float rounding; trim it to the
        // scanlines the crossing test accepts, positioned exactly as Pass positions them.
        float min = MathF.Min(a0, a1), max = MathF.Max(a0, a1);
        first = (int)Math.Max(lo, 0);
        last = (int)Math.Min(hi, lines - 1);
        while (first <= last && ScanlineAt(first, lineOrigin) < min)
            first++;
        while (last >= first && ScanlineAt(last, lineOrigin) > max)
            last--;
        return first <= last;
    }

    /// <summary>Where scanline <paramref name="line"/> lies; <see cref="Pass"/> and
    /// <see cref="Reach"/> must place it identically.</summary>
    private static float ScanlineAt(int line, float lineOrigin) => line + 0.5f + lineOrigin;

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
    private static int Crossings(Oriented[] edges, ReadOnlySpan<int> bucket, float at, float[] positions, Step[] steps)
    {
        int count = 0;
        foreach (int index in bucket)
        {
            ref readonly var e = ref edges[index];
            bool low = at >= e.Min && at < e.Max;
            bool high = at > e.Min && at <= e.Max;
            if (!low && !high)
                continue;

            positions[count] = e.B0 + ((at - e.A0) * e.DeltaB / e.DeltaA);
            steps[count] = new Step(low ? e.Winding : (sbyte)0, high ? e.Winding : (sbyte)0);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Sorts a scanline's crossings by position, in <see cref="float.CompareTo(float)"/> order.
    /// A scanline holds a handful of crossings, so an insertion sort beats the general sort's
    /// setup; long scanlines go to the general sort.
    /// </summary>
    private static void Sort(Span<float> positions, Span<Step> steps)
    {
        if (positions.Length > InsertionSortLimit)
        {
            positions.Sort(steps);
            return;
        }

        for (int i = 1; i < positions.Length; i++)
        {
            float key = positions[i];
            var step = steps[i];
            int j = i - 1;
            while (j >= 0 && positions[j].CompareTo(key) > 0)
            {
                positions[j + 1] = positions[j];
                steps[j + 1] = steps[j];
                j--;
            }

            positions[j + 1] = key;
            steps[j + 1] = step;
        }
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

    /// <summary>Crossings per scanline above which <see cref="Sort"/> hands over to the general sort.</summary>
    private const int InsertionSortLimit = 16;

    /// <summary>
    /// An edge as one pass sees it: <c>A</c> runs across the scanlines, <c>B</c> along them, and
    /// <see cref="First"/>..<see cref="Last"/> are the scanlines <see cref="Reach"/> gave it. The
    /// deltas are the same float subtractions the crossing formula makes, so a crossing comes
    /// out bit-identical whichever form it is computed from.
    /// </summary>
    private readonly struct Oriented
    {
        public readonly float A0, Min, Max, B0, DeltaA, DeltaB;
        public readonly int First, Last;
        public readonly sbyte Winding;

        public Oriented(EdgeSegment e, bool horizontal, int first, int last)
        {
            float a0 = horizontal ? e.Y0 : e.X0, a1 = horizontal ? e.Y1 : e.X1;
            float b0 = horizontal ? e.X0 : e.Y0, b1 = horizontal ? e.X1 : e.Y1;
            A0 = a0;
            B0 = b0;
            Min = MathF.Min(a0, a1);
            Max = MathF.Max(a0, a1);
            DeltaA = a1 - a0;
            DeltaB = b1 - b0;
            Winding = a1 > a0 ? (sbyte)1 : (sbyte)-1;
            First = first;
            Last = last;
        }
    }

    /// <summary>One crossing's winding step under the low-open and the high-open extent.</summary>
    private readonly record struct Step(sbyte Low, sbyte High);
}
