namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>How a stroke fills the gap on the outer side of a corner.</summary>
internal enum StrokeJoin
{
    /// <summary>Extends both edges to a point; falls back to <see cref="Bevel"/> past the miter limit.</summary>
    Miter,

    /// <summary>A circular arc centred on the corner.</summary>
    Round,

    /// <summary>A straight cut between the two edges' offset ends.</summary>
    Bevel,
}

/// <summary>
/// Builds the stroke of an outline: the region within half the stroke width of any contour,
/// with the requested corner joins, as an outline of its own.
/// </summary>
/// <remarks>
/// Each contour is flattened to a polyline in font units, then every edge contributes a
/// rectangle and every corner a join wedge on its outer side, all wound clockwise; the
/// rasterizer's non-zero fill draws their union. Unlike offsetting each contour to both sides
/// and pairing the results, the union cannot invert or punch holes where an offset
/// self-intersects — curves tighter than half the stroke width, and cusps — so it needs no
/// loop removal. Where two overlapping pieces' edges cross the same pixel, that anti-aliased
/// edge pixel can come out slightly darker than the exact union.
///
/// Glyph contours are always closed, so there are no caps.
/// </remarks>
internal static class OutlineStroker
{
    /// <summary>
    /// The default miter limit: a miter whose tip would lie more than this many half-widths from
    /// the corner (an interior angle under 60°) is beveled instead.
    /// </summary>
    public const float DefaultMiterLimit = 2f;

    /// <summary>Most segments one cubic is flattened into, whatever the tolerance.</summary>
    private const int MaxCubicSegments = 1024;

    /// <summary>Strokes every contour of <paramref name="outline"/>.</summary>
    /// <param name="outline">The outline, in font units.</param>
    /// <param name="width">Full stroke width, in font units; half lies each side of the contour.</param>
    /// <param name="join">The corner join.</param>
    /// <param name="miterLimit">For <see cref="StrokeJoin.Miter"/>: the largest allowed distance from
    /// corner to miter tip, in half-widths (the reciprocal sine of half the interior angle).</param>
    /// <param name="tolerance">The largest distance, in font units, a flattened curve or round join may
    /// stray from the true shape.</param>
    /// <returns>The stroke outline; empty when the input is.</returns>
    public static GlyphOutline Stroke(GlyphOutline outline, float width, StrokeJoin join, float miterLimit, float tolerance)
    {
        ArgumentNullException.ThrowIfNull(outline);
        if (!(width > 0) || !float.IsFinite(width))
            throw new ArgumentOutOfRangeException(nameof(width), width, "Stroke width must be positive and finite.");
        if (!(miterLimit >= 1) || !float.IsFinite(miterLimit))
            throw new ArgumentOutOfRangeException(nameof(miterLimit), miterLimit, "Miter limit must be at least 1.");
        if (!(tolerance > 0) || !float.IsFinite(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive and finite.");

        if (outline.IsEmpty)
            return GlyphOutline.Empty;

        var pieces = new List<OutlineCommand>();
        var contour = new List<Point>();
        var stroker = new ContourStroker(pieces, width / 2.0, join, miterLimit, tolerance);
        double currentX = 0, currentY = 0;

        foreach (var command in outline.Commands)
        {
            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                    contour.Clear();
                    contour.Add(new Point(command.EndX, command.EndY));
                    break;
                case OutlineCommandType.LineTo:
                    contour.Add(new Point(command.EndX, command.EndY));
                    break;
                case OutlineCommandType.CubicTo:
                    FlattenCubic(currentX, currentY, command, tolerance, contour);
                    break;
                case OutlineCommandType.Close:
                    stroker.Stroke(contour);
                    contour.Clear();
                    break;
            }

            currentX = command.EndX;
            currentY = command.EndY;
        }

        return pieces.Count == 0 ? GlyphOutline.Empty : GlyphOutline.FromCommands([.. pieces]);
    }

    /// <summary>
    /// Appends a cubic's polyline points (excluding its start) in uniform steps fine enough that
    /// no chord strays more than <paramref name="tolerance"/>: a chord over a parameter step h
    /// deviates at most <c>max|B''| · h² / 8</c>, and
    /// <c>max|B''| ≤ 6 · max(|p0 − 2p1 + p2|, |p1 − 2p2 + p3|)</c>.
    /// </summary>
    private static void FlattenCubic(double x0, double y0, OutlineCommand cubic, float tolerance, List<Point> points)
    {
        double x1 = cubic.Ctrl1X, y1 = cubic.Ctrl1Y;
        double x2 = cubic.Ctrl2X, y2 = cubic.Ctrl2Y;
        double x3 = cubic.EndX, y3 = cubic.EndY;

        double dd = Math.Max(
            Math.Sqrt(Square(x0 - (2 * x1) + x2) + Square(y0 - (2 * y1) + y2)),
            Math.Sqrt(Square(x1 - (2 * x2) + x3) + Square(y1 - (2 * y2) + y3)));
        int steps = (int)Math.Clamp(Math.Ceiling(Math.Sqrt(0.75 * dd / tolerance)), 1, MaxCubicSegments);

        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;
            double mt = 1 - t;
            double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
            points.Add(new Point((a * x0) + (b * x1) + (c * x2) + (d * x3), (a * y0) + (b * y1) + (c * y2) + (d * y3)));
        }

        points.Add(new Point(x3, y3));
    }

    private static double Square(double v) => v * v;

    private readonly record struct Point(double X, double Y);

    /// <summary>Emits the rectangles and join wedges for one closed polyline.</summary>
    private sealed class ContourStroker(List<OutlineCommand> output, double half, StrokeJoin join, float miterLimit, float tolerance)
    {
        private readonly List<Point> _points = [];
        private readonly List<Point> _polygon = [];

        public void Stroke(List<Point> contour)
        {
            // Drop zero-length edges, the closing one included.
            _points.Clear();
            foreach (var p in contour)
            {
                if (_points.Count == 0 || p != _points[^1])
                    _points.Add(p);
            }

            while (_points.Count > 1 && _points[^1] == _points[0])
                _points.RemoveAt(_points.Count - 1);

            int n = _points.Count;
            if (n < 2)
                return;

            for (int i = 0; i < n; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % n];
                var (dx, dy) = Direction(a, b);
                double nx = -dy * half, ny = dx * half;
                Emit(new Point(a.X + nx, a.Y + ny), new Point(b.X + nx, b.Y + ny), new Point(b.X - nx, b.Y - ny), new Point(a.X - nx, a.Y - ny));
            }

            for (int i = 0; i < n; i++)
            {
                var previous = _points[(i + n - 1) % n];
                var corner = _points[i];
                var next = _points[(i + 1) % n];
                Join(corner, Direction(previous, corner), Direction(corner, next));
            }
        }

        /// <summary>Fills the outer-side gap at a corner between unit directions d0 and d1.</summary>
        private void Join(Point v, (double X, double Y) d0, (double X, double Y) d1)
        {
            double cross = (d0.X * d1.Y) - (d0.Y * d1.X);
            double dot = (d0.X * d1.X) + (d0.Y * d1.Y);
            bool straight = Math.Abs(cross) < 1e-12;
            if (straight && dot > 0)
                return; // the rectangles already meet

            bool reversal = straight;

            // The gap opens away from the turn: right of a left turn, left of a right turn.
            double side = cross > 0 ? -1 : 1;
            var n0 = (X: -d0.Y * side, Y: d0.X * side);
            var n1 = (X: -d1.Y * side, Y: d1.X * side);
            var start = new Point(v.X + (n0.X * half), v.Y + (n0.Y * half));
            var end = new Point(v.X + (n1.X * half), v.Y + (n1.Y * half));

            switch (join)
            {
                case StrokeJoin.Round:
                    // The short way from n0 to n1 turns the same way as the path; a reversal
                    // has no short way, so its arc goes round the front of the corner.
                    double sign = reversal ? -side : Math.Sign(cross);
                    RoundJoin(v, n0, start, end, sign, Math.Acos(Math.Clamp(dot, -1, 1)));
                    return;

                case StrokeJoin.Miter when !reversal && 1 / Math.Sqrt((1 + dot) / 2) <= miterLimit:
                    double k = half / (1 + dot);
                    Emit(v, start, new Point(v.X + ((n0.X + n1.X) * k), v.Y + ((n0.Y + n1.Y) * k)), end);
                    return;

                default:
                    Emit(v, start, end);
                    return;
            }
        }

        /// <summary>A fan around the corner from <paramref name="start"/> to <paramref name="end"/>.</summary>
        private void RoundJoin(Point v, (double X, double Y) n0, Point start, Point end, double sign, double sweep)
        {
            // The largest step whose chord stays within tolerance of the arc.
            double maxStep = tolerance < half ? 2 * Math.Acos(1 - (tolerance / half)) : Math.PI / 2;
            int steps = Math.Max(1, (int)Math.Ceiling(sweep / maxStep));

            _polygon.Clear();
            _polygon.Add(v);
            _polygon.Add(start);
            double angle0 = Math.Atan2(n0.Y, n0.X);
            for (int i = 1; i < steps; i++)
            {
                double angle = angle0 + (sign * sweep * i / steps);
                _polygon.Add(new Point(v.X + (Math.Cos(angle) * half), v.Y + (Math.Sin(angle) * half)));
            }

            _polygon.Add(end);
            EmitPolygon();
        }

        private void Emit(Point a, Point b, Point c)
        {
            _polygon.Clear();
            _polygon.Add(a);
            _polygon.Add(b);
            _polygon.Add(c);
            EmitPolygon();
        }

        private void Emit(Point a, Point b, Point c, Point d)
        {
            _polygon.Clear();
            _polygon.Add(a);
            _polygon.Add(b);
            _polygon.Add(c);
            _polygon.Add(d);
            EmitPolygon();
        }

        /// <summary>Emits <see cref="_polygon"/> as a clockwise contour, or nothing if it has no area.</summary>
        private void EmitPolygon()
        {
            double area = 0;
            for (int i = 0; i < _polygon.Count; i++)
            {
                var p = _polygon[i];
                var q = _polygon[(i + 1) % _polygon.Count];
                area += (p.X * q.Y) - (q.X * p.Y);
            }

            if (area.Equals(0d) || !double.IsFinite(area))
                return;

            if (area > 0)
                _polygon.Reverse();

            output.Add(OutlineCommand.Move((float)_polygon[0].X, (float)_polygon[0].Y));
            for (int i = 1; i < _polygon.Count; i++)
                output.Add(OutlineCommand.Line((float)_polygon[i].X, (float)_polygon[i].Y));
            output.Add(OutlineCommand.CloseAt((float)_polygon[0].X, (float)_polygon[0].Y));
        }

        private static (double X, double Y) Direction(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            return (dx / length, dy / length);
        }
    }
}
