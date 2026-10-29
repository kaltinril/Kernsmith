namespace KernSmith.Rasterizers.Native.Internal.Distance;

/// <summary>
/// Multi-channel signed distance fields (MSDF): per texel, one signed distance per color
/// channel, each measured to the nearest edge carrying that channel. <c>median(R, G, B)</c>
/// reconstructs the shape with sharp corners where a single-channel field rounds them.
/// </summary>
/// <remarks>
/// Per channel, the nearest edge is chosen by true distance, ties (edges meeting at a shared
/// corner) going to the edge whose direction is more perpendicular to the texel. The stored
/// value is that edge's signed <em>pseudo-distance</em>: beyond the edge's own endpoints the
/// distance is measured to the edge's tangent line, extending the edge straight on, so the two
/// half-planes meeting at a corner stay linear across it. Signs come from edge orientation; a
/// texel whose median disagrees with the non-zero inside test has all its channels negated,
/// and texels whose channels clash with a neighbour are flattened to their median
/// (<see cref="CorrectClashes"/>).
///
/// Only edges within <c>range</c> of a texel are considered; a channel with none is set to
/// ±range by the inside test. The median ignores it whenever the nearest edge carries two or
/// three channels, which coloring guarantees.
/// </remarks>
internal static class MultiChannelDistanceField
{
    /// <summary>Ties within this many pixels count as the same distance.</summary>
    private const float TieTolerance = 1e-4f;

    /// <summary>
    /// A channel may change by at most one pixel of distance per pixel moved; more than this
    /// (with slack for float noise) between neighbours is a discontinuity.
    /// </summary>
    private const float ClashSlack = 1.001f;

    /// <summary>Computes the field: three floats (R, G, B) per texel, row-major, clamped to ±range.</summary>
    public static float[] Compute(DistanceShape shape, int width, int height, float range)
    {
        ArgumentNullException.ThrowIfNull(shape);

        int count = width * height * 3;
        var nearest = new float[count];
        var orthogonality = new float[count];
        var values = new float[count];
        Array.Fill(nearest, float.PositiveInfinity);

        foreach (var segment in shape.Boundary)
        {
            var edge = shape.Edges[segment.EdgeIndex];
            if (edge.Color != EdgeColor.None)
                Splat(segment, edge, shape.Polarity, nearest, orthogonality, values, width, height, range);
        }

        var inside = new bool[width];
        var scratch = new List<(double X, int Direction)>();
        for (int y = 0; y < height; y++)
        {
            shape.Winding.ScanRow(y, inside, scratch);
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 3;
                float fill = inside[x] ? range : -range;
                for (int c = 0; c < 3; c++)
                {
                    if (float.IsPositiveInfinity(nearest[i + c]))
                        values[i + c] = fill;
                }

                // Orientation decides each channel's sign; the non-zero rule decides inside.
                float median = Median(values[i], values[i + 1], values[i + 2]);
                if (inside[x] ? median < 0 : median > 0)
                {
                    values[i] = -values[i];
                    values[i + 1] = -values[i + 1];
                    values[i + 2] = -values[i + 2];
                }

                for (int c = 0; c < 3; c++)
                    values[i + c] = Math.Clamp(values[i + c], -range, range);
            }
        }

        return values;
    }

    /// <summary>The median of three values.</summary>
    public static float Median(float a, float b, float c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

    /// <summary>
    /// Finds texels whose channels jump against a neighbour by more than a distance field can
    /// (one pixel of distance per pixel of travel, √2 diagonally) and, of each such pair, keeps
    /// the one nearer the edge; the other is flattened to its median, which removes the
    /// interpolation artifact without moving the reconstructed edge at any texel centre.
    /// </summary>
    /// <returns>The number of texels flattened.</returns>
    public static int CorrectClashes(float[] values, int width, int height)
    {
        var clashing = new List<int>();
        float orthogonal = ClashSlack, diagonal = ClashSlack * MathF.Sqrt(2);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int a = ((y * width) + x) * 3;
                if ((x > 0 && Clashes(values, a, a - 3, orthogonal))
                    || (x < width - 1 && Clashes(values, a, a + 3, orthogonal))
                    || (y > 0 && Clashes(values, a, a - (width * 3), orthogonal))
                    || (y < height - 1 && Clashes(values, a, a + (width * 3), orthogonal))
                    || (x > 0 && y > 0 && Clashes(values, a, a - (width * 3) - 3, diagonal))
                    || (x < width - 1 && y > 0 && Clashes(values, a, a - (width * 3) + 3, diagonal))
                    || (x > 0 && y < height - 1 && Clashes(values, a, a + (width * 3) - 3, diagonal))
                    || (x < width - 1 && y < height - 1 && Clashes(values, a, a + (width * 3) + 3, diagonal)))
                {
                    clashing.Add(a);
                }
            }
        }

        foreach (int a in clashing)
        {
            float median = Median(values[a], values[a + 1], values[a + 2]);
            values[a] = values[a + 1] = values[a + 2] = median;
        }

        return clashing.Count;
    }

    /// <summary>
    /// True when texel a should be flattened because of neighbour b: their most-changed channel
    /// jumps by at least <paramref name="threshold"/>, b has not been flattened already, and a
    /// is the farther of the two from the edge in their least-changed channel.
    /// </summary>
    private static bool Clashes(float[] v, int a, int b, float threshold)
    {
        // Order the channels by how much they change between a and b, most first.
        int c0 = 0, c1 = 1, c2 = 2;
        if (Math.Abs(v[b + c0] - v[a + c0]) < Math.Abs(v[b + c1] - v[a + c1])) (c0, c1) = (c1, c0);
        if (Math.Abs(v[b + c1] - v[a + c1]) < Math.Abs(v[b + c2] - v[a + c2]))
        {
            (c1, c2) = (c2, c1);
            if (Math.Abs(v[b + c0] - v[a + c0]) < Math.Abs(v[b + c1] - v[a + c1])) (c0, c1) = (c1, c0);
        }

        bool bFlat = v[b].Equals(v[b + 1]) && v[b].Equals(v[b + 2]);
        return Math.Abs(v[b + c0] - v[a + c0]) >= threshold
            && !bFlat
            && Math.Abs(v[a + c2]) >= Math.Abs(v[b + c2]);
    }

    private static void Splat(
        BoundarySegment s, DistanceEdge edge, int polarity,
        float[] nearest, float[] orthogonality, float[] values,
        int width, int height, float range)
    {
        float dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
        float lengthSquared = (dx * dx) + (dy * dy);
        if (!(lengthSquared > 0))
            return;

        float length = MathF.Sqrt(lengthSquared);
        float ux = dx / length, uy = dy / length;
        int mask = (int)edge.Color;

        float minX = Math.Min(s.X0, s.X1) - range, maxX = Math.Max(s.X0, s.X1) + range;
        int firstRow = Math.Max(0, (int)MathF.Ceiling(Math.Min(s.Y0, s.Y1) - range - 0.5f));
        int lastRow = Math.Min(height - 1, (int)MathF.Floor(Math.Max(s.Y0, s.Y1) + range - 0.5f));
        float halfBand = Math.Abs(dy) > 0 ? range * length / Math.Abs(dy) : float.PositiveInfinity;

        for (int row = firstRow; row <= lastRow; row++)
        {
            float cy = row + 0.5f;
            float lo = minX, hi = maxX;
            if (!float.IsPositiveInfinity(halfBand))
            {
                float lineX = s.X0 + ((cy - s.Y0) * dx / dy);
                lo = Math.Max(lo, lineX - halfBand);
                hi = Math.Min(hi, lineX + halfBand);
            }

            int firstColumn = Math.Max(0, (int)MathF.Ceiling(lo - 0.5f));
            int lastColumn = Math.Min(width - 1, (int)MathF.Floor(hi - 0.5f));
            float ay = cy - s.Y0;

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                float ax = column + 0.5f - s.X0;
                float t = ((ax * dx) + (ay * dy)) / lengthSquared;
                float tc = t < 0 ? 0 : t > 1 ? 1 : t;
                float ex = ax - (tc * dx), ey = ay - (tc * dy);
                float distance = MathF.Sqrt((ex * ex) + (ey * ey));
                if (distance > range)
                    continue;

                // 0 for a perpendicular foot inside the segment; otherwise how far the texel
                // lies along the segment's line past the endpoint, as a cosine.
                float ortho = (t > 0 && t < 1) || distance <= 0
                    ? 0
                    : Math.Abs(((ux * ex) + (uy * ey)) / distance);

                int i = ((row * width) + column) * 3;
                bool computed = false;
                float value = 0;
                for (int c = 0; c < 3; c++)
                {
                    if ((mask & (1 << c)) == 0)
                        continue;

                    float best = nearest[i + c];
                    if (distance < best - TieTolerance
                        || (distance <= best + TieTolerance && ortho < orthogonality[i + c]))
                    {
                        if (!computed)
                        {
                            value = SignedPseudoDistance(s, edge, polarity, ax, ay, t, ux, uy, distance);
                            computed = true;
                        }

                        nearest[i + c] = distance;
                        orthogonality[i + c] = ortho;
                        values[i + c] = value;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The signed distance from the texel (at <paramref name="ax"/>, <paramref name="ay"/>
    /// relative to the segment start) to the segment — perpendicular inside it, to the nearest
    /// endpoint outside it — replaced by the distance to the edge's tangent line when the texel
    /// lies beyond the edge's own start or end and that line is no farther.
    /// </summary>
    private static float SignedPseudoDistance(
        BoundarySegment s, DistanceEdge edge, int polarity,
        float ax, float ay, float t, float ux, float uy, float distance)
    {
        float cross = (ux * ay) - (uy * ax);
        if (t > 0 && t < 1)
            return polarity * cross;

        if (t <= 0 && s.OpensEdge)
        {
            float along = (edge.StartDirX * ax) + (edge.StartDirY * ay);
            if (along < 0)
            {
                float pseudo = (edge.StartDirX * ay) - (edge.StartDirY * ax);
                if (Math.Abs(pseudo) <= distance)
                    return polarity * pseudo;
            }
        }
        else if (t >= 1 && s.ClosesEdge)
        {
            float bx = ax - (s.X1 - s.X0), by = ay - (s.Y1 - s.Y0);
            float along = (edge.EndDirX * bx) + (edge.EndDirY * by);
            if (along > 0)
            {
                float pseudo = (edge.EndDirX * by) - (edge.EndDirY * bx);
                if (Math.Abs(pseudo) <= distance)
                    return polarity * pseudo;
            }
        }

        return (cross < 0 ? -polarity : polarity) * distance;
    }
}
