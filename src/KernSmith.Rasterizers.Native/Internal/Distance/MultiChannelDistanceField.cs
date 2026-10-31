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
/// texel whose median disagrees with the non-zero inside test has all its channels negated.
/// <see cref="CorrectArtifacts"/> then flattens, to their median, the texels behind any
/// interpolated median that contradicts the true distance field.
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
    /// How deep, in pixels, the true field must provably be at a probe before a median of the
    /// other sign there counts as an artifact. Measured on Roboto, Noto Sans and Source Serif
    /// ASCII at 16-48 px: 0.05 leaves no artifacts while larger margins miss some, and smaller
    /// ones only add flattening at near-misses.
    /// </summary>
    private const float ProbeMargin = 0.05f;

    /// <summary>
    /// A corner value at least this far from zero keeps every interpolated value of its channel
    /// on its side (see <see cref="ProbedField.CertainMedianSign"/>): far above the subnormal
    /// range, far below any distance that matters.
    /// </summary>
    private const float CertainMagnitude = 1e-30f;

    /// <summary>Probes per cell side: 0, ¼, ½, ¾ and 1 of a texel.</summary>
    private const int ProbeSteps = 5;

    private const int ProbesPerCell = ProbeSteps * ProbeSteps;

    /// <summary>
    /// Bound on correction passes. A repair changes readings in cells the scan has already
    /// passed, so the scan repeats until a pass flattens nothing.
    /// </summary>
    private const int MaxCorrectionPasses = 4;

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
    /// Removes interpolation artifacts — places where bilinear sampling of the three channels
    /// gives a median of the wrong sign between texels — by checking the field against the
    /// true single-channel signed distance <paramref name="trueDistance"/> on the same grid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every cell of four neighbouring texels is probed on a quarter-texel lattice. The true
    /// field is 1-Lipschitz, so at radius r from a texel whose true distance is d it is at
    /// least |d| − r deep on the same side; where that bound exceeds
    /// <see cref="ProbeMargin"/> the probe's correct side is known, and an interpolated median
    /// on the other side is an artifact. Probes nearer the edge than the bound can prove are
    /// left alone, which is where a multi-channel field legitimately departs from the
    /// single-channel one (its sharp corners).
    /// </para>
    /// <para>
    /// An artifact is repaired by flattening texels of its cell to their own median (see
    /// <see cref="Repair"/>), which keeps each texel's median and so the reconstructed edge at
    /// every texel centre. Passes repeat until one flattens nothing.
    /// </para>
    /// </remarks>
    /// <returns>The number of texels flattened.</returns>
    public static int CorrectArtifacts(float[] values, float[] trueDistance, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(trueDistance);

        var field = new ProbedField(values, trueDistance, width, height);
        int flattened = 0;
        for (int pass = 0; pass < MaxCorrectionPasses; pass++)
        {
            int before = flattened;
            for (int y = 0; y + 1 < height; y++)
            {
                for (int x = 0; x + 1 < width; x++)
                {
                    if (field.CannotDisagree(x, y))
                        continue;

                    // Where every median's side is certain and the true field is on that side at
                    // all four corners, no probe can be known to lie on the other.
                    int side = field.CertainMedianSign(x, y);
                    if (side != 0 && field.CornersOnSide(x, y, side))
                        continue;

                    for (int probe = 0; probe < ProbesPerCell; probe++)
                    {
                        int sign = field.KnownSign(x, y, probe);
                        if (sign == 0)
                            continue;

                        bool agrees = side != 0 ? side == sign : field.Agrees(x, y, ProbeU(probe), ProbeV(probe), sign);
                        if (agrees)
                            continue;

                        int repaired = Repair(field, x, y, ProbeU(probe), ProbeV(probe), sign);
                        flattened += repaired;
                        if (repaired > 0)
                            side = field.CertainMedianSign(x, y);
                    }
                }
            }

            if (flattened == before)
                break;
        }

        return flattened;
    }

    /// <summary>Distance from each probe to each corner texel of its cell, four per probe.</summary>
    private static readonly float[] ProbeRadii = CreateProbeRadii();

    private static float[] CreateProbeRadii()
    {
        var radii = new float[ProbesPerCell * 4];
        for (int probe = 0; probe < ProbesPerCell; probe++)
        {
            for (int k = 0; k < 4; k++)
            {
                float du = ProbeU(probe) - (k & 1), dv = ProbeV(probe) - (k >> 1);
                radii[(probe * 4) + k] = MathF.Sqrt((du * du) + (dv * dv));
            }
        }

        return radii;
    }

    private static float ProbeU(int probe) => (probe % ProbeSteps) / (ProbeSteps - 1f);

    private static float ProbeV(int probe)
    {
        int row = probe / ProbeSteps; // whole rows: the integer division is intended
        return row / (ProbeSteps - 1f);
    }

    /// <summary>
    /// Flattens texels of the cell at (<paramref name="x"/>, <paramref name="y"/>) until the
    /// median at (u, v) reads <paramref name="sign"/>, each time picking the texel whose
    /// flattening moves that median furthest towards it.
    /// </summary>
    /// <remarks>
    /// The repair is then weighed over the probes of the surrounding three-by-three cells:
    /// +1 for each probe of known sign it puts right, −1 for each it puts wrong and for each
    /// probe of unknown sign whose reading it changes. A repair that scores below zero is
    /// undone. Near-miss probes beside a sub-texel hairline would otherwise strip the very
    /// channels that keep the hairline visible.
    /// </remarks>
    /// <returns>The number of texels flattened and kept flattened.</returns>
    private static int Repair(ProbedField field, int x, int y, float u, float v, int sign)
    {
        var values = field.Values;
        Span<float> saved = stackalloc float[12];
        Span<bool> readings = stackalloc bool[9 * ProbesPerCell];
        for (int k = 0; k < 4; k++)
            values.AsSpan(field.Texel(x, y, k), 3).CopyTo(saved.Slice(3 * k, 3));
        field.Neighbourhood(x, y, readings, record: true);

        int flattened = 0;
        while (!field.Agrees(x, y, u, v, sign))
        {
            int choice = -1;
            float best = float.NegativeInfinity;
            for (int k = 0; k < 4; k++)
            {
                int t = field.Texel(x, y, k);
                if (field.IsFlat(t))
                    continue;

                float r = values[t], g = values[t + 1], b = values[t + 2];
                Flatten(values, t);
                float score = sign * field.Median(x, y, u, v);
                values[t] = r;
                values[t + 1] = g;
                values[t + 2] = b;
                if (score > best)
                {
                    best = score;
                    choice = t;
                }
            }

            if (choice < 0)
                break;

            Flatten(values, choice);
            flattened++;
        }

        if (flattened > 0 && field.Neighbourhood(x, y, readings, record: false) < 0)
        {
            for (int k = 0; k < 4; k++)
                saved.Slice(3 * k, 3).CopyTo(values.AsSpan(field.Texel(x, y, k), 3));
            return 0;
        }

        return flattened;
    }

    /// <summary>Sets all three channels of the texel starting at <paramref name="t"/> to their median.</summary>
    private static void Flatten(float[] values, int t)
    {
        float median = Median(values[t], values[t + 1], values[t + 2]);
        values[t] = median;
        values[t + 1] = median;
        values[t + 2] = median;
    }

    /// <summary>
    /// A field under correction with its true distance, queried per cell: the cell at (x, y)
    /// has texels (x, y), (x + 1, y), (x, y + 1) and (x + 1, y + 1), and (u, v) in [0, 1]²
    /// runs across it between their centres.
    /// </summary>
    private readonly record struct ProbedField(float[] Values, float[] TrueDistance, int Width, int Height)
    {
        /// <summary>
        /// The side the true field is provably on at the probe — +1 inside, −1 outside — or 0
        /// when no texel of the cell is deep enough to prove it by <see cref="ProbeMargin"/>.
        /// </summary>
        public int KnownSign(int x, int y, int probe)
        {
            float best = ProbeMargin;
            int sign = 0;
            for (int k = 0; k < 4; k++)
            {
                float d = TrueDistance[((y + (k >> 1)) * Width) + x + (k & 1)];
                float depth = Math.Abs(d) - ProbeRadii[(probe * 4) + k];
                if (depth > best)
                {
                    best = depth;
                    sign = d > 0 ? 1 : -1;
                }
            }

            return sign;
        }

        /// <summary>
        /// True when no probe of the cell can need a repair: either all twelve channel values
        /// share one strict sign — every interpolated median then has that sign, and so does
        /// every texel's true distance, since texel medians follow the inside test — or every
        /// texel is already flat, leaving nothing to flatten.
        /// </summary>
        public bool CannotDisagree(int x, int y)
        {
            int t0 = 3 * ((y * Width) + x), t2 = t0 + (3 * Width);
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int i = 0; i < 6; i++)
            {
                min = Math.Min(min, Math.Min(Values[t0 + i], Values[t2 + i]));
                max = Math.Max(max, Math.Max(Values[t0 + i], Values[t2 + i]));
            }

            return min > 0 || max < 0
                || (IsFlat(t0) && IsFlat(t0 + 3) && IsFlat(t2) && IsFlat(t2 + 3));
        }

        /// <summary>
        /// The sign every probe's interpolated median has, when the corner values alone decide
        /// it — +1 when two channels are at least <see cref="CertainMagnitude"/> at all four
        /// corners, −1 when two are at most its negative — else 0.
        /// </summary>
        /// <remarks>
        /// Exact in float arithmetic, not just in the reals: every probe weight is a product of
        /// quarters, so it is exact, non-negative, and at least one of the four is 1/4 or more.
        /// Such a channel's interpolant is then a sum of same-signed products, one of them at
        /// least a quarter of <see cref="CertainMagnitude"/> (no underflow), and rounding never
        /// takes a sum of same-signed terms below the largest. Two channels of one sign fix the
        /// median's sign whatever the third holds — unless it is NaN, which Math.Min and Math.Max
        /// propagate, so a NaN anywhere answers 0.
        /// </remarks>
        public int CertainMedianSign(int x, int y)
        {
            int positive = 0, negative = 0;
            for (int c = 0; c < 3; c++)
            {
                bool allPositive = true, allNegative = true;
                for (int k = 0; k < 4; k++)
                {
                    float value = Values[Texel(x, y, k) + c];
                    if (float.IsNaN(value))
                        return 0;
                    allPositive &= value >= CertainMagnitude;
                    allNegative &= value <= -CertainMagnitude;
                }

                positive += allPositive ? 1 : 0;
                negative += allNegative ? 1 : 0;
            }

            return positive >= 2 ? 1 : negative >= 2 ? -1 : 0;
        }

        /// <summary>
        /// True when <see cref="KnownSign"/> can only answer 0 or <paramref name="side"/> in the
        /// cell: it takes a probe's sign from a corner's true distance, +1 exactly when that is
        /// above zero.
        /// </summary>
        public bool CornersOnSide(int x, int y, int side)
        {
            for (int k = 0; k < 4; k++)
            {
                float d = TrueDistance[((y + (k >> 1)) * Width) + x + (k & 1)];
                if ((d > 0 ? 1 : -1) != side)
                    return false;
            }

            return true;
        }

        // The NaN test keeps == semantics: float.Equals would call three NaN channels flat.
        /// <summary>True when the texel starting at <paramref name="t"/> has three equal channels.</summary>
        public bool IsFlat(int t) =>
            !float.IsNaN(Values[t]) && Values[t].Equals(Values[t + 1]) && Values[t + 1].Equals(Values[t + 2]);

        /// <summary>The median of the three channels, each interpolated bilinearly at (u, v).</summary>
        public float Median(int x, int y, float u, float v)
        {
            int t0 = 3 * ((y * Width) + x), t2 = t0 + (3 * Width);
            float w0 = (1 - u) * (1 - v), w1 = u * (1 - v), w2 = (1 - u) * v, w3 = u * v;
            float r = (w0 * Values[t0]) + (w1 * Values[t0 + 3]) + (w2 * Values[t2]) + (w3 * Values[t2 + 3]);
            float g = (w0 * Values[t0 + 1]) + (w1 * Values[t0 + 4]) + (w2 * Values[t2 + 1]) + (w3 * Values[t2 + 4]);
            float b = (w0 * Values[t0 + 2]) + (w1 * Values[t0 + 5]) + (w2 * Values[t2 + 2]) + (w3 * Values[t2 + 5]);
            return MultiChannelDistanceField.Median(r, g, b);
        }

        /// <summary>True when the interpolated median at (u, v) reads inside (&gt; 0) exactly when <paramref name="sign"/> is +1.</summary>
        public bool Agrees(int x, int y, float u, float v, int sign) => Median(x, y, u, v) > 0 == sign > 0;

        /// <summary>The index of the first channel of corner <paramref name="k"/> (0–3, row-major) of the cell.</summary>
        public int Texel(int x, int y, int k) => 3 * (((y + (k >> 1)) * Width) + x + (k & 1));

        /// <summary>
        /// Over the probes of the cell at (x, y) and its neighbours: with
        /// <paramref name="record"/>, stores each probe's inside/outside reading in
        /// <paramref name="readings"/> and returns 0; without, returns the net effect of the
        /// changes since — +1 for each probe of known sign now read right, −1 for each now
        /// read wrong and for each probe of unknown sign whose reading changed.
        /// </summary>
        public int Neighbourhood(int x, int y, Span<bool> readings, bool record)
        {
            int index = 0, net = 0;
            for (int cy = Math.Max(0, y - 1); cy <= Math.Min(Height - 2, y + 1); cy++)
            {
                for (int cx = Math.Max(0, x - 1); cx <= Math.Min(Width - 2, x + 1); cx++)
                {
                    for (int probe = 0; probe < ProbesPerCell; probe++, index++)
                    {
                        float u = ProbeU(probe), v = ProbeV(probe);
                        bool inside = Median(cx, cy, u, v) > 0;
                        if (record)
                        {
                            readings[index] = inside;
                        }
                        else if (inside != readings[index])
                        {
                            int known = KnownSign(cx, cy, probe);
                            net += known != 0 && inside == (known > 0) ? 1 : -1;
                        }
                    }
                }
            }

            return net;
        }
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
