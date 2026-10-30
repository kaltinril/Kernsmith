namespace KernSmith.Rasterizers.Native.Internal.Distance;

/// <summary>
/// Single-channel signed distance fields: at every texel centre, the Euclidean distance to the
/// nearest point of the filled region's boundary, positive inside, clamped to a range.
/// </summary>
internal static class SignedDistanceField
{
    /// <summary>
    /// Flattening tolerance, in pixels, for a field encoded over <paramref name="spread"/>
    /// pixels: a quarter of one 8-bit code value (spread/512 px), kept within [1/1024, 1/32] px.
    /// Distances to the polyline are within this of the distances to the true curves.
    /// </summary>
    public static float ToleranceFor(float spread) => Math.Clamp(spread / 512f, 1f / 1024f, 1f / 32f);

    /// <summary>
    /// Computes the field. Only boundary pieces within <paramref name="range"/> of a texel are
    /// visited — each piece updates the texels of its own capsule — so the cost scales with the
    /// outline's length times the range, not texels times segments.
    /// </summary>
    /// <returns>Row-major signed distances in pixels, clamped to ±<paramref name="range"/>.</returns>
    public static float[] Compute(DistanceShape shape, int width, int height, float range)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var nearest = new float[width * height];
        Array.Fill(nearest, range * range);

        foreach (var segment in shape.Boundary)
            Splat(segment, nearest, width, height, range);

        var field = new float[width * height];
        var inside = new bool[width];
        var scratch = new List<(double X, int Direction)>();
        for (int y = 0; y < height; y++)
        {
            shape.Winding.ScanRow(y, inside, scratch);
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                float distance = MathF.Sqrt(nearest[i]);
                field[i] = inside[x] ? distance : -distance;
            }
        }

        return field;
    }

    /// <summary>
    /// Lowers <paramref name="nearest"/> (squared distances) for every texel whose centre lies
    /// within <paramref name="range"/> of the segment.
    /// </summary>
    private static void Splat(BoundarySegment s, float[] nearest, int width, int height, float range)
    {
        float dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
        float lengthSquared = (dx * dx) + (dy * dy);
        if (!(lengthSquared > 0))
            return;

        float rangeSquared = range * range;
        float minX = Math.Min(s.X0, s.X1) - range, maxX = Math.Max(s.X0, s.X1) + range;
        int firstRow = Math.Max(0, (int)MathF.Ceiling(Math.Min(s.Y0, s.Y1) - range - 0.5f));
        int lastRow = Math.Min(height - 1, (int)MathF.Floor(Math.Max(s.Y0, s.Y1) + range - 0.5f));

        // Within `range` of the segment means within `range` of its line, a band whose
        // horizontal half-width on any row is range·length/|dy|.
        float halfBand = Math.Abs(dy) > 0 ? range * MathF.Sqrt(lengthSquared) / Math.Abs(dy) : float.PositiveInfinity;

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
            int rowStart = row * width;
            float ay = cy - s.Y0;

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                float ax = column + 0.5f - s.X0;
                float t = ((ax * dx) + (ay * dy)) / lengthSquared;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                float ex = ax - (t * dx), ey = ay - (t * dy);
                float d = (ex * ex) + (ey * ey);
                if (d < nearest[rowStart + column] && d < rangeSquared)
                    nearest[rowStart + column] = d;
            }
        }
    }

    /// <summary>
    /// The 8-bit encoding FreeType's SDF renderer uses: 128 ± ⌊|d|·128/spread⌋, inside bright,
    /// clamped to 0..255 (so +spread saturates at 255 and −spread at 0).
    /// </summary>
    public static byte Encode(float distance, float spread)
    {
        int magnitude = (int)(Math.Abs(distance) * 128f / spread);
        return distance >= 0
            ? (byte)Math.Min(255, 128 + magnitude)
            : (byte)Math.Max(0, 128 - magnitude);
    }
}
