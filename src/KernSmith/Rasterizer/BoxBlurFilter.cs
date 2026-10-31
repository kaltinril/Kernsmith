using System.Runtime.Intrinsics;

namespace KernSmith.Rasterizer;

/// <summary>
/// The shadow box blur shared by <see cref="ShadowEffect"/> and <see cref="ShadowPostProcessor"/>:
/// a horizontal then a vertical pass, each output the sum of its 2r+1 neighbours (edge samples
/// clamped), taken in order k = -r..r from 0, times 1 / (2r+1).
/// </summary>
/// <remarks>
/// That per-output summation order fixes every output's float rounding, so it is kept exactly
/// (a sliding window reorders it and changes pixels). The vector path computes four
/// neighbouring outputs per <see cref="Vector128{T}"/>, each lane doing that output's own adds
/// and multiply in the same order: lane-wise single-precision add and multiply round exactly
/// as the scalar operations do, and nothing reduces across lanes or fuses a multiply-add. It
/// covers the columns whose window needs no clamping in the horizontal pass and every column
/// in the vertical pass (there the clamp picks whole rows); the rest runs the scalar loop.
/// Without hardware acceleration (browser-wasm) everything runs the scalar loop.
/// </remarks>
internal static class BoxBlurFilter
{
    /// <summary>
    /// Blurs the first <c>width * height</c> values of <paramref name="src"/> into
    /// <paramref name="dst"/>, using <paramref name="temp"/> for the horizontal pass.
    /// </summary>
    public static void Blur(ReadOnlySpan<float> src, Span<float> temp, Span<float> dst, int width, int height, int radius) =>
        Blur(src, temp, dst, width, height, radius, Vector128.IsHardwareAccelerated);

    /// <summary><see cref="Blur(ReadOnlySpan{float}, Span{float}, Span{float}, int, int, int)"/> with the path chosen (tests run both).</summary>
    internal static void Blur(ReadOnlySpan<float> src, Span<float> temp, Span<float> dst, int width, int height, int radius, bool vector)
    {
        var invKernel = 1f / (2f * radius + 1f);
        var lanes = Vector128<float>.Count;

        // Horizontal pass.
        for (var y = 0; y < height; y++)
        {
            var row = src.Slice(y * width, width);
            var outRow = temp.Slice(y * width, width);

            // Columns radius .. width-1-radius read no clamped sample.
            var x = 0;
            if (vector)
            {
                var scale = Vector128.Create(invKernel);
                for (x = radius; x + lanes - 1 <= width - 1 - radius; x += lanes)
                {
                    var sum = Vector128<float>.Zero;
                    for (var k = -radius; k <= radius; k++)
                        sum += Vector128.Create(row.Slice(x + k, lanes));
                    (sum * scale).CopyTo(outRow.Slice(x, lanes));
                }

                for (var edge = 0; edge < radius && edge < width; edge++)
                    outRow[edge] = HorizontalAt(row, edge, width, radius, invKernel);
            }

            for (; x < width; x++)
                outRow[x] = HorizontalAt(row, x, width, radius, invKernel);
        }

        // Vertical pass.
        for (var y = 0; y < height; y++)
        {
            var outRow = dst.Slice(y * width, width);
            var x = 0;
            if (vector)
            {
                var scale = Vector128.Create(invKernel);
                for (; x + lanes <= width; x += lanes)
                {
                    var sum = Vector128<float>.Zero;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var sy = Math.Clamp(y + k, 0, height - 1);
                        sum += Vector128.Create(temp.Slice(sy * width + x, lanes));
                    }
                    (sum * scale).CopyTo(outRow.Slice(x, lanes));
                }
            }

            for (; x < width; x++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    sum += temp[sy * width + x];
                }
                outRow[x] = sum * invKernel;
            }
        }
    }

    private static float HorizontalAt(ReadOnlySpan<float> row, int x, int width, int radius, float invKernel)
    {
        float sum = 0;
        for (var k = -radius; k <= radius; k++)
            sum += row[Math.Clamp(x + k, 0, width - 1)];
        return sum * invKernel;
    }
}
