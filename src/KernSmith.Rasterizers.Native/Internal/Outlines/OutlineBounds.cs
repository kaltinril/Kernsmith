namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>An axis-aligned bounding box in font design units (Y up).</summary>
internal readonly record struct OutlineBounds(float XMin, float YMin, float XMax, float YMax)
{
    /// <summary>
    /// The tight bounds of the ink an outline draws: every on-curve point plus each cubic's
    /// interior extrema. Unlike <see cref="GlyphOutline"/>'s control hull, a control point
    /// that pulls a curve outward without the curve reaching it does not widen these.
    /// </summary>
    /// <returns>The bounds; all zero for an empty outline.</returns>
    public static OutlineBounds Tight(GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return default;

        double xMin = double.MaxValue, yMin = double.MaxValue;
        double xMax = double.MinValue, yMax = double.MinValue;
        double currentX = 0, currentY = 0;

        foreach (var command in outline.Commands)
        {
            if (command.Type == OutlineCommandType.CubicTo)
            {
                CubicExtrema(currentX, command.Ctrl1X, command.Ctrl2X, command.EndX, ref xMin, ref xMax);
                CubicExtrema(currentY, command.Ctrl1Y, command.Ctrl2Y, command.EndY, ref yMin, ref yMax);
            }

            Include(command.EndX, ref xMin, ref xMax);
            Include(command.EndY, ref yMin, ref yMax);
            currentX = command.EndX;
            currentY = command.EndY;
        }

        return new OutlineBounds((float)xMin, (float)yMin, (float)xMax, (float)yMax);
    }

    /// <summary>
    /// Widens [min, max] by one axis of a cubic's interior extrema. Only runs the root solve
    /// when a control point sits outside the endpoints' span — otherwise the curve cannot leave
    /// it, and the endpoints alone already bound that axis.
    /// </summary>
    private static void CubicExtrema(double p0, double p1, double p2, double p3, ref double min, ref double max)
    {
        double lo = Math.Min(p0, p3), hi = Math.Max(p0, p3);
        if (p1 >= lo && p1 <= hi && p2 >= lo && p2 <= hi)
            return;

        // B'(t)/3 = a·t² + b·t + c.
        double a = -p0 + (3 * p1) - (3 * p2) + p3;
        double b = 2 * (p0 - (2 * p1) + p2);
        double c = p1 - p0;

        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) >= 1e-12)
                IncludeAt(-c / b, ref min, ref max);
            return;
        }

        double discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0)
            return;

        double root = Math.Sqrt(discriminant);
        IncludeAt((-b + root) / (2 * a), ref min, ref max);
        IncludeAt((-b - root) / (2 * a), ref min, ref max);

        void IncludeAt(double t, ref double min, ref double max)
        {
            if (!(t > 0 && t < 1))
                return;

            double mt = 1 - t;
            double value = (mt * mt * mt * p0) + (3 * mt * mt * t * p1) + (3 * mt * t * t * p2) + (t * t * t * p3);
            Include(value, ref min, ref max);
        }
    }

    private static void Include(double value, ref double min, ref double max)
    {
        if (value < min) min = value;
        if (value > max) max = value;
    }
}
