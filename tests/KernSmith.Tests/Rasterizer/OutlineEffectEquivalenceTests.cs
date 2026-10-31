using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Holds <see cref="OutlineEffect"/> to a frozen copy of its straightforward implementation
/// (per-pixel sqrt + clamp, allocating distance transforms) over randomized glyphs, widths,
/// shapes and colors: the layer must be byte-identical.
/// </summary>
public sealed class OutlineEffectEquivalenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Generate_RandomGlyphs_MatchesReference(int seed)
    {
        var rng = new Random(seed);
        var shapes = new[] { OutlineShape.Round, OutlineShape.Square, OutlineShape.Plus };
        for (var trial = 0; trial < 120; trial++)
        {
            var width = rng.Next(0, 40);
            var height = rng.Next(0, 40);
            var pitch = width + rng.Next(0, 4);
            var alpha = RandomGlyph(rng, width, height, pitch);
            var ow = rng.Next(0, 9);
            var smooth = rng.Next(4) != 0;
            var shape = shapes[rng.Next(shapes.Length)];
            byte r = (byte)rng.Next(256), g = (byte)rng.Next(256), b = (byte)rng.Next(256);
            var metrics = new GlyphMetrics(0, 0, width, width, height);

            var actual = new OutlineEffect(ow, r, g, b, smooth, shape).Generate(alpha, width, height, pitch, metrics);
            var expected = ReferenceGenerate(ow, r, g, b, smooth, shape, alpha, width, height, pitch);

            actual.Width.ShouldBe(expected.Width);
            actual.Height.ShouldBe(expected.Height);
            actual.OffsetX.ShouldBe(expected.OffsetX);
            actual.OffsetY.ShouldBe(expected.OffsetY);
            actual.ZOrder.ShouldBe(expected.ZOrder);
            actual.RgbaData.ShouldBe(expected.RgbaData, $"seed {seed} trial {trial} ow {ow} {shape} smooth {smooth}");
        }
    }

    [Fact]
    public void Generate_LargeOutline_MatchesReference()
    {
        // Wide rings reach squared distances in the thousands.
        var rng = new Random(99);
        var alpha = RandomGlyph(rng, 30, 30, 30);
        foreach (var ow in new[] { 12, 25, 40 })
        {
            var actual = new OutlineEffect(ow).Generate(alpha, 30, 30, 30, default);
            var expected = ReferenceGenerate(ow, 0, 0, 0, true, OutlineShape.Round, alpha, 30, 30, 30);
            actual.RgbaData.ShouldBe(expected.RgbaData);
        }
    }

    private static byte[] RandomGlyph(Random rng, int width, int height, int pitch)
    {
        // Blobs with anti-aliased-ish edges, sometimes a buffer shorter than pitch * height.
        var data = new byte[Math.Max(0, pitch * height - (rng.Next(5) == 0 ? rng.Next(0, pitch + 1) : 0))];
        var blobs = rng.Next(0, 4);
        for (var i = 0; i < blobs && width > 0 && height > 0; i++)
        {
            var cx = rng.NextDouble() * width;
            var cy = rng.NextDouble() * height;
            var rad = 1 + rng.NextDouble() * 10;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var a = (int)Math.Clamp((rad - d) * 128 + 128, 0, 255);
                    var idx = y * pitch + x;
                    if (idx < data.Length && a > data[idx])
                        data[idx] = (byte)a;
                }
            }
        }
        if (data.Length > 0 && rng.Next(3) == 0)
            data[rng.Next(data.Length)] = (byte)rng.Next(1, 32); // faint isolated pixel
        return data;
    }

    // ---------------------------------------------------------------- frozen reference

    private static GlyphLayer ReferenceGenerate(int ow, byte outlineR, byte outlineG, byte outlineB, bool smooth,
        OutlineShape shape, byte[] alphaData, int width, int height, int pitch)
    {
        var dstW = width + 2 * ow;
        var dstH = height + 2 * ow;
        var size = dstW * dstH;
        var expandedAlpha = new byte[size];
        var binaryAlpha = new byte[size];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var srcIdx = y * pitch + x;
                var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                expandedAlpha[(y + ow) * dstW + (x + ow)] = alpha;
            }
        }

        for (var i = 0; i < size; i++)
            binaryAlpha[i] = expandedAlpha[i] >= 32 ? (byte)255 : (byte)0;

        var squaredDist = ReferenceSquared(binaryAlpha, dstW, dstH, shape);
        var dst = new byte[size * 4];

        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                var pixelIdx = y * dstW + x;
                if (expandedAlpha[pixelIdx] > 0)
                {
                    var idx = pixelIdx * 4;
                    dst[idx + 0] = outlineR;
                    dst[idx + 1] = outlineG;
                    dst[idx + 2] = outlineB;
                    dst[idx + 3] = 255;
                }
                else
                {
                    float outlineAlpha;
                    if (smooth)
                    {
                        var dist = MathF.Sqrt(squaredDist[pixelIdx]);
                        outlineAlpha = Math.Clamp(255f * (ow + 0.75f - dist) / 1.5f, 0f, 255f);
                    }
                    else
                    {
                        outlineAlpha = squaredDist[pixelIdx] <= (float)ow * ow ? 255f : 0f;
                    }

                    if (outlineAlpha <= 0)
                        continue;

                    var idx = pixelIdx * 4;
                    dst[idx + 0] = outlineR;
                    dst[idx + 1] = outlineG;
                    dst[idx + 2] = outlineB;
                    dst[idx + 3] = (byte)outlineAlpha;
                }
            }
        }

        return new GlyphLayer(dst, dstW, dstH, OffsetX: -ow, OffsetY: -ow, 1);
    }

    private const float Infinity = float.MaxValue / 2f;

    private static float[] ReferenceSquared(byte[] alphaData, int width, int height, OutlineShape shape)
    {
        var size = width * height;
        if (shape == OutlineShape.Round)
        {
            var grid = new float[size];
            for (var i = 0; i < size; i++)
                grid[i] = alphaData[i] > 0 ? 0f : Infinity;

            var maxDim = Math.Max(width, height);
            var f = new float[maxDim + 1];
            var v = new int[maxDim + 1];
            var z = new float[maxDim + 2];
            var d = new float[maxDim + 1];
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                    f[y] = grid[y * width + x];
                Edt1D(f, height, v, z, d);
                for (var y = 0; y < height; y++)
                    grid[y * width + x] = d[y];
            }
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

        const int Far = int.MaxValue / 4;
        var diagonals = shape == OutlineShape.Square;
        var cells = new int[size];
        for (var i = 0; i < size; i++)
            cells[i] = alphaData[i] > 0 ? 0 : Far;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = cells[y * width + x];
                if (x > 0) best = Math.Min(best, cells[y * width + x - 1] + 1);
                if (y > 0)
                {
                    best = Math.Min(best, cells[(y - 1) * width + x] + 1);
                    if (diagonals)
                    {
                        if (x > 0) best = Math.Min(best, cells[(y - 1) * width + x - 1] + 1);
                        if (x < width - 1) best = Math.Min(best, cells[(y - 1) * width + x + 1] + 1);
                    }
                }
                cells[y * width + x] = best;
            }
        }
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var best = cells[y * width + x];
                if (x < width - 1) best = Math.Min(best, cells[y * width + x + 1] + 1);
                if (y < height - 1)
                {
                    best = Math.Min(best, cells[(y + 1) * width + x] + 1);
                    if (diagonals)
                    {
                        if (x < width - 1) best = Math.Min(best, cells[(y + 1) * width + x + 1] + 1);
                        if (x > 0) best = Math.Min(best, cells[(y + 1) * width + x - 1] + 1);
                    }
                }
                cells[y * width + x] = best;
            }
        }
        var squared = new float[size];
        for (var i = 0; i < size; i++)
            squared[i] = cells[i] >= Far ? float.MaxValue / 2f : (float)cells[i] * cells[i];
        return squared;
    }

    private static void Edt1D(float[] f, int n, int[] v, float[] z, float[] d)
    {
        if (n == 0)
            return;
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
