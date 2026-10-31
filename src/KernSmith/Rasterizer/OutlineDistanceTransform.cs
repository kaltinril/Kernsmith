using System.Buffers;
using KernSmith.Atlas;

namespace KernSmith.Rasterizer;

/// <summary>
/// Squared distance from each pixel to the nearest opaque pixel, measured with the metric
/// that matches an <see cref="OutlineShape"/>.
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
}
