using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// Dropout control for thresholded (<see cref="AntiAliasMode.None"/>) output: restores ink that
/// a stroke thinner than a pixel, or one straddling two pixels evenly, loses to the 50% coverage
/// threshold.
/// </summary>
/// <remarks>
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

        // Horizontal scanlines through each row's pixel centres.
        for (int row = 0; row < height; row++)
        {
            foreach (var (start, end) in Spans(edges, row + 0.5f + originY, horizontal: true))
            {
                float from = start - originX, to = end - originX;
                int first = Math.Max(0, (int)MathF.Floor(from));
                int last = Math.Min(width - 1, (int)MathF.Ceiling(to) - 1);
                if (first > last || AnyLit(bitmap, (row * width) + first, 1, last - first + 1))
                    continue;

                int column = Math.Clamp((int)MathF.Floor((from + to) / 2f), first, last);
                bitmap[(row * width) + column] = 255;
            }
        }

        // Vertical scanlines through each column's pixel centres.
        for (int column = 0; column < width; column++)
        {
            foreach (var (start, end) in Spans(edges, column + 0.5f + originX, horizontal: false))
            {
                float from = start - originY, to = end - originY;
                int first = Math.Max(0, (int)MathF.Floor(from));
                int last = Math.Min(height - 1, (int)MathF.Ceiling(to) - 1);
                if (first > last || AnyLit(bitmap, (first * width) + column, width, last - first + 1))
                    continue;

                int row = Math.Clamp((int)MathF.Floor((from + to) / 2f), first, last);
                bitmap[(row * width) + column] = 255;
            }
        }
    }

    /// <summary>
    /// The interior spans (non-zero winding) of one scanline, as positions along it.
    /// </summary>
    /// <remarks>
    /// Crossings are taken twice, with the edge's extent half-open at its low end and then at
    /// its high end, and only what both call interior is kept. The two agree everywhere except
    /// where the scanline runs exactly along the outline's boundary, so a scanline lying on an
    /// edge contributes no span of zero thickness, while a vertex sitting on the scanline is
    /// still counted exactly once.
    /// </remarks>
    private static List<(float Start, float End)> Spans(EdgeSegment[] edges, float at, bool horizontal)
    {
        var low = Interior(edges, at, horizontal, lowInclusive: true);
        var high = Interior(edges, at, horizontal, lowInclusive: false);

        var spans = new List<(float, float)>();
        int i = 0, j = 0;
        while (i < low.Count && j < high.Count)
        {
            float start = MathF.Max(low[i].Start, high[j].Start);
            float end = MathF.Min(low[i].End, high[j].End);
            if (end > start)
                spans.Add((start, end));

            if (low[i].End < high[j].End)
                i++;
            else
                j++;
        }

        return spans;
    }

    private static List<(float Start, float End)> Interior(EdgeSegment[] edges, float at, bool horizontal, bool lowInclusive)
    {
        var crossings = new List<(float Position, int Winding)>();
        foreach (var e in edges)
        {
            // For a horizontal scanline the edge is parameterized by y and crossed at some x;
            // for a vertical one the roles swap.
            float a0 = horizontal ? e.Y0 : e.X0, a1 = horizontal ? e.Y1 : e.X1;
            float b0 = horizontal ? e.X0 : e.Y0, b1 = horizontal ? e.X1 : e.Y1;
            if (a0.Equals(a1))
                continue;

            float min = MathF.Min(a0, a1), max = MathF.Max(a0, a1);
            bool inside = lowInclusive ? at >= min && at < max : at > min && at <= max;
            if (!inside)
                continue;

            crossings.Add((b0 + ((at - a0) * (b1 - b0) / (a1 - a0)), a1 > a0 ? 1 : -1));
        }

        var spans = new List<(float, float)>();
        if (crossings.Count < 2)
            return spans;

        crossings.Sort((x, y) => x.Position.CompareTo(y.Position));
        int winding = 0;
        float start = 0f;
        foreach (var (position, w) in crossings)
        {
            int before = winding;
            winding += w;
            if (before == 0 && winding != 0)
                start = position;
            else if (before != 0 && winding == 0 && position > start)
                spans.Add((start, position));
        }

        return spans;
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
}
