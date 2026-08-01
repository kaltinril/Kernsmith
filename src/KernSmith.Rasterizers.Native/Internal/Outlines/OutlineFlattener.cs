namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Converts a <see cref="GlyphOutline"/> into directed <see cref="EdgeSegment"/>s in pixel
/// space: scales and flips the coordinates, then flattens every cubic with adaptive
/// De Casteljau subdivision.
/// </summary>
internal static class OutlineFlattener
{
    /// <summary>
    /// Default flatness tolerance, in pixels. A curve is emitted as a straight segment once
    /// its control points sit within this distance of its chord.
    /// </summary>
    public const float DefaultTolerance = 0.25f;

    /// <summary>
    /// Hard limit on subdivision recursion. Reached only by degenerate outlines with control
    /// points enormously far from the curve; 2^16 segments is far beyond visually useful.
    /// </summary>
    public const int MaxSubdivisionDepth = 16;

    /// <summary>
    /// Flattens an outline into the edges the scanline rasterizer consumes.
    /// </summary>
    /// <param name="outline">The extracted outline, in font units.</param>
    /// <param name="transform">The font-unit to pixel mapping.</param>
    /// <param name="tolerance">Maximum flattening error in pixels.</param>
    /// <returns>
    /// The contours' edges in outline order, keeping their original winding. Horizontal
    /// edges are omitted: they cross no scanline and contribute no coverage.
    /// </returns>
    public static EdgeSegment[] Flatten(GlyphOutline outline, OutlineTransform transform, float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return [];

        if (!(tolerance > 0f))
            tolerance = DefaultTolerance;

        var edges = new List<EdgeSegment>(outline.Commands.Length * 4);

        float currentX = 0f, currentY = 0f;

        foreach (var command in outline.Commands)
        {
            float endX = transform.ToPixelX(command.EndX);
            float endY = transform.ToPixelY(command.EndY);

            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                    // Lifts the pen; the contour's start is carried on its Close command.
                    break;

                case OutlineCommandType.LineTo:
                    AddEdge(edges, currentX, currentY, endX, endY);
                    break;

                case OutlineCommandType.CubicTo:
                    FlattenCubic(
                        edges,
                        currentX, currentY,
                        transform.ToPixelX(command.Ctrl1X), transform.ToPixelY(command.Ctrl1Y),
                        transform.ToPixelX(command.Ctrl2X), transform.ToPixelY(command.Ctrl2Y),
                        endX, endY,
                        tolerance,
                        depth: 0);
                    break;

                case OutlineCommandType.Close:
                default:
                    // Close carries the contour's start point, so the closing segment needs
                    // no extra bookkeeping here.
                    AddEdge(edges, currentX, currentY, endX, endY);
                    break;
            }

            currentX = endX;
            currentY = endY;
        }

        return edges.ToArray();
    }

    /// <summary>
    /// Recursively splits a cubic at its midpoint until it is flat enough to stand in for a
    /// straight segment, or until the depth cap stops it.
    /// </summary>
    private static void FlattenCubic(
        List<EdgeSegment> edges,
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float tolerance,
        int depth)
    {
        // The depth check comes first: with a tolerance the curve's own float precision can
        // never satisfy, the flatness test alone would subdivide essentially forever.
        if (depth >= MaxSubdivisionDepth || IsFlatEnough(x0, y0, x1, y1, x2, y2, x3, y3, tolerance))
        {
            AddEdge(edges, x0, y0, x3, y3);
            return;
        }

        // De Casteljau split at t = 0.5.
        float m01X = (x0 + x1) * 0.5f, m01Y = (y0 + y1) * 0.5f;
        float m12X = (x1 + x2) * 0.5f, m12Y = (y1 + y2) * 0.5f;
        float m23X = (x2 + x3) * 0.5f, m23Y = (y2 + y3) * 0.5f;
        float m012X = (m01X + m12X) * 0.5f, m012Y = (m01Y + m12Y) * 0.5f;
        float m123X = (m12X + m23X) * 0.5f, m123Y = (m12Y + m23Y) * 0.5f;
        float midX = (m012X + m123X) * 0.5f, midY = (m012Y + m123Y) * 0.5f;

        FlattenCubic(edges, x0, y0, m01X, m01Y, m012X, m012Y, midX, midY, tolerance, depth + 1);
        FlattenCubic(edges, midX, midY, m123X, m123Y, m23X, m23Y, x3, y3, tolerance, depth + 1);
    }

    /// <summary>
    /// True when both control points lie within <paramref name="tolerance"/> of the chord
    /// P0-P3. A cubic stays inside its control hull, so this bounds the error of replacing
    /// the curve with that chord.
    /// </summary>
    private static bool IsFlatEnough(
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float tolerance)
    {
        float chordX = x3 - x0;
        float chordY = y3 - y0;
        float chordLengthSquared = (chordX * chordX) + (chordY * chordY);

        if (chordLengthSquared <= float.Epsilon)
        {
            // A degenerate chord (the curve loops back on itself): measure the controls
            // against the shared endpoint instead.
            return SquaredDistance(x1, y1, x0, y0) <= tolerance * tolerance
                && SquaredDistance(x2, y2, x0, y0) <= tolerance * tolerance;
        }

        // Perpendicular distance to the chord, squared, without the square root:
        // |cross| / |chord| <= tolerance  =>  cross^2 <= tolerance^2 * |chord|^2.
        float cross1 = ((x1 - x0) * chordY) - ((y1 - y0) * chordX);
        float cross2 = ((x2 - x0) * chordY) - ((y2 - y0) * chordX);
        float limit = tolerance * tolerance * chordLengthSquared;

        return (cross1 * cross1) <= limit && (cross2 * cross2) <= limit;
    }

    private static float SquaredDistance(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Appends an edge unless it is horizontal, which no scanline can see.</summary>
    private static void AddEdge(List<EdgeSegment> edges, float x0, float y0, float x1, float y1)
    {
        if (y0 == y1)
            return;

        edges.Add(new EdgeSegment(x0, y0, x1, y1));
    }
}
