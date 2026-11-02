using System.Buffers;
using KernSmith.Rasterizer;
using Stb = StbTrueTypeSharp.StbTrueType;

namespace KernSmith.Rasterizers.StbTrueType;

/// <summary>
/// Dropout control for thresholded (<see cref="AntiAliasMode.None"/>) output: restores ink that
/// a stroke thinner than a pixel, or one straddling two pixels evenly, loses when stb_truetype's
/// coverage is thresholded at 50%.
/// </summary>
/// <remarks>
/// <para>
/// stb_truetype only renders coverage, so this runs on top of its bitmap from the glyph's own
/// vertices; stb itself is not modified. The rule is the Native backend's (TrueType "smart"
/// dropout control including stubs): along each scanline through pixel centres, rows and then
/// columns, the outline's interior is a set of spans; a span that lit none of the pixels it
/// passes through has dropped out, and the pixel holding its midpoint is turned on. Spans that
/// already lit a pixel are left alone, so a glyph without dropouts renders exactly as plain
/// thresholding does. The packages are separate, so the sweep is a copy of Native's
/// <c>DropoutControl</c>, not a reference to it.
/// </para>
/// <para>
/// The outline is flattened as stb_truetype flattens it for <c>stbtt_Rasterize</c> (same
/// subdivision, same 0.35 px flatness, same float arithmetic) and placed in its pixel space
/// (y flipped, column 0 at the bitmap's x offset), so the spans are those of the polygon stb
/// filled. Scratch is pooled; nothing is allocated per glyph.
/// </para>
/// </remarks>
internal static class DropoutControl
{
    /// <summary>The flatness, in pixels, stb_truetype passes to <c>stbtt_Rasterize</c> for glyph bitmaps.</summary>
    internal const float FlatnessInPixels = 0.35f;

    // stbtt_vertex.type values.
    private const byte VMove = 1;
    private const byte VLine = 2;
    private const byte VCurve = 3;
    private const byte VCubic = 4;

    /// <summary>
    /// Turns on the midpoint pixel of every interior span of the outline that lit nothing.
    /// </summary>
    /// <param name="bitmap">The thresholded bitmap (0 or 255), row-major, pitch = width.</param>
    /// <param name="width">Bitmap width.</param>
    /// <param name="height">Bitmap height.</param>
    /// <param name="vertices">The outline stb_truetype rasterized, in font units.</param>
    /// <param name="numVerts">Number of vertices.</param>
    /// <param name="scale">The scale stb_truetype rasterized at (same on both axes, no shift).</param>
    /// <param name="offsetX">The bitmap's x offset: the pixel-space X of column 0.</param>
    /// <param name="offsetY">The bitmap's y offset: the pixel-space Y (down) of row 0.</param>
    public static unsafe void Apply(
        byte[] bitmap, int width, int height, Stb.stbtt_vertex* vertices, int numVerts, float scale, int offsetX, int offsetY)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (width <= 0 || height <= 0 || numVerts <= 0 || !(scale > 0))
            return;

        float flatness = FlatnessInPixels / scale;
        float flatnessSquared = flatness * flatness;
        int count = Flatten(vertices, numVerts, flatnessSquared, null, 0f);
        if (count < 2)
            return;

        Edge[] edges = ArrayPool<Edge>.Shared.Rent(count);
        try
        {
            Flatten(vertices, numVerts, flatnessSquared, edges, scale);

            // Rows first: the column pass must see the pixels the row pass turned on.
            var span = new ReadOnlySpan<Edge>(edges, 0, count);
            Pass(bitmap, span, width, height, offsetX, offsetY, horizontal: true);
            Pass(bitmap, span, width, height, offsetX, offsetY, horizontal: false);
        }
        finally
        {
            ArrayPool<Edge>.Shared.Return(edges);
        }
    }

    // ── Flattening (mirrors stbtt_FlattenCurves and stbtt__rasterize's edge setup) ──

    /// <summary>
    /// Flattens the outline as <c>stbtt_FlattenCurves</c> does and, when <paramref name="edges"/>
    /// is given, writes each closed contour's edges in stb_truetype's pixel space.
    /// </summary>
    /// <returns>The number of flattened points, which is also the number of edges.</returns>
    private static unsafe int Flatten(Stb.stbtt_vertex* vertices, int numVerts, float flatnessSquared, Edge[]? edges, float scale)
    {
        var sink = new PointSink(edges, scale);
        float x = 0, y = 0;
        for (int i = 0; i < numVerts; i++)
        {
            ref readonly var v = ref vertices[i];
            switch (v.type)
            {
                case VMove:
                    sink.Close();
                    x = v.x;
                    y = v.y;
                    sink.Add(x, y);
                    break;
                case VLine:
                    x = v.x;
                    y = v.y;
                    sink.Add(x, y);
                    break;
                case VCurve:
                    TessellateCurve(ref sink, x, y, v.cx, v.cy, v.x, v.y, flatnessSquared, 0);
                    x = v.x;
                    y = v.y;
                    break;
                case VCubic:
                    TessellateCubic(ref sink, x, y, v.cx, v.cy, v.cx1, v.cy1, v.x, v.y, flatnessSquared, 0);
                    x = v.x;
                    y = v.y;
                    break;
            }
        }

        sink.Close();
        return sink.Count;
    }

    /// <summary>Mirrors <c>stbtt__tesselate_curve</c>.</summary>
    private static void TessellateCurve(ref PointSink sink, float x0, float y0, float x1, float y1, float x2, float y2, float flatnessSquared, int n)
    {
        float mx = (x0 + 2 * x1 + x2) / 4;
        float my = (y0 + 2 * y1 + y2) / 4;
        float dx = (x0 + x2) / 2 - mx;
        float dy = (y0 + y2) / 2 - my;
        if (n > 16)
            return;
        if (dx * dx + dy * dy > flatnessSquared)
        {
            TessellateCurve(ref sink, x0, y0, (x0 + x1) / 2.0f, (y0 + y1) / 2.0f, mx, my, flatnessSquared, n + 1);
            TessellateCurve(ref sink, mx, my, (x1 + x2) / 2.0f, (y1 + y2) / 2.0f, x2, y2, flatnessSquared, n + 1);
        }
        else
        {
            sink.Add(x2, y2);
        }
    }

    /// <summary>Mirrors <c>stbtt__tesselate_cubic</c>.</summary>
    private static void TessellateCubic(ref PointSink sink, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float flatnessSquared, int n)
    {
        float dx0 = x1 - x0;
        float dy0 = y1 - y0;
        float dx1 = x2 - x1;
        float dy1 = y2 - y1;
        float dx2 = x3 - x2;
        float dy2 = y3 - y2;
        float dx = x3 - x0;
        float dy = y3 - y0;
        float longlen = (float)(Math.Sqrt(dx0 * dx0 + dy0 * dy0) + Math.Sqrt(dx1 * dx1 + dy1 * dy1) + Math.Sqrt(dx2 * dx2 + dy2 * dy2));
        float shortlen = (float)Math.Sqrt(dx * dx + dy * dy);
        float flatness = longlen * longlen - shortlen * shortlen;
        if (n > 16)
            return;
        if (flatness > flatnessSquared)
        {
            float x01 = (x0 + x1) / 2;
            float y01 = (y0 + y1) / 2;
            float x12 = (x1 + x2) / 2;
            float y12 = (y1 + y2) / 2;
            float x23 = (x2 + x3) / 2;
            float y23 = (y2 + y3) / 2;
            float xa = (x01 + x12) / 2;
            float ya = (y01 + y12) / 2;
            float xb = (x12 + x23) / 2;
            float yb = (y12 + y23) / 2;
            float mx = (xa + xb) / 2;
            float my = (ya + yb) / 2;
            TessellateCubic(ref sink, x0, y0, x01, y01, xa, ya, mx, my, flatnessSquared, n + 1);
            TessellateCubic(ref sink, mx, my, xb, yb, x23, y23, x3, y3, flatnessSquared, n + 1);
        }
        else
        {
            sink.Add(x3, y3);
        }
    }

    /// <summary>
    /// Collects flattened points; on <see cref="Close"/> writes the contour's edges, each from
    /// the previous point to the next and the last back to the first, in pixel space
    /// (x × scale, y × −scale), as <c>stbtt__rasterize</c> builds them.
    /// </summary>
    private struct PointSink
    {
        private readonly Edge[]? _edges;
        private readonly float _scale;
        private int _start;
        private float _firstX, _firstY, _lastX, _lastY;

        public PointSink(Edge[]? edges, float scale)
        {
            _edges = edges;
            _scale = scale;
            Count = 0;
            _start = 0;
            _firstX = _firstY = _lastX = _lastY = 0f;
        }

        public int Count { get; private set; }

        public void Add(float x, float y)
        {
            if (_edges is not null)
            {
                float px = x * _scale, py = y * -_scale;
                if (Count > _start)
                    _edges[Count - 1] = new Edge(_lastX, _lastY, px, py);
                else
                    (_firstX, _firstY) = (px, py);
                (_lastX, _lastY) = (px, py);
            }

            Count++;
        }

        /// <summary>Ends the current contour: its last edge runs back to its first point.</summary>
        public void Close()
        {
            if (_edges is not null && Count > _start)
                _edges[Count - 1] = new Edge(_lastX, _lastY, _firstX, _firstY);
            _start = Count;
        }
    }

    // ── Dropout sweep (mirrors Native's DropoutControl) ──

    /// <summary>
    /// Runs every scanline of one direction: rows (horizontal scanlines, crossed at some x) or
    /// columns (vertical scanlines, crossed at some y).
    /// </summary>
    private static void Pass(byte[] bitmap, ReadOnlySpan<Edge> edges, int width, int height, float originX, float originY, bool horizontal)
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

            // One rental holds both the per-scanline offsets and the buckets.
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
    private static bool Reach(Edge e, bool horizontal, float lineOrigin, int lines, out int first, out int last)
    {
        float a0 = horizontal ? e.Y0 : e.X0, a1 = horizontal ? e.Y1 : e.X1;
        first = last = 0;

        // An edge running along the scanlines crosses none of them. Non-finite edges are dropped.
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
    /// Writes where the bucketed edges cross the scanline at <paramref name="at"/>. Each crossing
    /// carries two winding steps, one with the edge's extent half-open at its low end and one at
    /// its high end; only where both windings are non-zero is interior, so a scanline running
    /// exactly along the outline contributes no zero-thickness span while a vertex on the
    /// scanline is still counted once.
    /// </summary>
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
    /// Sorts a scanline's crossings by position: insertion sort for the usual handful, the
    /// general sort past <see cref="InsertionSortLimit"/>.
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
    /// pixel of every interior span that lit nothing. Crossings at the same position are
    /// applied together, so contours that abut exactly form one span, not two touching ones.
    /// </summary>
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

    /// <summary>A directed outline edge in stb_truetype's pixel space (y down).</summary>
    private readonly record struct Edge(float X0, float Y0, float X1, float Y1);

    /// <summary>
    /// An edge as one pass sees it: <c>A</c> runs across the scanlines, <c>B</c> along them, and
    /// <see cref="First"/>..<see cref="Last"/> are the scanlines <see cref="Reach"/> gave it.
    /// </summary>
    private readonly struct Oriented
    {
        public readonly float A0, Min, Max, B0, DeltaA, DeltaB;
        public readonly int First, Last;
        public readonly sbyte Winding;

        public Oriented(Edge e, bool horizontal, int first, int last)
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
