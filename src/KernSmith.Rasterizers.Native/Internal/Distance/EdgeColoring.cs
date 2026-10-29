namespace KernSmith.Rasterizers.Native.Internal.Distance;

/// <summary>
/// Assigns MSDF channels to outline edges so that the two edges meeting at every corner feed
/// different channel pairs — the property that lets <c>median(R, G, B)</c> reconstruct a sharp
/// corner (Chlumský, "Shape Decomposition for Multi-channel Distance Fields", 2015).
/// </summary>
/// <remarks>
/// Smooth contours stay <see cref="EdgeColor.White"/>. Otherwise each run of edges between two
/// corners (a spline) gets one of the two-channel colors cyan, magenta and yellow in rotation,
/// any two of which share exactly one channel; the last spline takes the third color when
/// rotation would repeat the first. A contour with a single corner (a teardrop) is colored in
/// thirds — one color, white, another color — so the corner's two edges still differ; it is
/// split into at least three edges first.
/// </remarks>
internal static class EdgeColoring
{
    /// <summary>
    /// A join is a corner when the tangents turn back (dot ≤ 0) or turn by more than this sine —
    /// sin(3 rad) ≈ 0.141, about 8°, msdfgen's default threshold. Lower values turn the slight
    /// tangent breaks of rounded integer outlines into spurious corners.
    /// </summary>
    public const double CornerSineThreshold = 0.14112000805986721;

    private static readonly EdgeColor[] Palette = [EdgeColor.Cyan, EdgeColor.Magenta, EdgeColor.Yellow];

    /// <summary>True when the turn from tangent a to tangent b is a corner.</summary>
    public static bool IsCorner(double ax, double ay, double bx, double by) =>
        (ax * bx) + (ay * by) <= 0 || Math.Abs((ax * by) - (ay * bx)) > CornerSineThreshold;

    /// <summary>True when the join before edge <paramref name="index"/> of the contour is a corner.</summary>
    public static bool IsCornerBefore(IReadOnlyList<int> contour, IReadOnlyList<DistanceEdge> edges, int index)
    {
        var previous = edges[contour[(index + contour.Count - 1) % contour.Count]];
        var current = edges[contour[index]];
        return IsCorner(previous.EndDirX, previous.EndDirY, current.StartDirX, current.StartDirY);
    }

    /// <summary>Colors every edge, splitting teardrop contours' edges where needed.</summary>
    public static void Apply(List<List<int>> contours, List<DistanceEdge> edges)
    {
        foreach (var contour in contours)
        {
            var corners = new List<int>();
            for (int i = 0; i < contour.Count; i++)
            {
                if (IsCornerBefore(contour, edges, i))
                    corners.Add(i);
            }

            if (corners.Count == 0)
            {
                foreach (int e in contour)
                    edges[e].Color = EdgeColor.White;
            }
            else if (corners.Count == 1)
            {
                ColorTeardrop(contour, edges, corners[0]);
            }
            else
            {
                ColorSplines(contour, edges, corners);
            }
        }
    }

    private static void ColorSplines(List<int> contour, List<DistanceEdge> edges, List<int> corners)
    {
        int m = contour.Count;
        int splineCount = corners.Count;
        int start = corners[0];
        int spline = 0;
        int nextCorner = 1;

        for (int j = 0; j < m; j++)
        {
            int index = (start + j) % m;
            if (nextCorner < splineCount && index == corners[nextCorner])
            {
                spline++;
                nextCorner++;
            }

            // Rotation alone would give the last spline the first spline's color whenever
            // splineCount ≡ 1 (mod 3); the middle color differs from both of its neighbours.
            var color = spline == splineCount - 1 && splineCount % 3 == 1
                ? Palette[1]
                : Palette[spline % 3];
            edges[contour[index]].Color = color;
        }
    }

    private static void ColorTeardrop(List<int> contour, List<DistanceEdge> edges, int corner)
    {
        // Rotate so the contour starts at the corner, then make sure there are three edges.
        var ordered = new List<int>(contour.Count);
        for (int j = 0; j < contour.Count; j++)
            ordered.Add(contour[(corner + j) % contour.Count]);

        while (ordered.Count < 3)
        {
            int longest = 0;
            for (int j = 1; j < ordered.Count; j++)
            {
                if (edges[ordered[j]].Points.Length > edges[ordered[longest]].Points.Length)
                    longest = j;
            }

            var (first, second) = Split(edges[ordered[longest]]);
            edges[ordered[longest]] = first;
            ordered.Insert(longest + 1, edges.Count);
            edges.Add(second);
        }

        contour.Clear();
        contour.AddRange(ordered);

        EdgeColor[] thirds = [EdgeColor.Magenta, EdgeColor.White, EdgeColor.Yellow];
        int m = contour.Count;
        for (int j = 0; j < m; j++)
            edges[contour[j]].Color = thirds[Math.Min(2, j * 3 / m)];
    }

    /// <summary>
    /// Splits an edge's polyline in two at a vertex near its middle (inserting the midpoint of
    /// a single segment first). The new inner ends take the polyline's direction there.
    /// </summary>
    private static (DistanceEdge First, DistanceEdge Second) Split(DistanceEdge edge)
    {
        var p = edge.Points;
        if (p.Length == 4)
            p = [p[0], p[1], (p[0] + p[2]) / 2, (p[1] + p[3]) / 2, p[2], p[3]];

        int pointCount = p.Length / 2;
        int mid = pointCount / 2;
        var a = p[..((mid + 1) * 2)];
        var b = p[(mid * 2)..];

        var (dx, dy) = Direction(p, mid - 1, mid);
        var (ex, ey) = Direction(p, mid, mid + 1);
        return (
            new DistanceEdge(a, edge.StartDirX, edge.StartDirY, dx, dy) { Color = edge.Color },
            new DistanceEdge(b, ex, ey, edge.EndDirX, edge.EndDirY) { Color = edge.Color });

        static (float X, float Y) Direction(float[] p, int i, int j)
        {
            float x = p[2 * j] - p[2 * i], y = p[(2 * j) + 1] - p[(2 * i) + 1];
            float length = MathF.Sqrt((x * x) + (y * y));
            return length > 0 ? (x / length, y / length) : (0, 0);
        }
    }
}
