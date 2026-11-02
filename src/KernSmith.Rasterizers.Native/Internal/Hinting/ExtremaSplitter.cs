using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// Splits every cubic of an outline at its interior x and y extrema, so each extremum of a
/// bowl or arch is a node whose tangent is exactly axis-aligned. The edge finder
/// (<see cref="GridFitter.FindEdges"/>) only sees an extremum when a node sits on it within a
/// few degrees of flat; a quadratic font's bowl converted to cubics, a CFF eye drawn through
/// its diagonals, or any curve whose on-curve point is off the true extremum would otherwise
/// have no edge there, and the <c>Normal</c> fitter could neither snap it to its zone nor pair
/// it into a stroke. Splitting changes no shape: the pieces draw the same curve.
/// </summary>
internal static class ExtremaSplitter
{
    /// <summary>Roots this close to a segment's end are its end, not an interior extremum.</summary>
    private const double EndMargin = 1e-3;

    /// <summary>The outline with every interior extremum on a node; the same instance when there was nothing to split.</summary>
    public static GlyphOutline Split(GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var source = outline.Commands;
        List<OutlineCommand>? result = null;
        Span<double> roots = stackalloc double[4];
        float lastX = 0f, lastY = 0f;
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c.Type == OutlineCommandType.CubicTo)
            {
                int count = InteriorExtrema(lastX, c.Ctrl1X, c.Ctrl2X, c.EndX, roots, 0);
                count = InteriorExtrema(lastY, c.Ctrl1Y, c.Ctrl2Y, c.EndY, roots, count);
                if (count > 0)
                {
                    if (result is null)
                    {
                        result = new List<OutlineCommand>(source.Length + 8);
                        for (int k = 0; k < i; k++)
                            result.Add(source[k]);
                    }

                    SplitAt(lastX, lastY, c, roots[..count], result);
                    lastX = c.EndX;
                    lastY = c.EndY;
                    continue;
                }
            }

            result?.Add(c);
            if (c.Type != OutlineCommandType.Close)
            {
                lastX = c.EndX;
                lastY = c.EndY;
            }
        }

        return result is null ? outline : GlyphOutline.FromCommands([.. result]);
    }

    /// <summary>
    /// Appends the parameters in (0, 1) where the cubic through p0..p3 has zero derivative along
    /// one axis, merged with the ones already there, and returns the new count. The caller's
    /// buffer holds at most four.
    /// </summary>
    private static int InteriorExtrema(double p0, double p1, double p2, double p3, Span<double> roots, int count)
    {
        // Derivative: 3[(−p0+3p1−3p2+p3)t² + 2(p0−2p1+p2)t + (p1−p0)].
        double a = -p0 + (3 * p1) - (3 * p2) + p3;
        double b = 2 * (p0 - (2 * p1) + p2);
        double c = p1 - p0;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) >= 1e-12)
                count = Take(-c / b, roots, count);
            return count;
        }

        double discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0)
            return count;
        double root = Math.Sqrt(discriminant);
        count = Take((-b + root) / (2 * a), roots, count);
        return Take((-b - root) / (2 * a), roots, count);
    }

    private static int Take(double t, Span<double> roots, int count)
    {
        if (!(t > EndMargin && t < 1 - EndMargin) || count >= roots.Length)
            return count;
        for (int i = 0; i < count; i++)
        {
            if (Math.Abs(roots[i] - t) < EndMargin)
                return count;
        }

        roots[count] = t;
        return count + 1;
    }

    /// <summary>Appends the cubic split at the given parameters (any order), de Casteljau piece by piece.</summary>
    private static void SplitAt(float startX, float startY, OutlineCommand c, Span<double> roots, List<OutlineCommand> into)
    {
        roots.Sort();
        double x0 = startX, y0 = startY, x1 = c.Ctrl1X, y1 = c.Ctrl1Y, x2 = c.Ctrl2X, y2 = c.Ctrl2Y, x3 = c.EndX, y3 = c.EndY;
        double consumed = 0;
        foreach (double root in roots)
        {
            double t = (root - consumed) / (1 - consumed);
            double mt = 1 - t;
            double ax = (mt * x0) + (t * x1), ay = (mt * y0) + (t * y1);
            double bx = (mt * x1) + (t * x2), by = (mt * y1) + (t * y2);
            double cx = (mt * x2) + (t * x3), cy = (mt * y2) + (t * y3);
            double dx = (mt * ax) + (t * bx), dy = (mt * ay) + (t * by);
            double ex = (mt * bx) + (t * cx), ey = (mt * by) + (t * cy);
            double px = (mt * dx) + (t * ex), py = (mt * dy) + (t * ey);
            into.Add(OutlineCommand.Cubic((float)ax, (float)ay, (float)dx, (float)dy, (float)px, (float)py));
            x0 = px;
            y0 = py;
            x1 = ex;
            y1 = ey;
            x2 = cx;
            y2 = cy;
            consumed = root;
        }

        // The last piece ends exactly on the original endpoint, so a contour closing on its
        // start still does.
        into.Add(OutlineCommand.Cubic((float)x1, (float)y1, (float)x2, (float)y2, c.EndX, c.EndY));
    }
}
