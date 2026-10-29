namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Synthetic-bold outline expansion: thickens the ink of an outline by a fixed amount, so
/// stems widen and counters close by the same amount.
/// </summary>
/// <remarks>
/// <para>
/// The outline is offset by half the strength as a polygon of its control points — on-curve
/// points and cubic control points alike. Offsetting a Bézier's control polygon edge by edge
/// approximates the offset of the curve itself (the Tiller–Hanson construction): each vertex
/// moves along the bisector of its two edges' outward normals, far enough that both edges move
/// out by exactly the offset (<c>d / cos(φ)</c>, φ being half the angle between the normals).
/// Smooth on-curve points sit between collinear control legs, so they simply move along their
/// normal.
/// </para>
/// <para>
/// "Outward" is decided once for the whole glyph from the sign of its total signed area, not
/// per contour: outer contours and counters wind in opposite directions, so a single choice
/// grows the strokes and shrinks the counters together, whichever direction the font winds.
/// The result is then moved up and right by half the strength, so the left and bottom edges of
/// the ink stay where they were and the glyph grows by the full strength to the right and up —
/// the placement the FreeType backend's bold has.
/// </para>
/// <para>
/// Two limits keep the offset well-behaved where the plain construction is not:
/// <list type="bullet">
/// <item>A corner sharper than <see cref="MiterLimit"/> allows (an angle under about 29°) gets
/// the midpoint of the bevel instead of the far-off miter tip, so hairline points do not grow
/// spikes and narrow notches (the crotch of an 'M') are not filled in from the tip.</item>
/// <item>Where an inward corner's offset slides it along an edge, the slides of an edge's two
/// ends may together not exceed its length; both are pulled back in proportion when they
/// would. A short edge between two inward corners therefore shrinks to nothing at worst and
/// never turns round into a reversed loop that would punch a hole in the ink.</item>
/// </list>
/// </para>
/// </remarks>
internal static class OutlineEmboldener
{
    /// <summary>
    /// The longest a corner's offset may be, in multiples of the offset distance, before the
    /// corner is beveled — the common stroke miter limit of 4 (an angle of about 29°).
    /// </summary>
    private const double MiterLimit = 4.0;

    /// <summary>Edges shorter than this (squared, in font units) have no usable direction.</summary>
    private const double MinEdgeLengthSquared = 1e-6;

    /// <summary>
    /// Expands an outline by <paramref name="strength"/> font units in total width and height.
    /// </summary>
    /// <returns>
    /// The emboldened outline; the input itself when it is empty; null when its contours enclose
    /// no signed area, so there is no inside to grow away from.
    /// </returns>
    public static GlyphOutline? Embolden(GlyphOutline outline, float strength)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return outline;

        var commands = outline.Commands;
        double area = SignedArea(commands);
        if (area == 0 || !double.IsFinite(area))
            return null;

        // Y up: a negative (clockwise) total area puts the ink on the right of travel, so the
        // outward normal of a direction (dx, dy) is its left-hand one, (-dy, dx).
        double side = area < 0 ? 1 : -1;
        double distance = strength / 2.0;
        float shift = strength / 2f;

        var result = new OutlineCommand[commands.Length];
        var points = new List<Point>();
        int start = 0;
        while (start < commands.Length)
        {
            int end = ContourEnd(commands, start);

            points.Clear();
            for (int i = start; i < end; i++)
                AppendPoints(commands[i], points);

            var offsets = OffsetContour(points, side, distance);
            WriteContour(commands, start, end, points, offsets, shift, result);
            start = end;
        }

        return GlyphOutline.FromCommands(result);
    }

    private readonly record struct Point(double X, double Y);

    /// <summary>
    /// The index just past the contour that starts at <paramref name="start"/>: past its
    /// <see cref="OutlineCommandType.Close"/>, or at the next <see cref="OutlineCommandType.MoveTo"/>.
    /// </summary>
    private static int ContourEnd(OutlineCommand[] commands, int start)
    {
        for (int i = start; i < commands.Length; i++)
        {
            if (commands[i].Type == OutlineCommandType.Close)
                return i + 1;

            if (i > start && commands[i].Type == OutlineCommandType.MoveTo)
                return i;
        }

        return commands.Length;
    }

    /// <summary>Appends the control-polygon vertices a command contributes, in path order.</summary>
    private static void AppendPoints(OutlineCommand command, List<Point> points)
    {
        switch (command.Type)
        {
            case OutlineCommandType.MoveTo:
            case OutlineCommandType.LineTo:
                points.Add(new Point(command.EndX, command.EndY));
                break;

            case OutlineCommandType.CubicTo:
                points.Add(new Point(command.Ctrl1X, command.Ctrl1Y));
                points.Add(new Point(command.Ctrl2X, command.Ctrl2Y));
                points.Add(new Point(command.EndX, command.EndY));
                break;

            // Close repeats the start point, which is already the first vertex.
        }
    }

    /// <summary>Twice the signed area of the control polygons of every contour (shoelace sum).</summary>
    private static double SignedArea(OutlineCommand[] commands)
    {
        double total = 0;
        var points = new List<Point>();
        int start = 0;
        while (start < commands.Length)
        {
            int end = ContourEnd(commands, start);

            points.Clear();
            for (int i = start; i < end; i++)
                AppendPoints(commands[i], points);

            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                total += (a.X * b.Y) - (b.X * a.Y);
            }

            start = end;
        }

        return total;
    }

    /// <summary>
    /// How far each vertex of one closed control polygon moves. A vertex that coincides with a
    /// neighbour takes its direction from the nearest edges that have a length, so coincident
    /// points move together and a zero-length edge never contributes a direction.
    /// </summary>
    private static Point[] OffsetContour(List<Point> points, double side, double distance)
    {
        int n = points.Count;
        var offsets = new Point[n];
        if (n < 2)
            return offsets;

        // Edge i runs from vertex i to vertex i + 1 (wrapping).
        var dirX = new double[n];
        var dirY = new double[n];
        var length = new double[n];
        bool anyEdge = false;
        for (int i = 0; i < n; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double squared = (dx * dx) + (dy * dy);
            if (!(squared > MinEdgeLengthSquared))
                continue;

            double len = Math.Sqrt(squared);
            dirX[i] = dx / len;
            dirY[i] = dy / len;
            length[i] = len;
            anyEdge = true;
        }

        if (!anyEdge)
            return offsets;

        // Each vertex's edges, and how far its offset slides it back along the incoming edge
        // and forward along the outgoing one. Only an inward corner slides into its edges; an
        // outward corner extends them, which costs nothing.
        var incomingEdge = new int[n];
        var outgoingEdge = new int[n];
        var slideIn = new double[n];
        var slideOut = new double[n];
        for (int i = 0; i < n; i++)
        {
            int incoming = (i - 1 + n) % n;
            while (length[incoming] == 0)
                incoming = (incoming - 1 + n) % n;

            int outgoing = i;
            while (length[outgoing] == 0)
                outgoing = (outgoing + 1) % n;

            var offset = CornerOffset(dirX[incoming], dirY[incoming], dirX[outgoing], dirY[outgoing], side, distance);
            offsets[i] = offset;
            incomingEdge[i] = incoming;
            outgoingEdge[i] = outgoing;
            slideIn[i] = Math.Max(0, -((offset.X * dirX[incoming]) + (offset.Y * dirY[incoming])));
            slideOut[i] = Math.Max(0, (offset.X * dirX[outgoing]) + (offset.Y * dirY[outgoing]));
        }

        // An edge's two ends may together slide no further than its length, or it would turn
        // round. Where they would, both ends are pulled back in proportion; a vertex then takes
        // the tighter of its two edges' limits.
        var edgeScale = new double[n];
        for (int e = 0; e < n; e++)
        {
            edgeScale[e] = 1;
            if (length[e] == 0)
                continue;

            // A valid edge e starts at vertex e and ends at vertex e + 1.
            double consumed = slideOut[e] + slideIn[(e + 1) % n];
            if (consumed > length[e])
                edgeScale[e] = length[e] / consumed;
        }

        for (int i = 0; i < n; i++)
        {
            double scale = Math.Min(edgeScale[incomingEdge[i]], edgeScale[outgoingEdge[i]]);
            if (scale < 1)
                offsets[i] = new Point(offsets[i].X * scale, offsets[i].Y * scale);
        }

        return offsets;
    }

    /// <summary>
    /// The offset of a vertex between an incoming edge of direction <c>a</c> and an outgoing edge
    /// of direction <c>b</c> (both unit vectors): the miter that moves both edges outward by
    /// <paramref name="distance"/>, or the bevel midpoint past the miter limit.
    /// </summary>
    private static Point CornerOffset(double ax, double ay, double bx, double by, double side, double distance)
    {
        // Outward unit normals of the two edges.
        double nax = -ay * side, nay = ax * side;
        double nbx = -by * side, nby = bx * side;

        // cos(2φ), with φ half the angle between the normals; 1 + cos(2φ) = 2cos²(φ).
        double cos2Phi = (nax * nbx) + (nay * nby);
        double onePlusCos = 1 + cos2Phi;
        double sumX = nax + nbx, sumY = nay + nby;

        // The path doubles straight back: the two offsets point in opposite directions and there
        // is no corner to move towards.
        if (onePlusCos < 1e-12)
            return default;

        // The miter, (na + nb) / (1 + cos 2φ), is 1 / cos(φ) long along the bisector.
        if (2 / onePlusCos <= MiterLimit * MiterLimit)
            return new Point(sumX / onePlusCos * distance, sumY / onePlusCos * distance);

        // Past the limit: the midpoint of the chord between the two edges' offset ends. For an
        // outward point that is the bevel; for an inward notch it keeps the notch where it was
        // rather than filling it in from the tip.
        return new Point(sumX / 2 * distance, sumY / 2 * distance);
    }

    /// <summary>Writes one contour's commands with every vertex offset and shifted.</summary>
    private static void WriteContour(
        OutlineCommand[] source, int start, int end,
        List<Point> points, Point[] offsets, float shift, OutlineCommand[] result)
    {
        int vertex = 0;
        for (int i = start; i < end; i++)
        {
            var command = source[i];
            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                case OutlineCommandType.LineTo:
                    {
                        var (x, y) = Moved(vertex++);
                        result[i] = command with { EndX = x, EndY = y };
                        break;
                    }

                case OutlineCommandType.CubicTo:
                    {
                        var (c1x, c1y) = Moved(vertex++);
                        var (c2x, c2y) = Moved(vertex++);
                        var (x, y) = Moved(vertex++);
                        result[i] = command with { Ctrl1X = c1x, Ctrl1Y = c1y, Ctrl2X = c2x, Ctrl2Y = c2y, EndX = x, EndY = y };
                        break;
                    }

                case OutlineCommandType.Close:
                    {
                        // The closing point is the contour's start, moved with it.
                        var (x, y) = points.Count > 0 ? Moved(0) : (command.EndX + shift, command.EndY + shift);
                        result[i] = command with { EndX = x, EndY = y };
                        break;
                    }
            }
        }

        (float X, float Y) Moved(int index) =>
            ((float)(points[index].X + offsets[index].X) + shift,
             (float)(points[index].Y + offsets[index].Y) + shift);
    }
}
