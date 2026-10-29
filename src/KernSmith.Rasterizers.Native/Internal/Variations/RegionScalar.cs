namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The per-axis "tent" scalar of a variation region, per the OpenType overview's "Algorithm for
/// interpolation of instance values". A region's overall scalar is the product over its axes.
/// </summary>
internal static class RegionScalar
{
    /// <summary>
    /// How strongly a region applies along one axis at <paramref name="coordinate"/>: 1 at the
    /// peak, falling linearly to 0 at start and end.
    /// </summary>
    /// <returns>A value in [0, 1]. Malformed axis ranges, and a zero peak, return 1 so the axis
    /// does not participate.</returns>
    public static float ForAxis(float start, float peak, float end, float coordinate)
    {
        if (start > peak || peak > end)
            return 1f;
        if (start < 0f && end > 0f && !peak.Equals(0f))
            return 1f;
        if (peak.Equals(0f))
            return 1f;
        if (coordinate < start || coordinate > end)
            return 0f;
        if (coordinate.Equals(peak))
            return 1f;

        return coordinate < peak
            ? (coordinate - start) / (peak - start)
            : (end - coordinate) / (end - peak);
    }

    /// <summary>
    /// The overall scalar of a region given as start/peak/end tuples, short-circuiting to 0 once
    /// any axis rules the region out.
    /// </summary>
    public static float ForRegion(
        ReadOnlySpan<float> start, ReadOnlySpan<float> peak, ReadOnlySpan<float> end, ReadOnlySpan<float> coordinates)
    {
        float scalar = 1f;
        for (int axis = 0; axis < peak.Length; axis++)
        {
            float coordinate = axis < coordinates.Length ? coordinates[axis] : 0f;
            scalar *= ForAxis(start[axis], peak[axis], end[axis], coordinate);
            if (scalar.Equals(0f))
                return 0f;
        }

        return scalar;
    }
}
