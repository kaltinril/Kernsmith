// ─────────────────────────────────────────────────────────────────────────────
// Embolden is a port of FreeType's FT_Outline_EmboldenXY algorithm.
//
// Portions of this software are copyright © 2023 The FreeType Project
// (www.freetype.org). All rights reserved.
//
// Used under the FreeType License (FTL). See THIRD-PARTY-NOTICES.md at the repo
// root for the full license text.
// ─────────────────────────────────────────────────────────────────────────────

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Synthetic-bold outline expansion, ported from FreeType's <c>FT_Outline_EmboldenXY</c> so
/// Native's bold matches the FreeType backend's in weight and placement.
/// </summary>
/// <remarks>
/// Works on the control polygon — on-curve points and cubic control points alike — exactly as
/// FreeType works on its quadratic one, which offsets each Bézier by moving its controls
/// (Tiller–Hanson). Every vertex moves by half the strength along the miter bisector of its two
/// edges, pointing out of the ink; because outer contours and counters wind in opposite
/// directions, one global orientation grows strokes and shrinks counters at the same time. The
/// whole outline then shifts up and right by half the strength, so the left and bottom edges stay
/// put and the ink grows by the full strength to the right and upwards.
///
/// INVARIANT (from FreeType): a turn sharper than ~160° gets no lateral shift, and an inward
/// (concave) corner's shift is capped at <c>shortestEdge / sin(turn)</c>. The first stops spikes
/// at acute apexes; the second stops short edges collapsing into each other at small sizes.
/// </remarks>
internal static class OutlineEmboldener
{
    /// <summary>FreeType's "shift only if turn is less than ~160°": cos(turn) &gt; -0xF000/0x10000.</summary>
    private const double MinTurnCosine = -0.9375;

    /// <summary>
    /// Expands an outline by <paramref name="strength"/> font units in total width and height.
    /// </summary>
    /// <returns>
    /// The emboldened outline; the input itself when it is empty; null when its contours enclose
    /// no signed area, so it has no orientation to expand against (FreeType rejects those too).
    /// </returns>
    public static GlyphOutline? Embolden(GlyphOutline outline, float strength)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return outline;

        var commands = outline.Commands;
        int pointCount = 0;
        foreach (var command in commands)
        {
            pointCount += command.Type switch
            {
                OutlineCommandType.CubicTo => 3,
                OutlineCommandType.Close => 0,
                _ => 1,
            };
        }

        // The control polygon, contour by contour. Close repeats the start point, so it adds no
        // vertex; the closing edge is the polygon's wrap-around.
        var xs = new double[pointCount];
        var ys = new double[pointCount];
        var contours = new List<(int First, int Last)>();
        int n = 0, contourFirst = 0;
        foreach (var command in commands)
        {
            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                    contourFirst = n;
                    Add(command.EndX, command.EndY);
                    break;
                case OutlineCommandType.LineTo:
                    Add(command.EndX, command.EndY);
                    break;
                case OutlineCommandType.CubicTo:
                    Add(command.Ctrl1X, command.Ctrl1Y);
                    Add(command.Ctrl2X, command.Ctrl2Y);
                    Add(command.EndX, command.EndY);
                    break;
                case OutlineCommandType.Close:
                    contours.Add((contourFirst, n - 1));
                    break;
            }
        }

        // FT_Outline_Get_Orientation: the sign of the total signed area. Clockwise (Y up), the
        // TrueType convention for outer contours, sums negative.
        double area = 0;
        foreach (var (first, last) in contours)
        {
            double prevX = xs[last], prevY = ys[last];
            for (int i = first; i <= last; i++)
            {
                area += (ys[i] - prevY) * (xs[i] + prevX);
                prevX = xs[i];
                prevY = ys[i];
            }
        }

        if (area.Equals(0d))
            return null;

        bool clockwise = area < 0;
        double half = strength / 2.0;
        var movedX = (double[])xs.Clone();
        var movedY = (double[])ys.Clone();
        foreach (var (first, last) in contours)
            EmboldenContour(xs, ys, movedX, movedY, first, last, half, clockwise);

        var result = new OutlineCommand[commands.Length];
        int p = 0, start = 0;
        for (int c = 0; c < commands.Length; c++)
        {
            var command = commands[c];
            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                    start = p;
                    result[c] = OutlineCommand.Move(X(p), Y(p));
                    p++;
                    break;
                case OutlineCommandType.LineTo:
                    result[c] = OutlineCommand.Line(X(p), Y(p));
                    p++;
                    break;
                case OutlineCommandType.CubicTo:
                    result[c] = OutlineCommand.Cubic(X(p), Y(p), X(p + 1), Y(p + 1), X(p + 2), Y(p + 2));
                    p += 3;
                    break;
                default:
                    result[c] = OutlineCommand.CloseAt(X(start), Y(start));
                    break;
            }
        }

        return GlyphOutline.FromCommands(result);

        void Add(float x, float y)
        {
            xs[n] = x;
            ys[n] = y;
            n++;
        }

        float X(int i) => (float)movedX[i];
        float Y(int i) => (float)movedY[i];
    }

    /// <summary>
    /// One contour of <c>FT_Outline_EmboldenXY</c>. Reads the original points and writes the
    /// moved ones. Coincident points share their neighbour's shift, so a zero-length edge
    /// never produces a direction.
    /// </summary>
    private static void EmboldenContour(
        double[] xs, double[] ys, double[] movedX, double[] movedY,
        int first, int last, double half, bool clockwise)
    {
        double inX = 0, inY = 0, lIn = 0;
        double anchorX = 0, anchorY = 0, lAnchor = 0;

        // j walks the points; i advances only when points are moved; k marks the first moved
        // point, where the walk stops after wrapping around.
        int i = last, j = first, k = -1;
        while (j != i && i != k)
        {
            double outX, outY, lOut;
            if (j != k)
            {
                outX = xs[j] - xs[i];
                outY = ys[j] - ys[i];
                lOut = Math.Sqrt((outX * outX) + (outY * outY));
                if (lOut.Equals(0d))
                {
                    j = j < last ? j + 1 : first;
                    continue;
                }

                outX /= lOut;
                outY /= lOut;
            }
            else
            {
                outX = anchorX;
                outY = anchorY;
                lOut = lAnchor;
            }

            if (!lIn.Equals(0d))
            {
                if (k < 0)
                {
                    k = i;
                    anchorX = inX;
                    anchorY = inY;
                    lAnchor = lIn;
                }

                double d = (inX * outX) + (inY * outY);
                double shiftX = 0, shiftY = 0;
                if (d > MinTurnCosine)
                {
                    d += 1;

                    // The lateral bisector, pointing out of the ink for this orientation.
                    shiftX = inY + outY;
                    shiftY = inX + outX;
                    if (clockwise)
                        shiftX = -shiftX;
                    else
                        shiftY = -shiftY;

                    // Positive for an inward (concave) turn; caps the shift so short edges
                    // either side of it cannot cross.
                    double q = (outX * inY) - (outY * inX);
                    if (clockwise)
                        q = -q;

                    double l = Math.Min(lIn, lOut);
                    double scale = half * q <= l * d ? half / d : l / q;
                    shiftX *= scale;
                    shiftY *= scale;
                }

                for (; i != j; i = i < last ? i + 1 : first)
                {
                    movedX[i] = xs[i] + half + shiftX;
                    movedY[i] = ys[i] + half + shiftY;
                }
            }
            else
            {
                i = j;
            }

            inX = outX;
            inY = outY;
            lIn = lOut;
            j = j < last ? j + 1 : first;
        }
    }
}
