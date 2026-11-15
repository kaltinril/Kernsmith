using System.Buffers;
using KernSmith.Atlas;

namespace KernSmith.Rasterizer;

/// <summary>
/// Squared distance from each pixel to the glyph, measured with the metric that matches an
/// <see cref="OutlineShape"/>: to the nearest opaque pixel (<see cref="ComputeSquared(byte[], int, int, OutlineShape)"/>),
/// or to the anti-aliased edge the glyph's coverage describes (<see cref="ComputeSquaredFromCoverage"/>).
/// </summary>
internal static class OutlineDistanceTransform
{
    private const int Far = int.MaxValue / 4;

    /// <summary>
    /// Returns squared distances (alpha &gt; 0 is inside, distance 0). Round is Euclidean;
    /// Square is Chebyshev; Plus is Manhattan. Squaring keeps callers comparing against <c>ow * ow</c>.
    /// </summary>
    public static float[] ComputeSquared(byte[] alphaData, int width, int height, OutlineShape shape)
    {
        var squared = new float[width * height];
        ComputeSquared(alphaData, width, height, shape, squared);
        return squared;
    }

    /// <summary>
    /// <see cref="ComputeSquared(byte[], int, int, OutlineShape)"/> into the first
    /// <c>width * height</c> entries of <paramref name="squared"/>.
    /// </summary>
    public static void ComputeSquared(byte[] alphaData, int width, int height, OutlineShape shape, float[] squared)
    {
        if (shape == OutlineShape.Round)
        {
            EuclideanDistanceTransform.Compute(alphaData, width, height, squared);
            return;
        }

        var diagonals = shape == OutlineShape.Square;
        var size = width * height;
        var grid = ArrayPool<int>.Shared.Rent(size);
        try
        {
            for (var i = 0; i < size; i++)
                grid[i] = alphaData[i] > 0 ? 0 : Far;

            // Two-pass chamfer with unit weights is exact for both Chebyshev (8 neighbours)
            // and Manhattan (4 neighbours).
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var best = grid[y * width + x];
                    if (x > 0) best = Math.Min(best, grid[y * width + x - 1] + 1);
                    if (y > 0)
                    {
                        best = Math.Min(best, grid[(y - 1) * width + x] + 1);
                        if (diagonals)
                        {
                            if (x > 0) best = Math.Min(best, grid[(y - 1) * width + x - 1] + 1);
                            if (x < width - 1) best = Math.Min(best, grid[(y - 1) * width + x + 1] + 1);
                        }
                    }
                    grid[y * width + x] = best;
                }
            }

            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = width - 1; x >= 0; x--)
                {
                    var best = grid[y * width + x];
                    if (x < width - 1) best = Math.Min(best, grid[y * width + x + 1] + 1);
                    if (y < height - 1)
                    {
                        best = Math.Min(best, grid[(y + 1) * width + x] + 1);
                        if (diagonals)
                        {
                            if (x < width - 1) best = Math.Min(best, grid[(y + 1) * width + x + 1] + 1);
                            if (x > 0) best = Math.Min(best, grid[(y + 1) * width + x - 1] + 1);
                        }
                    }
                    grid[y * width + x] = best;
                }
            }

            for (var i = 0; i < size; i++)
                squared[i] = grid[i] >= Far ? float.MaxValue / 2f : (float)grid[i] * grid[i];
        }
        finally
        {
            ArrayPool<int>.Shared.Return(grid);
        }
    }

    /// <summary>
    /// Squared distances from each empty pixel (coverage 0) to the glyph's anti-aliased edge,
    /// into the first <c>width * height</c> entries of <paramref name="squared"/>; a pixel with
    /// any coverage is in the glyph and gets 0.
    /// </summary>
    /// <remarks>
    /// A pixel's coverage says how far the edge reaches into it: fully covered, the glyph fills
    /// the pixel, as <see cref="ComputeSquared(byte[], int, int, OutlineShape)"/> assumes of
    /// every inside pixel; at coverage c the edge stops <c>1 - c</c> of a pixel short of that.
    /// So the distance to a covered pixel is the metric's distance between the two centres plus
    /// <c>1 - c</c>, and a pixel's distance is the least of those. For a glyph of fully covered
    /// pixels this is exactly <see cref="ComputeSquared(byte[], int, int, OutlineShape)"/>; on an
    /// anti-aliased glyph the distance varies smoothly with the edge's position instead of
    /// jumping a whole pixel where coverage crosses a threshold (issue #127: the faint row at a
    /// curved apex counted as a solid row, and the outline drew a flat cap over it).
    /// Distances of <paramref name="reach"/> or more are not resolved: such an entry holds some
    /// value that is at least <c>reach * reach</c>.
    /// </remarks>
    public static void ComputeSquaredFromCoverage(byte[] coverage, int width, int height, OutlineShape shape, float reach, float[] squared)
    {
        var size = width * height;
        var hasPartial = false;
        var solid = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            for (var i = 0; i < size; i++)
            {
                var c = coverage[i];
                solid[i] = c == 255 ? (byte)255 : (byte)0;
                hasPartial |= c is > 0 and < 255;
            }

            ComputeSquared(solid, width, height, shape, squared);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(solid);
        }

        if (!hasPartial)
            return;

        // Every metric here is at least max(|dx|, |dy|), so offsets past `radius` on either axis
        // are at reach or beyond.
        var radius = Math.Max(0, (int)MathF.Ceiling(reach) - 1);
        var side = radius + 1;
        var metric = ArrayPool<float>.Shared.Rent(side * side);
        try
        {
            for (var dy = 0; dy < side; dy++)
            {
                for (var dx = 0; dx < side; dx++)
                {
                    metric[dy * side + dx] = shape switch
                    {
                        OutlineShape.Round => MathF.Sqrt(((float)dx * dx) + ((float)dy * dy)),
                        OutlineShape.Square => Math.Max(dx, dy),
                        _ => dx + dy,
                    };
                }
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = coverage[y * width + x];
                    if (c == 0)
                        continue;

                    squared[y * width + x] = 0f;
                    if (c == 255)
                        continue;

                    var inset = (255 - c) / 255f;
                    if (inset >= reach)
                        continue;

                    int y0 = Math.Max(0, y - radius), y1 = Math.Min(height - 1, y + radius);
                    int x0 = Math.Max(0, x - radius), x1 = Math.Min(width - 1, x + radius);
                    for (var ty = y0; ty <= y1; ty++)
                    {
                        var metricRow = Math.Abs(ty - y) * side;
                        var row = ty * width;
                        for (var tx = x0; tx <= x1; tx++)
                        {
                            if (coverage[row + tx] != 0)
                                continue;

                            var distance = metric[metricRow + Math.Abs(tx - x)] + inset;
                            if (distance >= reach)
                                continue;

                            var sq = distance * distance;
                            if (sq < squared[row + tx])
                                squared[row + tx] = sq;
                        }
                    }
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(metric);
        }
    }
}
