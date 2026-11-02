using System.Buffers;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// Bilevel rasterization for text sizes, written the way a pixel-font artist draws: a stroke
/// thinner than <see cref="ThinSpan"/> is one pixel wide wherever it runs, and a counter the
/// outline encloses stays open.
/// </summary>
/// <remarks>
/// <para>
/// Along every scanline through pixel centres (one per row and one per column) the outline's
/// interior is a set of spans. A span narrower than <see cref="ThinSpan"/> whose two bounding
/// edges are near-parallel and cross the scanline within 45° of perpendicular (a cross-section
/// of a thin stroke — not a scanline grazing a bowl's top, not the stroke's end cap, and not
/// the axis a Bresenham line would not step along) is a <em>thin cross-section</em>: it lights
/// exactly the pixel holding its midpoint. Every other span is centre-sampled, so a stem the
/// fitter placed on whole pixels comes out exactly as fitted and solid shapes are plain centre
/// sampling.
/// </para>
/// <para>
/// The two passes are combined per pixel. A pixel whose centre is inside the outline is lit
/// unless a thin cross-section runs through its centre and it is not that span's midpoint —
/// so a thin stroke is never two pixels wide in either axis, and a dot under 1.5 px is one
/// pixel. The one exception is a join: where a stem meets a thin arch (the top of an 'n'),
/// the row span through the junction is the stem plus a sliver of the arch, and its midpoint
/// is the stem's pixel; the junction pixel is kept when it is itself the arch's column
/// midpoint and the stem's pixel is no column midpoint (the stem is tall, the arch is thin),
/// which a dot, whose midpoints agree in both axes, never satisfies. A pixel whose centre is
/// outside is lit when it is some thin cross-section's midpoint, which is how a hairline
/// survives. A thin diagonal therefore steps like a Bresenham line: one pixel per row where it
/// is steep, one per column where it is shallow, and at 45° the row and column midpoints
/// sample the same centre line and agree.
/// </para>
/// <para>
/// The slope tests read the flattened edges, so the caller flattens finely
/// (<see cref="Flatness"/>): at the renderer's usual 0.35 px a bowl's shoulder is one chord
/// whose slope is nothing like the curve's, and the span it bounds is misjudged.
/// </para>
/// <para>
/// Topology: the outline is centre-sampled again at <see cref="Oversample"/>× to find the
/// counters it encloses; a counter that the bilevel bitmap closed (no enclosed background pixel
/// overlaps it) is reopened at the pixel it covers most, provided that pixel's four neighbours
/// are lit, so an 'e', 'a', '8' or 'B' keeps its eye at sizes where the counter is under a
/// pixel. The bitmap's own extra holes are left alone.
/// </para>
/// <para>
/// All scratch is pooled; the only allocation is the caller's bitmap. Cost is linear in edges ×
/// scanlines, which at text sizes is a few thousand crossing tests per glyph.
/// </para>
/// </remarks>
internal static class MonoStrokeRasterizer
{
    /// <summary>A span narrower than this, in pixels, is a thin stroke: one pixel, at its midpoint.</summary>
    public const float ThinSpan = 1.5f;

    /// <summary>The flattening tolerance, in pixels, the edges should be built with.</summary>
    public const float Flatness = 0.05f;

    /// <summary>
    /// The largest |d(along)/d(across)| of a bounding edge for a span to count as a stroke's
    /// cross-section: 1 is an edge within 45° of perpendicular to the scanline, so a stroke is
    /// drawn along the axis it is longer in — one pixel per row when steep, per column when
    /// shallow — and at exactly 45° both passes draw it and agree.
    /// </summary>
    private const float MaxCrossSlope = 1f;

    /// <summary>
    /// The largest difference between the two bounding edges' signed slopes for them to count
    /// as the two sides of one stroke. A side and the stroke's end cap at 45° differ by 2; a
    /// stem and the underside of the arch leaving it, or a terminal's cut and the curve
    /// rising from it (the 'r' arm, the tail of a '9'), by 0.6-1.5 — spans that are a stroke
    /// plus a sliver of another, whose midpoint is not where the other stroke needs a pixel.
    /// The outer and inner sides of a one-pixel arc differ by under 0.5 wherever both are
    /// within <see cref="MaxCrossSlope"/>, on any bowl at least 2.5 px across.
    /// </summary>
    private const float ParallelTolerance = 0.5f;

    /// <summary>
    /// A narrow span both of whose bounding edges are shallower than this is a scanline
    /// grazing the top of a bowl, not a stroke, and the dropout pass leaves it alone: on a
    /// bowl 3 px across a chord under <see cref="ThinSpan"/> wide sits where the tangents are
    /// at least 1.7. A diagonal just past 45° (the 'z' at 8 ppem, 1.07) is a stroke.
    /// </summary>
    private const float GrazeSlope = 1.5f;

    /// <summary>Sub-pixels per pixel for the counter check.</summary>
    private const int Oversample = 4;

    /// <summary>
    /// The counter check runs only for bitmaps up to this many pixels tall: a counter or
    /// aperture closes when it is under a pixel, which happens below about 16 ppem (an 'e' eye
    /// is a tenth of the em), and the check is the costliest stage — sampling at
    /// <see cref="Oversample"/>× would quadruple the time of a 24 px glyph for nothing.
    /// </summary>
    private const int MaxGuardHeight = 20;

    /// <summary>A counter smaller than this many sub-pixels is noise, not a counter.</summary>
    private const int MinCounterSubpixels = 2;

    /// <summary>
    /// An enclosed background region of the bitmap larger than this many pixels is not
    /// treated as a closed aperture: the apertures that close at text sizes (a 'c', 's', 'e'
    /// or 'a') enclose a pixel or two, while the large interior of an '@' or ampersand has several
    /// low-ink rim pixels and opening the wrong one cuts the glyph.
    /// </summary>
    private const int MaxAperturePixels = 4;

    /// <summary>
    /// A span thinner than this, in pixels, is a hairline: the first pass does not draw it,
    /// the dropout pass does where it is needed — a hairline stroke standing alone, a crossbar
    /// running between stems — and not where it only thickens what is already drawn, which is
    /// what a hairline serif slab along a stem's end would do.
    /// </summary>
    private const float HairlineSpan = 0.35f;

    /// <summary>
    /// A span narrower than this, in pixels, is no span: the two sides of a zero-area contour
    /// (a collinear or out-and-back path) cross a scanline a rounding error apart, and the
    /// dropout pass must not restore that sliver as a hairline.
    /// </summary>
    private const float MinSpan = 1f / 32f;

    private const byte Inside = 1;
    private const byte ThinRow = 2;
    private const byte RowMid = 4;
    private const byte ThinColumn = 8;
    private const byte ColumnMid = 16;
    private const byte NarrowRowMid = 32;
    private const byte NarrowColumnMid = 64;

    /// <summary>Rasterizes flattened edges into a fresh 0/255 bitmap.</summary>
    /// <param name="edges">Directed edges in pixel space.</param>
    /// <param name="width">Bitmap width.</param>
    /// <param name="height">Bitmap height.</param>
    /// <param name="originX">Pixel-space X of bitmap column 0.</param>
    /// <param name="originY">Pixel-space Y of bitmap row 0.</param>
    public static byte[] Rasterize(ReadOnlySpan<EdgeSegment> edges, int width, int height, float originX, float originY)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        var bitmap = new byte[width * height];
        Rasterize(edges, bitmap, width, height, originX, originY);
        return bitmap;
    }

    /// <summary>Rasterizes into <paramref name="bitmap"/>, which must be zeroed and hold <c>width × height</c> bytes.</summary>
    public static void Rasterize(ReadOnlySpan<EdgeSegment> edges, Span<byte> bitmap, int width, int height, float originX, float originY)
    {
        int size = width * height;
        if (size == 0 || edges.Length < 2)
            return;

        byte[] flags = ArrayPool<byte>.Shared.Rent(size);
        byte[] snapshot = ArrayPool<byte>.Shared.Rent(size);
        int[] midpoints = ArrayPool<int>.Shared.Rent(2 * size);
        try
        {
            var f = flags.AsSpan(0, size);
            f.Clear();

            // For a pixel in a thin span, the index of that span's midpoint pixel (-1 when it
            // is off the bitmap); written whenever the Thin flag is, so never read stale.
            var rowMidOf = midpoints.AsSpan(0, size);
            var columnMidOf = midpoints.AsSpan(size, size);
            Pass(edges, f, rowMidOf, bitmap, bitmap, width, height, originX, originY, horizontal: true, restore: false);
            Pass(edges, f, columnMidOf, bitmap, bitmap, width, height, originX, originY, horizontal: false, restore: false);
            for (int i = 0; i < size; i++)
            {
                byte v = f[i];
                bool lit;
                if ((v & Inside) == 0)
                {
                    lit = (v & (RowMid | ColumnMid)) != 0;
                }
                else
                {
                    bool suppressedByRow = (v & ThinRow) != 0 && (v & RowMid) == 0
                        && !((v & ColumnMid) != 0 && !Has(f, rowMidOf[i], ColumnMid));
                    bool suppressedByColumn = (v & ThinColumn) != 0 && (v & ColumnMid) == 0
                        && !((v & RowMid) != 0 && !Has(f, columnMidOf[i], RowMid));
                    lit = !suppressedByRow && !suppressedByColumn;
                }

                bitmap[i] = lit ? (byte)255 : (byte)0;
            }

            // Dropout: a thin span that lit nothing — a hairline, or a tight bend whose two
            // sides are not parallel in either pass and whose pixel centre fell outside — gets
            // its midpoint where that keeps a stroke whole (see RestoreSpan), judged against
            // what the first pass drew. Rows first; the column pass sees what rows restored.
            var drawn = snapshot.AsSpan(0, size);
            bitmap.CopyTo(drawn);
            Pass(edges, f, rowMidOf, bitmap, drawn, width, height, originX, originY, horizontal: true, restore: true);
            Pass(edges, f, columnMidOf, bitmap, drawn, width, height, originX, originY, horizontal: false, restore: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(flags);
            ArrayPool<byte>.Shared.Return(snapshot);
            ArrayPool<int>.Shared.Return(midpoints);
        }

        if (height <= MaxGuardHeight)
            GuardCounters(edges, bitmap, width, height, originX, originY);
    }

    private static bool Has(ReadOnlySpan<byte> flags, int index, byte flag) => index >= 0 && (flags[index] & flag) != 0;

    /// <summary>
    /// One direction of scanlines: rows (crossed at some x) or columns (crossed at some y).
    /// Marking (<paramref name="restore"/> false) fills the flags; restoring lights the
    /// midpoint of a thin span that is not a graze and has no lit pixel, when
    /// <see cref="RestoreSpan"/> finds a stroke needs it. <paramref name="drawn"/> is the
    /// first pass's result (the bitmap itself while marking).
    /// </summary>
    private static void Pass(ReadOnlySpan<EdgeSegment> edges, Span<byte> flags, Span<int> midOf, Span<byte> bitmap, ReadOnlySpan<byte> drawn, int width, int height, float originX, float originY, bool horizontal, bool restore)
    {
        int lines = horizontal ? height : width;
        int along = horizontal ? width : height;
        float lineOrigin = horizontal ? originY : originX;
        float alongOrigin = horizontal ? originX : originY;

        var crossings = new CrossingBuffer(edges.Length);
        try
        {
            for (int line = 0; line < lines; line++)
            {
                float at = line + 0.5f + lineOrigin;
                int count = crossings.Collect(edges, at, horizontal);
                if (count < 2)
                    continue;

                crossings.Sort(count);
                int winding = 0;
                float start = 0f, startSlope = 0f;
                for (int i = 0; i < count; i++)
                {
                    bool wasInside = winding != 0;
                    winding += crossings.Winding[i];
                    bool inside = winding != 0;
                    if (!wasInside && inside)
                    {
                        start = crossings.Position[i];
                        startSlope = crossings.Slope[i];
                    }
                    else if (wasInside && !inside)
                    {
                        float end = crossings.Position[i], endSlope = crossings.Slope[i];
                        if (end - start < MinSpan)
                            continue;

                        float span = end - start;
                        bool narrow = span < ThinSpan;
                        if (restore)
                        {
                            bool graze = MathF.Abs(startSlope) > GrazeSlope && MathF.Abs(endSlope) > GrazeSlope;
                            if (narrow && !graze)
                                RestoreSpan(bitmap, drawn, flags, width, height, line, horizontal, along, start - alongOrigin, end - alongOrigin);
                            continue;
                        }

                        bool thin = narrow && span >= HairlineSpan
                            && MathF.Abs(startSlope) <= MaxCrossSlope && MathF.Abs(endSlope) <= MaxCrossSlope
                            && MathF.Abs(startSlope - endSlope) <= ParallelTolerance;
                        ApplySpan(flags, midOf, width, line, horizontal, along, start - alongOrigin, end - alongOrigin, narrow, thin);
                    }
                }
            }
        }
        finally
        {
            crossings.Dispose();
        }
    }

    /// <summary>Marks one span's pixels: every centre it holds as inside; a thin cross-section
    /// also marks those centres as lying in one and its midpoint pixel as the midpoint.</summary>
    private static void ApplySpan(Span<byte> flags, Span<int> midOf, int width, int line, bool horizontal, int along, float from, float to, bool narrow, bool thin)
    {
        byte mark = Inside;
        int midIndex = -1;
        int mid = (int)MathF.Floor((from + to) / 2f);
        bool midInside = mid >= 0 && mid < along;
        if (narrow && midInside)
            flags[Index(width, line, mid, horizontal)] |= horizontal ? NarrowRowMid : NarrowColumnMid;
        if (thin)
        {
            mark |= horizontal ? ThinRow : ThinColumn;
            if (midInside)
            {
                midIndex = Index(width, line, mid, horizontal);
                flags[midIndex] |= horizontal ? RowMid : ColumnMid;
            }
        }

        // Pixel centres k + 0.5 in [from, to).
        int first = Math.Max(0, (int)MathF.Ceiling(from - 0.5f));
        int last = Math.Min(along - 1, (int)MathF.Ceiling(to - 0.5f) - 1);
        for (int k = first; k <= last; k++)
        {
            int index = Index(width, line, k, horizontal);
            flags[index] |= mark;
            if (thin)
                midOf[index] = midIndex;
        }
    }

    /// <summary>
    /// Lights a thin span's midpoint pixel when neither it nor any pixel whose centre the span
    /// holds is lit, and a stroke needs it: the pixel has no lit neighbour at all (a hairline
    /// standing alone), or it joins two lit groups that do not touch (the step of a diagonal
    /// between two bars), or the hairline carries on into the next scanline (a crossbar
    /// leaving a stem). A pixel with one lit group beside it and nothing beyond is a stroke's
    /// end cap or a dot's second row — ink already drawn — and stays off. Neighbours are read
    /// from the first pass's result, so a hairline restored row by row does not block itself.
    /// </summary>
    private static void RestoreSpan(Span<byte> bitmap, ReadOnlySpan<byte> drawn, ReadOnlySpan<byte> flags, int width, int height, int line, bool horizontal, int along, float from, float to)
    {
        int mid = (int)MathF.Floor((from + to) / 2f);
        if (mid < 0 || mid >= along)
            return;

        int first = Math.Max(0, (int)MathF.Ceiling(from - 0.5f));
        int last = Math.Min(along - 1, (int)MathF.Ceiling(to - 0.5f) - 1);
        int m = Index(width, line, mid, horizontal);
        if (bitmap[m] != 0)
            return;
        for (int k = first; k <= last; k++)
        {
            if (bitmap[Index(width, line, k, horizontal)] != 0)
                return;
        }

        int x = m % width, y = m / width;
        int groups = LitGroupsAround(drawn, width, height, x, y);
        bool needed = groups != 1;
        if (!needed)
        {
            // Continuation: an unlit narrow span's midpoint in an adjacent scanline, within a
            // step of this one.
            byte continues = horizontal ? NarrowRowMid : NarrowColumnMid;
            for (int side = -1; side <= 1 && !needed; side += 2)
            {
                for (int step = -1; step <= 1 && !needed; step++)
                {
                    int nx = horizontal ? x + step : x + side;
                    int ny = horizontal ? y + side : y + step;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    int n = (ny * width) + nx;
                    needed = (flags[n] & continues) != 0 && drawn[n] == 0;
                }
            }
        }

        if (needed)
            bitmap[m] = 255;
    }

    /// <summary>The number of 8-connected groups among the lit 8-neighbours of a pixel (the crossing number of its ring).</summary>
    private static int LitGroupsAround(ReadOnlySpan<byte> drawn, int width, int height, int x, int y)
    {
        ReadOnlySpan<(int Dx, int Dy)> ring = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];
        int groups = 0;
        bool previous = LitAt(drawn, width, height, x + ring[7].Dx, y + ring[7].Dy);
        foreach (var (dx, dy) in ring)
        {
            bool current = LitAt(drawn, width, height, x + dx, y + dy);
            if (current && !previous)
                groups++;
            previous = current;
        }

        return groups;
    }

    private static bool LitAt(ReadOnlySpan<byte> drawn, int width, int height, int x, int y) =>
        x >= 0 && y >= 0 && x < width && y < height && drawn[(y * width) + x] != 0;

    private static int Index(int width, int line, int k, bool horizontal) =>
        horizontal ? (line * width) + k : (k * width) + line;

    // ---- counters -----------------------------------------------------------------------------

    /// <summary>Reopens counters the outline encloses that the bitmap closed.</summary>
    private static void GuardCounters(ReadOnlySpan<EdgeSegment> edges, Span<byte> bitmap, int width, int height, float originX, float originY)
    {
        int subWidth = width * Oversample, subHeight = height * Oversample;
        int subSize = subWidth * subHeight;
        int size = width * height;

        // One rental: the sub-pixel ink/visited map, then the pixel-level scratch.
        byte[] sub = ArrayPool<byte>.Shared.Rent(subSize);
        int[] queue = ArrayPool<int>.Shared.Rent(Math.Max(subSize, size));
        int[] overlap = ArrayPool<int>.Shared.Rent(size);
        byte[] pixelMark = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var ink = sub.AsSpan(0, subSize);
            ink.Clear();
            SampleSubpixels(edges, ink, subWidth, subHeight, originX, originY);

            // 1 = ink, 2 = outside background. What is left at 0 is a counter.
            FloodFromBorder(ink, subWidth, subHeight, queue, mark: 2);

            // The bitmap's enclosed background pixels, so a counter they overlap counts as open.
            var enclosed = pixelMark.AsSpan(0, size);
            for (int i = 0; i < size; i++)
                enclosed[i] = bitmap[i] != 0 ? (byte)1 : (byte)0;
            FloodFromBorder(enclosed, width, height, queue, mark: 2);

            var counts = overlap.AsSpan(0, size);
            for (int start = 0; start < subSize; start++)
            {
                if (ink[start] != 0)
                    continue;

                // Flood this counter (marking it 3), counting its sub-pixels per bitmap pixel.
                counts.Clear();
                int area = 0;
                bool open = false;
                int head = 0, tail = 0;
                queue[tail++] = start;
                ink[start] = 3;
                while (head < tail)
                {
                    int s = queue[head++];
                    int sx = s % subWidth, sy = s / subWidth;
                    int pixel = ((sy / Oversample) * width) + (sx / Oversample);
                    counts[pixel]++;
                    area++;
                    if (enclosed[pixel] == 0)
                        open = true;

                    if (sx > 0 && ink[s - 1] == 0) { ink[s - 1] = 3; queue[tail++] = s - 1; }
                    if (sx + 1 < subWidth && ink[s + 1] == 0) { ink[s + 1] = 3; queue[tail++] = s + 1; }
                    if (sy > 0 && ink[s - subWidth] == 0) { ink[s - subWidth] = 3; queue[tail++] = s - subWidth; }
                    if (sy + 1 < subHeight && ink[s + subWidth] == 0) { ink[s + subWidth] = 3; queue[tail++] = s + subWidth; }
                }

                if (open || area < MinCounterSubpixels)
                    continue;

                Reopen(bitmap, counts, width, height, enclosed);
            }

            // The other way round: an enclosed background region of the bitmap that overlaps
            // no counter of the outline is an aperture the bitmap closed — the opening of a
            // 'c', 'e' or 's', the top of an 'a' — and the lit pixel on its rim holding the
            // least ink is the bridge, so it is cleared. Enclosed cells are still 0 here.
            for (int start = 0; start < size; start++)
            {
                if (enclosed[start] != 0)
                    continue;

                int head = 0, tail = 0;
                bool real = false;
                queue[tail++] = start;
                enclosed[start] = 3;
                while (head < tail)
                {
                    int p = queue[head++];
                    int px = p % width, py = p / width;
                    real |= OverlapsCounter(ink, subWidth, px, py);
                    if (px > 0 && enclosed[p - 1] == 0) { enclosed[p - 1] = 3; queue[tail++] = p - 1; }
                    if (px + 1 < width && enclosed[p + 1] == 0) { enclosed[p + 1] = 3; queue[tail++] = p + 1; }
                    if (py > 0 && enclosed[p - width] == 0) { enclosed[p - width] = 3; queue[tail++] = p - width; }
                    if (py + 1 < height && enclosed[p + width] == 0) { enclosed[p + width] = 3; queue[tail++] = p + width; }
                }

                if (!real && tail <= MaxAperturePixels)
                    OpenAperture(bitmap, ink, subWidth, width, height, queue.AsSpan(0, tail));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sub);
            ArrayPool<int>.Shared.Return(queue);
            ArrayPool<int>.Shared.Return(overlap);
            ArrayPool<byte>.Shared.Return(pixelMark);
        }
    }

    /// <summary>True when any sub-pixel of the pixel at (<paramref name="px"/>, <paramref name="py"/>) lies in a counter of the outline (marked 3).</summary>
    private static bool OverlapsCounter(ReadOnlySpan<byte> ink, int subWidth, int px, int py)
    {
        for (int sy = py * Oversample; sy < (py + 1) * Oversample; sy++)
        {
            for (int sx = px * Oversample; sx < (px + 1) * Oversample; sx++)
            {
                if (ink[(sy * subWidth) + sx] == 3)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Clears, on the rim of a spurious hole (its pixels listed in <paramref name="hole"/>), the lit pixel holding the least ink.</summary>
    private static void OpenAperture(Span<byte> bitmap, ReadOnlySpan<byte> ink, int subWidth, int width, int height, ReadOnlySpan<int> hole)
    {
        int best = -1, bestInk = int.MaxValue;
        ReadOnlySpan<(int Dx, int Dy)> neighbours = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        foreach (int p in hole)
        {
            int px = p % width, py = p / width;
            foreach (var (dx, dy) in neighbours)
            {
                int x = px + dx, y = py + dy;
                if (x < 0 || y < 0 || x >= width || y >= height)
                    continue;
                int index = (y * width) + x;
                if (bitmap[index] == 0 || Through(ink, subWidth, x, y))
                    continue;

                int count = InkIn(ink, subWidth, x, y);
                if (count < bestInk)
                {
                    bestInk = count;
                    best = index;
                }
            }
        }

        if (best >= 0)
            bitmap[best] = 0;
    }

    /// <summary>
    /// True when the outline's ink runs through the pixel — reaches both its left and right
    /// edges, or both its top and bottom — so it is a stroke passing by (the spine of an 's'
    /// crossing a corner of the pixel), not a terminal ending in it. Clearing it would cut the
    /// stroke, which is worse than a closed aperture.
    /// </summary>
    private static bool Through(ReadOnlySpan<byte> ink, int subWidth, int x, int y)
    {
        bool left = false, right = false, top = false, bottom = false;
        int x0 = x * Oversample, y0 = y * Oversample, last = Oversample - 1;
        for (int k = 0; k < Oversample; k++)
        {
            left |= ink[((y0 + k) * subWidth) + x0] == 1;
            right |= ink[((y0 + k) * subWidth) + x0 + last] == 1;
            top |= ink[(y0 * subWidth) + x0 + k] == 1;
            bottom |= ink[((y0 + last) * subWidth) + x0 + k] == 1;
        }

        return (left && right) || (top && bottom);
    }

    /// <summary>How many of a pixel's sub-pixels are ink.</summary>
    private static int InkIn(ReadOnlySpan<byte> ink, int subWidth, int x, int y)
    {
        int count = 0;
        for (int sy = y * Oversample; sy < (y + 1) * Oversample; sy++)
        {
            for (int sx = x * Oversample; sx < (x + 1) * Oversample; sx++)
                count += ink[(sy * subWidth) + sx] == 1 ? 1 : 0;
        }

        return count;
    }

    /// <summary>Clears the lit pixel the counter covers most whose four neighbours are all lit, if there is one.</summary>
    private static void Reopen(Span<byte> bitmap, Span<int> counts, int width, int height, Span<byte> enclosed)
    {
        // Candidates in descending overlap; a rejected one is zeroed so the next scan skips it.
        while (true)
        {
            int best = -1, bestCount = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > bestCount && bitmap[i] != 0)
                {
                    bestCount = counts[i];
                    best = i;
                }
            }

            if (best < 0)
                return;

            int x = best % width, y = best / width;
            bool surrounded = x > 0 && x + 1 < width && y > 0 && y + 1 < height
                && bitmap[best - 1] != 0 && bitmap[best + 1] != 0 && bitmap[best - width] != 0 && bitmap[best + width] != 0;
            if (surrounded)
            {
                bitmap[best] = 0;
                enclosed[best] = 0;
                return;
            }

            counts[best] = 0;
        }
    }

    /// <summary>Centre-samples the outline on the sub-pixel grid: 1 where a sub-pixel centre is inside.</summary>
    private static void SampleSubpixels(ReadOnlySpan<EdgeSegment> edges, Span<byte> ink, int subWidth, int subHeight, float originX, float originY)
    {
        var crossings = new CrossingBuffer(edges.Length);
        try
        {
            for (int line = 0; line < subHeight; line++)
            {
                float at = ((line + 0.5f) / Oversample) + originY;
                int count = crossings.Collect(edges, at, horizontal: true);
                if (count < 2)
                    continue;

                crossings.Sort(count);
                int winding = 0;
                float start = 0f;
                for (int i = 0; i < count; i++)
                {
                    bool wasInside = winding != 0;
                    winding += crossings.Winding[i];
                    bool inside = winding != 0;
                    if (!wasInside && inside)
                    {
                        start = crossings.Position[i];
                    }
                    else if (wasInside && !inside)
                    {
                        float from = (start - originX) * Oversample, to = (crossings.Position[i] - originX) * Oversample;
                        int first = Math.Max(0, (int)MathF.Ceiling(from - 0.5f));
                        int last = Math.Min(subWidth - 1, (int)MathF.Ceiling(to - 0.5f) - 1);
                        for (int k = first; k <= last; k++)
                            ink[(line * subWidth) + k] = 1;
                    }
                }
            }
        }
        finally
        {
            crossings.Dispose();
        }
    }

    /// <summary>Marks every 0 cell reachable (4-connected) from a 0 cell on the border with <paramref name="mark"/>.</summary>
    private static void FloodFromBorder(Span<byte> cells, int width, int height, int[] queue, byte mark)
    {
        int head = 0, tail = 0;
        for (int x = 0; x < width; x++)
        {
            Seed(cells, queue, ref tail, x, mark);
            Seed(cells, queue, ref tail, ((height - 1) * width) + x, mark);
        }

        for (int y = 1; y + 1 < height; y++)
        {
            Seed(cells, queue, ref tail, y * width, mark);
            Seed(cells, queue, ref tail, (y * width) + width - 1, mark);
        }

        while (head < tail)
        {
            int s = queue[head++];
            int sx = s % width, sy = s / width;
            if (sx > 0) Seed(cells, queue, ref tail, s - 1, mark);
            if (sx + 1 < width) Seed(cells, queue, ref tail, s + 1, mark);
            if (sy > 0) Seed(cells, queue, ref tail, s - width, mark);
            if (sy + 1 < height) Seed(cells, queue, ref tail, s + width, mark);
        }
    }

    private static void Seed(Span<byte> cells, int[] queue, ref int tail, int i, byte mark)
    {
        if (cells[i] == 0)
        {
            cells[i] = mark;
            queue[tail++] = i;
        }
    }

    // ---- crossings ----------------------------------------------------------------------------

    /// <summary>Pooled storage for one scanline's crossings: position along the line, winding
    /// step, and the edge's signed slope d(along)/d(across) — geometric, the same whichever way
    /// the edge runs.</summary>
    private struct CrossingBuffer(int capacity) : IDisposable
    {
        public float[] Position = ArrayPool<float>.Shared.Rent(capacity);
        public float[] Slope = ArrayPool<float>.Shared.Rent(capacity);
        public sbyte[] Winding = ArrayPool<sbyte>.Shared.Rent(capacity);

        /// <summary>Where the edges cross the scanline at <paramref name="at"/>, half-open at each edge's high end.</summary>
        public readonly int Collect(ReadOnlySpan<EdgeSegment> edges, float at, bool horizontal)
        {
            int count = 0;
            foreach (var e in edges)
            {
                float a0 = horizontal ? e.Y0 : e.X0, a1 = horizontal ? e.Y1 : e.X1;
                float b0 = horizontal ? e.X0 : e.Y0, b1 = horizontal ? e.X1 : e.Y1;
                if (!float.IsFinite(a0) || !float.IsFinite(a1) || !float.IsFinite(b0) || !float.IsFinite(b1) || a0.Equals(a1))
                    continue;

                float min = MathF.Min(a0, a1), max = MathF.Max(a0, a1);
                if (at < min || at >= max)
                    continue;

                float deltaA = a1 - a0, deltaB = b1 - b0;
                Position[count] = b0 + ((at - a0) * deltaB / deltaA);
                Slope[count] = deltaB / deltaA;
                Winding[count] = a1 > a0 ? (sbyte)1 : (sbyte)-1;
                count++;
            }

            return count;
        }

        /// <summary>Insertion sort by position; a scanline holds a handful of crossings.</summary>
        public readonly void Sort(int count)
        {
            for (int i = 1; i < count; i++)
            {
                float p = Position[i], s = Slope[i];
                sbyte w = Winding[i];
                int j = i - 1;
                while (j >= 0 && Position[j] > p)
                {
                    Position[j + 1] = Position[j];
                    Slope[j + 1] = Slope[j];
                    Winding[j + 1] = Winding[j];
                    j--;
                }

                Position[j + 1] = p;
                Slope[j + 1] = s;
                Winding[j + 1] = w;
            }
        }

        public readonly void Dispose()
        {
            ArrayPool<float>.Shared.Return(Position);
            ArrayPool<float>.Shared.Return(Slope);
            ArrayPool<sbyte>.Shared.Return(Winding);
        }
    }
}
