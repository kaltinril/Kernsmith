using System.Buffers;

namespace KernSmith.Atlas;

/// <summary>
/// Computes the Euclidean Distance Transform using the Felzenszwalb-Huttenlocher algorithm.
/// Runs in O(W*H) time regardless of distance magnitude.
/// </summary>
internal static class EuclideanDistanceTransform
{
    private const float Infinity = float.MaxValue / 2f;

    /// <summary>
    /// Computes squared Euclidean distances from each pixel to the nearest "inside" pixel.
    /// Pixels with alpha &gt; 0 are considered inside (distance 0).
    /// </summary>
    /// <param name="alphaData">Alpha channel data (one byte per pixel).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <returns>Array of squared distances, one float per pixel.</returns>
    public static float[] Compute(byte[] alphaData, int width, int height)
    {
        var size = width * height;
        var grid = new float[size];

        // Initialize: inside pixels = 0, outside pixels = infinity.
        for (var i = 0; i < size; i++)
            grid[i] = alphaData[i] > 0 ? 0f : Infinity;

        var maxDim = Math.Max(width, height);
        var f = ArrayPool<float>.Shared.Rent(maxDim);
        var v = ArrayPool<int>.Shared.Rent(maxDim);
        var z = ArrayPool<float>.Shared.Rent(maxDim + 1);
        var d = ArrayPool<float>.Shared.Rent(maxDim);
        try
        {
            // Column-wise 1D EDT.
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                    f[y] = grid[y * width + x];

                Edt1D(f, height, v, z, d);

                for (var y = 0; y < height; y++)
                    grid[y * width + x] = d[y];
            }

            // Row-wise 1D EDT.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    f[x] = grid[y * width + x];

                Edt1D(f, width, v, z, d);

                for (var x = 0; x < width; x++)
                    grid[y * width + x] = d[x];
            }

            return grid;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(f);
            ArrayPool<int>.Shared.Return(v);
            ArrayPool<float>.Shared.Return(z);
            ArrayPool<float>.Shared.Return(d);
        }
    }

    /// <summary>
    /// 1D distance transform using the parabola lower-envelope approach.
    /// </summary>
    /// <param name="f">Input function values (squared distances from previous pass or initial).</param>
    /// <param name="n">Length of the input.</param>
    /// <param name="v">Scratch buffer for parabola locations (length &gt;= n).</param>
    /// <param name="z">Scratch buffer for parabola boundaries (length &gt;= n+1).</param>
    /// <param name="d">Output distances (length &gt;= n).</param>
    private static void Edt1D(float[] f, int n, int[] v, float[] z, float[] d)
    {
        var k = 0;
        v[0] = 0;
        z[0] = -Infinity;
        z[1] = Infinity;

        for (var q = 1; q < n; q++)
        {
            while (true)
            {
                var vk = v[k];
                var s = ((f[q] + (float)q * q) - (f[vk] + (float)vk * vk)) / (2f * q - 2f * vk);
                if (s > z[k])
                {
                    k++;
                    v[k] = q;
                    z[k] = s;
                    z[k + 1] = Infinity;
                    break;
                }

                k--;
            }
        }

        k = 0;
        for (var q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
                k++;

            var diff = q - v[k];
            d[q] = (float)diff * diff + f[v[k]];
        }
    }
}
