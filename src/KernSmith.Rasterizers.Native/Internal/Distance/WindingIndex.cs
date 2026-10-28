namespace KernSmith.Rasterizers.Native.Internal.Distance;

/// <summary>
/// Non-zero winding queries over a closed polyline outline, bucketed into one-pixel horizontal
/// bands so a query only visits the segments that span its row.
/// </summary>
/// <remarks>
/// A point's winding number counts the signed crossings of a ray cast from it towards +X.
/// Crossings use the half-open rule (a segment spans <c>[min(y0, y1), max(y0, y1))</c>), so a
/// ray through a shared vertex counts it exactly once and horizontal segments never count.
/// </remarks>
internal sealed class WindingIndex
{
    private readonly float[] _segments; // x0, y0, x1, y1 per non-horizontal segment
    private readonly int _bandOrigin;
    private readonly int[][] _bands;

    private WindingIndex(float[] segments, int bandOrigin, int[][] bands)
    {
        _segments = segments;
        _bandOrigin = bandOrigin;
        _bands = bands;
    }

    /// <summary>Indexes every segment of every edge's polyline.</summary>
    public static WindingIndex Create(IReadOnlyList<DistanceEdge> edges)
    {
        var segments = new List<float>();
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var edge in edges)
        {
            var p = edge.Points;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                // Exactly horizontal segments cross no ray; Equals keeps the test exact.
                if (p[i + 1].Equals(p[i + 3]))
                    continue;

                segments.Add(p[i]);
                segments.Add(p[i + 1]);
                segments.Add(p[i + 2]);
                segments.Add(p[i + 3]);
                minY = Math.Min(minY, Math.Min(p[i + 1], p[i + 3]));
                maxY = Math.Max(maxY, Math.Max(p[i + 1], p[i + 3]));
            }
        }

        if (segments.Count == 0)
            return new WindingIndex([], 0, []);

        int origin = (int)Math.Floor(minY);
        int count = (int)Math.Floor(maxY) - origin + 1;
        var lists = new List<int>[count];
        for (int s = 0; s < segments.Count; s += 4)
        {
            int first = (int)Math.Floor(Math.Min(segments[s + 1], segments[s + 3])) - origin;
            int last = (int)Math.Floor(Math.Max(segments[s + 1], segments[s + 3])) - origin;
            for (int b = first; b <= last; b++)
                (lists[b] ??= []).Add(s);
        }

        var bands = new int[count][];
        for (int b = 0; b < count; b++)
            bands[b] = lists[b] is { } list ? [.. list] : [];

        return new WindingIndex([.. segments], origin, bands);
    }

    /// <summary>True when the point is inside the outline under the non-zero rule.</summary>
    public bool IsInside(double x, double y)
    {
        int band = (int)Math.Floor(y) - _bandOrigin;
        if (band < 0 || band >= _bands.Length)
            return false;

        int winding = 0;
        foreach (int s in _bands[band])
        {
            double x0 = _segments[s], y0 = _segments[s + 1], x1 = _segments[s + 2], y1 = _segments[s + 3];
            bool down = y1 > y0;
            if (down ? (y < y0 || y >= y1) : (y < y1 || y >= y0))
                continue;

            double crossX = x0 + ((y - y0) * (x1 - x0) / (y1 - y0));
            if (crossX > x)
                winding += down ? 1 : -1;
        }

        return winding != 0;
    }

    /// <summary>
    /// Fills <paramref name="inside"/> with the non-zero inside test at every texel centre of
    /// one row of a field (centres at x = i + 0.5, y = <paramref name="row"/> + 0.5).
    /// </summary>
    public void ScanRow(int row, Span<bool> inside, List<(double X, int Direction)> scratch)
    {
        inside.Clear();
        double y = row + 0.5;
        int band = row - _bandOrigin;
        if (band < 0 || band >= _bands.Length)
            return;

        scratch.Clear();
        foreach (int s in _bands[band])
        {
            double x0 = _segments[s], y0 = _segments[s + 1], x1 = _segments[s + 2], y1 = _segments[s + 3];
            bool down = y1 > y0;
            if (down ? (y < y0 || y >= y1) : (y < y1 || y >= y0))
                continue;

            scratch.Add((x0 + ((y - y0) * (x1 - x0) / (y1 - y0)), down ? 1 : -1));
        }

        if (scratch.Count == 0)
            return;

        scratch.Sort((a, b) => a.X.CompareTo(b.X));

        // Sweep right to left: a texel's winding is the sum of the crossings to its right.
        int winding = 0;
        int k = scratch.Count - 1;
        for (int i = inside.Length - 1; i >= 0; i--)
        {
            double centre = i + 0.5;
            while (k >= 0 && scratch[k].X > centre)
                winding += scratch[k--].Direction;
            inside[i] = winding != 0;
        }
    }
}
