using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Generates a drop shadow layer from a grayscale alpha mask.
/// Z-order 0: renders furthest back (behind everything).
/// </summary>
internal sealed class ShadowEffect : IGlyphEffect
{
    public int ZOrder => 0;

    private readonly int _offsetX;
    private readonly int _offsetY;
    private readonly int _blurRadius;
    private readonly byte _shadowR;
    private readonly byte _shadowG;
    private readonly byte _shadowB;
    private readonly float _opacity;
    private readonly bool _hardShadow;

    public ShadowEffect(
        int offsetX = 2,
        int offsetY = 2,
        int blurRadius = 0,
        byte shadowR = 0,
        byte shadowG = 0,
        byte shadowB = 0,
        float opacity = 1.0f,
        bool hardShadow = false)
    {
        _offsetX = offsetX;
        _offsetY = offsetY;
        _blurRadius = Math.Max(0, blurRadius);
        _shadowR = shadowR;
        _shadowG = shadowG;
        _shadowB = shadowB;
        _opacity = Math.Clamp(opacity, 0f, 1f);
        _hardShadow = hardShadow;
    }

    public GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics)
    {
        // Calculate the expanded bitmap size to accommodate shadow + blur.
        var expandLeft = Math.Max(0, -_offsetX) + _blurRadius;
        var expandRight = Math.Max(0, _offsetX) + _blurRadius;
        var expandTop = Math.Max(0, -_offsetY) + _blurRadius;
        var expandBottom = Math.Max(0, _offsetY) + _blurRadius;

        var dstW = width + expandLeft + expandRight;
        var dstH = height + expandTop + expandBottom;

        // Step 1: Place source alpha at the shadow offset position in the expanded buffer.
        var shadowAlpha = new float[dstW * dstH];

        var shadowOriginX = expandLeft + _offsetX;
        var shadowOriginY = expandTop + _offsetY;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var srcIdx = y * pitch + x;
                var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] / 255f : 0f;
                if (_hardShadow && alpha > 0f)
                    alpha = 1f;

                var dx = shadowOriginX + x;
                var dy = shadowOriginY + y;
                if (dx >= 0 && dx < dstW && dy >= 0 && dy < dstH)
                    shadowAlpha[dy * dstW + dx] = alpha * _opacity;
            }
        }

        // Step 2: Apply box blur if requested.
        if (_blurRadius > 0)
            shadowAlpha = BoxBlur(shadowAlpha, dstW, dstH, _blurRadius);

        // Step 3: Build RGBA output with shadow color.
        var dst = new byte[dstW * dstH * 4];

        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                var sa = shadowAlpha[y * dstW + x];
                if (sa <= 0) continue;

                var idx = (y * dstW + x) * 4;
                dst[idx + 0] = _shadowR;
                dst[idx + 1] = _shadowG;
                dst[idx + 2] = _shadowB;
                dst[idx + 3] = (byte)Math.Min(255, (int)(sa * 255));
            }
        }

        // The layer origin is offset relative to the glyph origin.
        // expandLeft/expandTop is how much the canvas extends before the glyph origin.
        return new GlyphLayer(dst, dstW, dstH, OffsetX: -expandLeft, OffsetY: -expandTop, ZOrder);
    }

    /// <summary>
    /// Two-pass separable box blur.
    /// </summary>
    private static float[] BoxBlur(float[] src, int width, int height, int radius)
    {
        var temp = new float[width * height];
        var dst = new float[width * height];
        var kernelSize = radius * 2 + 1;
        var invKernel = 1f / kernelSize;

        // Horizontal pass — sliding window across each row.
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;

            // Build initial sum for x = 0: sum of src[clamp(k, 0, width-1)] for k in -radius..radius.
            // Negative indices clamp to 0, so src[0] is counted (radius) extra times.
            float sum = src[rowOffset] * radius;
            for (var k = 0; k <= radius; k++)
                sum += src[rowOffset + Math.Min(k, width - 1)];

            temp[rowOffset] = sum * invKernel;

            // Slide across remaining pixels.
            for (var x = 1; x < width; x++)
            {
                sum += src[rowOffset + Math.Min(x + radius, width - 1)];
                sum -= src[rowOffset + Math.Clamp(x - radius - 1, 0, width - 1)];
                temp[rowOffset + x] = sum * invKernel;
            }
        }

        // Vertical pass — sliding window down each column.
        for (var x = 0; x < width; x++)
        {
            // Build initial sum for y = 0: sum of temp[clamp(k, 0, height-1) * width + x] for k in -radius..radius.
            float sum = temp[x] * radius;
            for (var k = 0; k <= radius; k++)
                sum += temp[Math.Min(k, height - 1) * width + x];

            dst[x] = sum * invKernel;

            // Slide down remaining pixels.
            for (var y = 1; y < height; y++)
            {
                sum += temp[Math.Min(y + radius, height - 1) * width + x];
                sum -= temp[Math.Clamp(y - radius - 1, 0, height - 1) * width + x];
                dst[y * width + x] = sum * invKernel;
            }
        }

        return dst;
    }
}
