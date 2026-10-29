namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// Infers deltas for the points of a contour that a <c>gvar</c> tuple does not reference, per
/// "Inferred deltas for un-referenced point numbers" in the 'gvar' chapter. Unlike the TrueType
/// IUP instruction this works from the glyph's default point positions and the tuple's unscaled
/// deltas, one axis at a time.
/// </summary>
internal static class InferredDeltas
{
    /// <summary>
    /// Fills in the untouched entries of <paramref name="deltas"/> for points
    /// <paramref name="start"/>..<paramref name="end"/> (inclusive) along one axis. A contour with
    /// no touched point is left as it is.
    /// </summary>
    /// <param name="coordinates">Default coordinates along this axis, indexed by point number.</param>
    /// <param name="deltas">Deltas along this axis; touched entries are read, untouched ones written.</param>
    /// <param name="touched">Which points the tuple references.</param>
    /// <param name="start">First point of the contour.</param>
    /// <param name="end">Last point of the contour.</param>
    public static void InferContour(
        ReadOnlySpan<int> coordinates, Span<float> deltas, ReadOnlySpan<bool> touched, int start, int end)
    {
        int firstTouched = -1;
        for (int i = start; i <= end; i++)
        {
            if (touched[i])
            {
                firstTouched = i;
                break;
            }
        }

        if (firstTouched < 0)
            return;

        // Walk each run of untouched points between consecutive touched ones, wrapping from the
        // last touched point back round to the first.
        int previous = firstTouched;
        int count = end - start + 1;
        for (int step = 1; step <= count; step++)
        {
            int index = start + ((firstTouched - start + step) % count);
            if (!touched[index])
                continue;

            FillGap(coordinates, deltas, start, count, previous, index);
            previous = index;
        }
    }

    /// <summary>Infers every point strictly between two touched points, in contour order.</summary>
    private static void FillGap(
        ReadOnlySpan<int> coordinates, Span<float> deltas, int start, int count, int before, int after)
    {
        int beforeCoord = coordinates[before];
        int afterCoord = coordinates[after];
        float beforeDelta = deltas[before];
        float afterDelta = deltas[after];

        for (int index = Next(before); index != after; index = Next(index))
            deltas[index] = Infer(coordinates[index], beforeCoord, beforeDelta, afterCoord, afterDelta);

        int Next(int i) => start + ((i - start + 1) % count);
    }

    private static float Infer(int target, int beforeCoord, float beforeDelta, int afterCoord, float afterDelta)
    {
        if (beforeCoord == afterCoord)
            return beforeDelta.Equals(afterDelta) ? beforeDelta : 0f;

        // Outside the neighbours' span the nearer end's delta applies unchanged.
        if (target <= Math.Min(beforeCoord, afterCoord))
            return beforeCoord < afterCoord ? beforeDelta : afterDelta;
        if (target >= Math.Max(beforeCoord, afterCoord))
            return beforeCoord > afterCoord ? beforeDelta : afterDelta;

        float proportion = (float)(target - beforeCoord) / (afterCoord - beforeCoord);
        return ((1f - proportion) * beforeDelta) + (proportion * afterDelta);
    }
}
