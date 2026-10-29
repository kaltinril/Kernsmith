using System.Buffers;
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
    /// <param name="offsetX">Added to every X coordinate before curves are elevated, so a
    /// whole-unit offset keeps the elevation exact.</param>
    /// <returns>The normalized outline; <see cref="GlyphOutline.Empty"/> for glyphs with nothing to draw.</returns>
    public static GlyphOutline Extract(ParsedGlyph glyph, float offsetX = 0f)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.IsEmpty)
            return GlyphOutline.Empty;

        var commands = ArrayPool<OutlineCommand>.Shared.Rent(MaxCommands(glyph.PointCount, glyph.Contours.Length));
        try
        {
            int count = 0;
            foreach (var contour in glyph.Contours)
                count = AppendContour(commands, count, contour.Points, offsetX);

            return ToOutline(commands.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<OutlineCommand>.Shared.Return(commands);
        }
    }

    /// <summary>
    /// The most commands <see cref="AppendContour"/> can write for <paramref name="points"/>
    /// points across <paramref name="contours"/> contours: each walked point writes at most one,
    /// plus a move, a pending closing curve and a close per contour.
    /// </summary>
    public static int MaxCommands(int points, int contours) => points + (3 * contours);

    /// <summary>The outline over a copy of <paramref name="commands"/>; <see cref="GlyphOutline.Empty"/> when there are none.</summary>
    public static GlyphOutline ToOutline(ReadOnlySpan<OutlineCommand> commands) =>
        commands.IsEmpty ? GlyphOutline.Empty : GlyphOutline.FromCommands(commands.ToArray());

    /// <summary>
    /// Walks one closed contour, writing its commands into <paramref name="commands"/> from
    /// index <paramref name="count"/>. Contours with fewer than two points enclose no area and
    /// are skipped.
    /// </summary>
    /// <param name="commands">Destination, with room for <see cref="MaxCommands"/> of this contour.</param>
    /// <param name="count">Commands already written.</param>
    /// <param name="points">The contour's points.</param>
    /// <param name="offsetX">Added to every X before curves are elevated; 0 leaves X untouched.</param>
    /// <returns>Commands written so far, this contour's included.</returns>
    public static int AppendContour(Span<OutlineCommand> commands, int count, ReadOnlySpan<GlyphPoint> points, float offsetX)
    {
        int pointCount = points.Length;
        if (pointCount < 2)
            return count;

        // Exactly zero skips the addition, so X keeps its bits (a -0 stays -0).
        bool shift = !offsetX.Equals(0f);

        // A contour may legally begin on a control point. The real start is then the last
        // on-curve point of the contour, or — when that is a control point too — the implicit
        // on-curve midpoint between the two. Either way the start is never walked twice.
        var firstPoint = points[0];
        var lastPoint = points[pointCount - 1];
        float startX, startY;
        int first, walk;
        if (firstPoint.OnCurve)
        {
            (startX, startY) = (X(firstPoint), firstPoint.Y);
            first = 1;
            walk = pointCount - 1;
        }
        else if (lastPoint.OnCurve)
        {
            (startX, startY) = (X(lastPoint), lastPoint.Y);
            first = 0;
            walk = pointCount - 1;
        }
        else
        {
            startX = (X(lastPoint) + X(firstPoint)) * 0.5f;
            startY = (lastPoint.Y + firstPoint.Y) * 0.5f;
            first = 0;
            walk = pointCount;
        }

        commands[count++] = OutlineCommand.Move(startX, startY);

        float currentX = startX;
        float currentY = startY;
        bool hasControl = false;
        float controlX = 0f;
        float controlY = 0f;

        for (int i = 0; i < walk; i++)
        {
            var point = points[(first + i) % pointCount];
            float pointX = X(point);

            if (point.OnCurve)
            {
                if (hasControl)
                {
                    commands[count++] = Elevate(currentX, currentY, controlX, controlY, pointX, point.Y);
                    hasControl = false;
                }
                else
                {
                    commands[count++] = OutlineCommand.Line(pointX, point.Y);
                }

                currentX = pointX;
                currentY = point.Y;
            }
            else if (hasControl)
            {
                // Two controls in a row: TrueType leaves the on-curve point between them
                // implicit at their midpoint.
                float midX = (controlX + pointX) * 0.5f;
                float midY = (controlY + point.Y) * 0.5f;
                commands[count++] = Elevate(currentX, currentY, controlX, controlY, midX, midY);

                currentX = midX;
                currentY = midY;
                controlX = pointX;
                controlY = point.Y;
            }
            else
            {
                controlX = pointX;
                controlY = point.Y;
                hasControl = true;
            }
        }

        // A control point left pending at the end curves back to where the contour started.
        if (hasControl)
            commands[count++] = Elevate(currentX, currentY, controlX, controlY, startX, startY);

        commands[count++] = OutlineCommand.CloseAt(startX, startY);
        return count;

        float X(GlyphPoint point) => shift ? point.X + offsetX : point.X;
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
