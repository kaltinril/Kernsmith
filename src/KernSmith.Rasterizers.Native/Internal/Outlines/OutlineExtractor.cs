using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Turns parsed <c>glyf</c> contours into a normalized <see cref="GlyphOutline"/>: one
/// <see cref="OutlineCommandType.MoveTo"/> per contour, straight and cubic segments in
/// between, and a closing <see cref="OutlineCommandType.Close"/>.
/// </summary>
/// <remarks>
/// TrueType stores quadratic Beziers; each is elevated to an exactly equivalent cubic so the
/// rasterizer only has to flatten one curve type.
/// </remarks>
internal static class OutlineExtractor
{
    /// <summary>
    /// Extracts the pen commands for a glyph. Coordinates stay in font design units.
    /// </summary>
    /// <param name="glyph">The parsed glyph, with composites already resolved.</param>
    /// <returns>The normalized outline; <see cref="GlyphOutline.Empty"/> for glyphs with nothing to draw.</returns>
    public static GlyphOutline Extract(ParsedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.IsEmpty)
            return GlyphOutline.Empty;

        var commands = new List<OutlineCommand>(glyph.PointCount + (glyph.Contours.Length * 2));
        foreach (var contour in glyph.Contours)
            AppendContour(commands, contour.Points);

        if (commands.Count == 0)
            return GlyphOutline.Empty;

        var extracted = commands.ToArray();
        var (xMin, yMin, xMax, yMax) = ComputeBounds(extracted);
        return new GlyphOutline(extracted, xMin, yMin, xMax, yMax);
    }

    /// <summary>
    /// Bounds every point a command touches, control points included. A cubic never leaves
    /// its control hull, so this is conservative — it can only over-estimate, never clip.
    /// </summary>
    private static (float XMin, float YMin, float XMax, float YMax) ComputeBounds(OutlineCommand[] commands)
    {
        float xMin = float.MaxValue, yMin = float.MaxValue;
        float xMax = float.MinValue, yMax = float.MinValue;

        foreach (var command in commands)
        {
            Include(command.EndX, command.EndY);

            if (command.Type != OutlineCommandType.CubicTo)
                continue;

            Include(command.Ctrl1X, command.Ctrl1Y);
            Include(command.Ctrl2X, command.Ctrl2Y);
        }

        return (xMin, yMin, xMax, yMax);

        void Include(float x, float y)
        {
            if (x < xMin) xMin = x;
            if (y < yMin) yMin = y;
            if (x > xMax) xMax = x;
            if (y > yMax) yMax = y;
        }
    }

    /// <summary>
    /// Walks one closed contour, emitting its commands. Contours with fewer than two points
    /// enclose no area and are skipped.
    /// </summary>
    private static void AppendContour(List<OutlineCommand> commands, GlyphPoint[] points)
    {
        int count = points.Length;
        if (count < 2)
            return;

        // A contour may legally begin on a control point. The real start is then the last
        // on-curve point of the contour, or — when that is a control point too — the implicit
        // on-curve midpoint between the two. Either way the start is never walked twice.
        float startX, startY;
        int first, walk;
        if (points[0].OnCurve)
        {
            (startX, startY) = (points[0].X, points[0].Y);
            first = 1;
            walk = count - 1;
        }
        else if (points[count - 1].OnCurve)
        {
            (startX, startY) = (points[count - 1].X, points[count - 1].Y);
            first = 0;
            walk = count - 1;
        }
        else
        {
            startX = (points[count - 1].X + points[0].X) * 0.5f;
            startY = (points[count - 1].Y + points[0].Y) * 0.5f;
            first = 0;
            walk = count;
        }

        commands.Add(OutlineCommand.Move(startX, startY));

        float currentX = startX;
        float currentY = startY;
        bool hasControl = false;
        float controlX = 0f;
        float controlY = 0f;

        for (int i = 0; i < walk; i++)
        {
            var point = points[(first + i) % count];

            if (point.OnCurve)
            {
                if (hasControl)
                {
                    commands.Add(Elevate(currentX, currentY, controlX, controlY, point.X, point.Y));
                    hasControl = false;
                }
                else
                {
                    commands.Add(OutlineCommand.Line(point.X, point.Y));
                }

                currentX = point.X;
                currentY = point.Y;
            }
            else if (hasControl)
            {
                // Two controls in a row: TrueType leaves the on-curve point between them
                // implicit at their midpoint.
                float midX = (controlX + point.X) * 0.5f;
                float midY = (controlY + point.Y) * 0.5f;
                commands.Add(Elevate(currentX, currentY, controlX, controlY, midX, midY));

                currentX = midX;
                currentY = midY;
                controlX = point.X;
                controlY = point.Y;
            }
            else
            {
                controlX = point.X;
                controlY = point.Y;
                hasControl = true;
            }
        }

        // A control point left pending at the end curves back to where the contour started.
        if (hasControl)
            commands.Add(Elevate(currentX, currentY, controlX, controlY, startX, startY));

        commands.Add(OutlineCommand.CloseAt(startX, startY));
    }

    /// <summary>
    /// Elevates the quadratic (p0, control, p1) to the equivalent cubic. Written as
    /// <c>(p + 2c) / 3</c> rather than <c>p + 2/3 (c - p)</c> — algebraically identical, but
    /// it avoids rounding the 2/3 factor, so the elevation is exact for whole-unit inputs.
    /// </summary>
    private static OutlineCommand Elevate(
        float p0X, float p0Y,
        float controlX, float controlY,
        float p1X, float p1Y)
    {
        // Divide rather than multiply by a 1/3 reciprocal: division is correctly rounded, so
        // whole-unit font coordinates land on exact results.
        float c1X = (p0X + (2f * controlX)) / 3f;
        float c1Y = (p0Y + (2f * controlY)) / 3f;
        float c2X = (p1X + (2f * controlX)) / 3f;
        float c2Y = (p1Y + (2f * controlY)) / 3f;

        return OutlineCommand.Cubic(c1X, c1Y, c2X, c2Y, p1X, p1Y);
    }
}
