using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Distance;

/// <summary>
/// The MSDF channel set an edge contributes to. Single-channel SDF leaves every edge
/// <see cref="White"/>.
/// </summary>
[Flags]
internal enum EdgeColor : byte
{
    None = 0,
    Red = 1,
    Green = 2,
    Blue = 4,
    Yellow = Red | Green,
    Magenta = Red | Blue,
    Cyan = Green | Blue,
    White = Red | Green | Blue,
}

/// <summary>
/// One outline edge — a line or a cubic between two on-curve points — flattened to a polyline
/// in field coordinates, with the true tangents at its ends.
/// </summary>
internal sealed class DistanceEdge
{
    public DistanceEdge(float[] points, float startDirX, float startDirY, float endDirX, float endDirY)
    {
        Points = points;
        StartDirX = startDirX;
        StartDirY = startDirY;
        EndDirX = endDirX;
        EndDirY = endDirY;
    }

    /// <summary>Polyline vertices as x0, y0, x1, y1, …; at least two points.</summary>
    public float[] Points { get; }

    /// <summary>Unit tangent at the edge's start, from its control points (not the polyline).</summary>
    public float StartDirX { get; }

    /// <inheritdoc cref="StartDirX"/>
    public float StartDirY { get; }

    /// <summary>Unit tangent at the edge's end, from its control points (not the polyline).</summary>
    public float EndDirX { get; }

    /// <inheritdoc cref="EndDirX"/>
    public float EndDirY { get; }

    /// <summary>The channels this edge feeds. Assigned by MSDF edge coloring.</summary>
    public EdgeColor Color { get; set; } = EdgeColor.White;
}

/// <summary>
/// A straight piece of the filled region's boundary. <see cref="OpensEdge"/> and
/// <see cref="ClosesEdge"/> mark pieces that end at one of the original edge's endpoints,
/// where MSDF may extend the edge along its tangent (pseudo-distance).
/// </summary>
internal readonly record struct BoundarySegment(
    float X0, float Y0, float X1, float Y1, int EdgeIndex, bool OpensEdge, bool ClosesEdge);

/// <summary>
/// A glyph outline prepared for distance-field generation, in field coordinates (pixels, Y
/// down, origin at the field's top-left corner, texel centres at half-integers).
/// </summary>
/// <remarks>
/// Every curve is flattened with Wang's bound, so the polyline is within
/// <c>tolerance</c> pixels of the true curve everywhere and every distance measured to it is
/// within <c>tolerance</c> of the distance to the curve.
///
/// INVARIANT: distances are measured to <see cref="Boundary"/> — the outline of the filled
/// region under the non-zero rule — never to raw contours. Where contours overlap, the parts
/// of each that lie inside another are removed, so the hidden interior edges of overlapping
/// contours do not dig a near-zero seam through the glyph. Inside/outside comes from
/// <see cref="Winding"/>, which sees every contour unclipped.
/// </remarks>
internal sealed class DistanceShape
{
    /// <summary>Upper bound on the segments one cubic flattens to, against degenerate control points.</summary>
    private const int MaxCubicSegments = 1024;

    /// <summary>Offset, in pixels, of the probe points either side of a boundary piece.</summary>
    private const double ProbeOffset = 1.0 / 1024;

    private DistanceShape(List<List<int>> contours, List<DistanceEdge> edges, BoundarySegment[] boundary, WindingIndex winding, int polarity)
    {
        Contours = contours;
        Edges = edges;
        Boundary = boundary;
        Winding = winding;
        Polarity = polarity;
    }

    /// <summary>Each contour as an ordered, cyclic list of indices into <see cref="Edges"/>.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Contours { get; }

    /// <summary>Every non-degenerate edge, contour by contour.</summary>
    public IReadOnlyList<DistanceEdge> Edges { get; }

    /// <summary>The boundary of the filled region, as straight pieces of <see cref="Edges"/>.</summary>
    public BoundarySegment[] Boundary { get; }

    /// <summary>Non-zero winding queries over the unclipped outline.</summary>
    public WindingIndex Winding { get; }

    /// <summary>
    /// +1 when the outline's net orientation puts the filled side on the positive side of
    /// <c>cross(edge direction, point − edge)</c>, −1 when on the negative side.
    /// </summary>
    public int Polarity { get; }

    /// <summary>Builds the shape for an outline.</summary>
    /// <param name="outline">The outline, in its own units.</param>
    /// <param name="toPixels">Maps the outline to pixel space (Y down, baseline on row 0).</param>
    /// <param name="originX">Pixel-space X of the field's left edge.</param>
    /// <param name="originY">Pixel-space Y of the field's top edge.</param>
    /// <param name="tolerance">Maximum distance, in pixels, between a curve and its polyline.</param>
    /// <param name="colorEdges">Assign MSDF channels with <see cref="EdgeColoring"/>; may split
    /// edges, so it runs before the boundary is resolved.</param>
    public static DistanceShape Build(GlyphOutline outline, OutlineTransform toPixels, float originX, float originY, float tolerance, bool colorEdges = false)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var edges = new List<DistanceEdge>();
        var contours = new List<List<int>>();
        List<int>? contour = null;
        double penX = 0, penY = 0;

        foreach (var command in outline.Commands)
        {
            double x = toPixels.ToPixelX(command.EndX) - (double)originX;
            double y = toPixels.ToPixelY(command.EndY) - (double)originY;

            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                    contour = [];
                    break;

                case OutlineCommandType.CubicTo:
                    contour ??= [];
                    AddCubic(
                        edges, contour, penX, penY,
                        toPixels.ToPixelX(command.Ctrl1X) - (double)originX, toPixels.ToPixelY(command.Ctrl1Y) - (double)originY,
                        toPixels.ToPixelX(command.Ctrl2X) - (double)originX, toPixels.ToPixelY(command.Ctrl2Y) - (double)originY,
                        x, y, tolerance);
                    break;

                case OutlineCommandType.LineTo:
                case OutlineCommandType.Close:
                default:
                    contour ??= [];
                    AddLine(edges, contour, penX, penY, x, y);
                    if (command.Type == OutlineCommandType.Close)
                    {
                        if (contour.Count > 0)
                            contours.Add(contour);
                        contour = null;
                    }

                    break;
            }

            penX = x;
            penY = y;
        }

        if (contour is { Count: > 0 })
            contours.Add(contour);

        if (colorEdges)
            EdgeColoring.Apply(contours, edges);

        var winding = WindingIndex.Create(edges);
        var boundary = ResolveBoundary(contours, edges, winding);
        return new DistanceShape(contours, edges, boundary, winding, OrientationOf(edges));
    }

    private static void AddLine(List<DistanceEdge> edges, List<int> contour, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-9)
            return;

        float ux = (float)(dx / length), uy = (float)(dy / length);
        contour.Add(edges.Count);
        edges.Add(new DistanceEdge([(float)x0, (float)y0, (float)x1, (float)y1], ux, uy, ux, uy));
    }

    private static void AddCubic(
        List<DistanceEdge> edges, List<int> contour,
        double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3,
        float tolerance)
    {
        var (sx, sy) = FirstNonZero(x1 - x0, y1 - y0, x2 - x0, y2 - y0, x3 - x0, y3 - y0);
        var (ex, ey) = FirstNonZero(x3 - x2, y3 - y2, x3 - x1, y3 - y1, x3 - x0, y3 - y0);
        if (sx == 0 && sy == 0)
            return; // all four points coincide

        // Wang's bound: n uniform steps in t keep a cubic within `tolerance` of its chords
        // when n >= sqrt(3·M / (4·tolerance)), M the largest second difference of the controls.
        double m = Math.Max(
            Math.Sqrt(Square(x0 - (2 * x1) + x2) + Square(y0 - (2 * y1) + y2)),
            Math.Sqrt(Square(x1 - (2 * x2) + x3) + Square(y1 - (2 * y2) + y3)));
        int n = (int)Math.Ceiling(Math.Sqrt(3 * m / (4 * (double)tolerance)));
        n = Math.Clamp(n, 1, MaxCubicSegments);

        var points = new float[(n + 1) * 2];
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, mt = 1 - t;
            double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
            points[2 * i] = (float)((a * x0) + (b * x1) + (c * x2) + (d * x3));
            points[(2 * i) + 1] = (float)((a * y0) + (b * y1) + (c * y2) + (d * y3));
        }

        contour.Add(edges.Count);
        edges.Add(new DistanceEdge(points, (float)sx, (float)sy, (float)ex, (float)ey));

        static (double X, double Y) FirstNonZero(double ax, double ay, double bx, double by, double cx, double cy)
        {
            foreach (var (x, y) in new[] { (ax, ay), (bx, by), (cx, cy) })
            {
                double length = Math.Sqrt((x * x) + (y * y));
                if (length > 1e-9)
                    return (x / length, y / length);
            }

            return (0, 0);
        }
    }

    private static double Square(double v) => v * v;

    /// <summary>The sign of the outline's total signed area — which side of its edges is filled.</summary>
    private static int OrientationOf(List<DistanceEdge> edges)
    {
        double area = 0;
        foreach (var edge in edges)
        {
            var p = edge.Points;
            for (int i = 0; i + 3 < p.Length; i += 2)
                area += ((double)p[i] * p[i + 3]) - ((double)p[i + 2] * p[i + 1]);
        }

        return area >= 0 ? 1 : -1;
    }

    /// <summary>
    /// Keeps the parts of the outline that separate filled from unfilled space. Contours are
    /// split wherever they meet another part of the outline; each piece survives only if
    /// exactly one of its two sides is filled. A contour that meets nothing is uniform along
    /// its length, so one probe decides all of it.
    /// </summary>
    private static BoundarySegment[] ResolveBoundary(List<List<int>> contours, List<DistanceEdge> edges, WindingIndex winding)
    {
        var segments = new List<RawSegment>();
        for (int c = 0; c < contours.Count; c++)
        {
            int first = segments.Count;
            foreach (int edgeIndex in contours[c])
            {
                var p = edges[edgeIndex].Points;
                int edgeFirst = segments.Count;
                for (int i = 0; i + 3 < p.Length; i += 2)
                {
                    // Zero-length steps (a cusp sampled twice) carry no boundary.
                    if (p[i].Equals(p[i + 2]) && p[i + 1].Equals(p[i + 3]))
                        continue;

                    segments.Add(new RawSegment(
                        p[i], p[i + 1], p[i + 2], p[i + 3],
                        edgeIndex, c, segments.Count - first, segments.Count == edgeFirst, false));
                }

                if (segments.Count > edgeFirst)
                    segments[^1] = segments[^1] with { LastInEdge = true };
            }

            for (int i = first; i < segments.Count; i++)
                segments[i] = segments[i] with { ContourLength = segments.Count - first };
        }

        var splits = FindSplits(segments);
        var boundary = new List<BoundarySegment>(segments.Count);

        if (splits is null)
        {
            // Nothing touches anything else: classify each contour once, by its longest piece.
            int start = 0;
            while (start < segments.Count)
            {
                int end = start;
                int longest = start;
                while (end < segments.Count && segments[end].Contour == segments[start].Contour)
                {
                    if (segments[end].LengthSquared > segments[longest].LengthSquared)
                        longest = end;
                    end++;
                }

                var probe = segments[longest];
                if (IsBoundary(winding, probe.X0, probe.Y0, probe.X1, probe.Y1))
                {
                    for (int i = start; i < end; i++)
                        boundary.Add(segments[i].ToBoundary());
                }

                start = end;
            }

            return [.. boundary];
        }

        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var ts = splits[i];
            if (ts is null)
            {
                if (IsBoundary(winding, s.X0, s.Y0, s.X1, s.Y1))
                    boundary.Add(s.ToBoundary());
                continue;
            }

            ts.Sort();
            double previous = 0;
            for (int k = 0; k <= ts.Count; k++)
            {
                double next = k < ts.Count ? ts[k] : 1;
                if (next - previous <= 1e-9)
                    continue;

                float ax = (float)(s.X0 + ((s.X1 - s.X0) * previous)), ay = (float)(s.Y0 + ((s.Y1 - s.Y0) * previous));
                float bx = (float)(s.X0 + ((s.X1 - s.X0) * next)), by = (float)(s.Y0 + ((s.Y1 - s.Y0) * next));
                if (IsBoundary(winding, ax, ay, bx, by))
                {
                    boundary.Add(new BoundarySegment(
                        ax, ay, bx, by, s.EdgeIndex,
                        s.FirstInEdge && previous <= 0,
                        s.LastInEdge && next >= 1));
                }

                previous = next;
            }
        }

        return [.. boundary];
    }

    /// <summary>True when exactly one side of the piece is filled.</summary>
    private static bool IsBoundary(WindingIndex winding, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-9)
            return false;

        double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        double nx = -dy / length * ProbeOffset, ny = dx / length * ProbeOffset;
        return winding.IsInside(mx + nx, my + ny) != winding.IsInside(mx - nx, my - ny);
    }

    /// <summary>
    /// Finds where outline segments meet — crossings, touches and collinear overlaps — between
    /// any two segments that are not neighbours on one contour. Returns, per segment, the
    /// parameters to split it at, or null when no two segments meet at all.
    /// </summary>
    private static List<double>?[]? FindSplits(List<RawSegment> segments)
    {
        int n = segments.Count;
        var order = new int[n];
        var minX = new double[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
            minX[i] = Math.Min(segments[i].X0, segments[i].X1);
        }

        Array.Sort(minX, order);

        List<double>?[]? splits = null;
        bool any = false;

        for (int oi = 0; oi < n; oi++)
        {
            var a = segments[order[oi]];
            double aMaxX = Math.Max(a.X0, a.X1);
            double aMinY = Math.Min(a.Y0, a.Y1), aMaxY = Math.Max(a.Y0, a.Y1);

            for (int oj = oi + 1; oj < n && minX[oj] <= aMaxX; oj++)
            {
                var b = segments[order[oj]];
                if (Math.Max(b.Y0, b.Y1) < aMinY || Math.Min(b.Y0, b.Y1) > aMaxY)
                    continue;
                if (AreNeighbours(a, b))
                    continue;

                if (Intersect(a, b, out var tA, out var tB))
                {
                    any = true;
                    splits ??= new List<double>?[n];
                    AddSplits(ref splits[order[oi]], tA);
                    AddSplits(ref splits[order[oj]], tB);
                }
            }
        }

        return any ? splits : null;

        static void AddSplits(ref List<double>? list, List<double>? ts)
        {
            list ??= [];
            if (ts is null)
                return;

            foreach (double t in ts)
            {
                if (t > 1e-9 && t < 1 - 1e-9)
                    list.Add(t);
            }
        }
    }

    private static bool AreNeighbours(RawSegment a, RawSegment b)
    {
        if (a.Contour != b.Contour)
            return false;

        int gap = Math.Abs(a.Position - b.Position);
        return gap == 1 || gap == a.ContourLength - 1 || gap == 0;
    }

    /// <summary>
    /// Intersects two segments. Returns true when they meet anywhere (touching included);
    /// <paramref name="tA"/>/<paramref name="tB"/> receive the parameters on each where they
    /// meet, collinear overlaps contributing each other's endpoints.
    /// </summary>
    private static bool Intersect(RawSegment a, RawSegment b, out List<double>? tA, out List<double>? tB)
    {
        tA = null;
        tB = null;

        double rx = a.X1 - a.X0, ry = a.Y1 - a.Y0;
        double sx = b.X1 - b.X0, sy = b.Y1 - b.Y0;
        double qx = b.X0 - a.X0, qy = b.Y0 - a.Y0;
        double denominator = (rx * sy) - (ry * sx);
        double rLength = Math.Sqrt((rx * rx) + (ry * ry));
        double sLength = Math.Sqrt((sx * sx) + (sy * sy));

        if (Math.Abs(denominator) > 1e-12 * rLength * sLength)
        {
            double t = ((qx * sy) - (qy * sx)) / denominator;
            double u = ((qx * ry) - (qy * rx)) / denominator;
            const double slack = 1e-9;
            if (t < -slack || t > 1 + slack || u < -slack || u > 1 + slack)
                return false;

            tA = [t];
            tB = [u];
            return true;
        }

        // Parallel: only collinear overlaps matter.
        if (Math.Abs((qx * ry) - (qy * rx)) > 1e-9 * rLength)
            return false;

        double rr = (rx * rx) + (ry * ry), ss = (sx * sx) + (sy * sy);
        double tb0 = ((qx * rx) + (qy * ry)) / rr;
        double tb1 = (((b.X1 - a.X0) * rx) + ((b.Y1 - a.Y0) * ry)) / rr;
        if (Math.Max(tb0, tb1) < 0 || Math.Min(tb0, tb1) > 1)
            return false;

        double ua0 = (((a.X0 - b.X0) * sx) + ((a.Y0 - b.Y0) * sy)) / ss;
        double ua1 = (((a.X1 - b.X0) * sx) + ((a.Y1 - b.Y0) * sy)) / ss;
        tA = [tb0, tb1];
        tB = [ua0, ua1];
        return true;
    }

    private readonly record struct RawSegment(
        double X0, double Y0, double X1, double Y1,
        int EdgeIndex, int Contour, int Position, bool FirstInEdge, bool LastInEdge, int ContourLength = 0)
    {
        public double LengthSquared => ((X1 - X0) * (X1 - X0)) + ((Y1 - Y0) * (Y1 - Y0));

        public BoundarySegment ToBoundary() =>
            new((float)X0, (float)Y0, (float)X1, (float)Y1, EdgeIndex, FirstInEdge, LastInEdge);
    }
}
