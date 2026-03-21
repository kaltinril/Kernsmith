using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Post-processor that adds a drop shadow behind glyphs.
/// Supports configurable offset, color, opacity, and optional box blur.
/// </summary>
public sealed class ShadowPostProcessor : IGlyphPostProcessor
{
    /// <summary>Horizontal shadow offset in pixels (positive = right).</summary>
    public int OffsetX { get; }

    /// <summary>Vertical shadow offset in pixels (positive = down).</summary>
    public int OffsetY { get; }

    /// <summary>Blur radius. 0 = hard shadow, greater values produce softer shadows.</summary>
    public int BlurRadius { get; }

    /// <summary>Shadow red channel.</summary>
    public byte ShadowR { get; }

    /// <summary>Shadow green channel.</summary>
    public byte ShadowG { get; }

    /// <summary>Shadow blue channel.</summary>
    public byte ShadowB { get; }

    /// <summary>Shadow opacity (0.0 to 1.0).</summary>
    public float Opacity { get; }

    public ShadowPostProcessor(
        int offsetX = 2,
        int offsetY = 2,
        int blurRadius = 0,
        byte shadowR = 0,
        byte shadowG = 0,
        byte shadowB = 0,
        float opacity = 1.0f)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        BlurRadius = Math.Max(0, blurRadius);
        ShadowR = shadowR;
        ShadowG = shadowG;
        ShadowB = shadowB;
        Opacity = Math.Clamp(opacity, 0f, 1f);
    }

    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;

        // Calculate the expanded bitmap size to accommodate shadow + blur.
        var expandLeft = Math.Max(0, -OffsetX) + BlurRadius;
        var expandRight = Math.Max(0, OffsetX) + BlurRadius;
        var expandTop = Math.Max(0, -OffsetY) + BlurRadius;
        var expandBottom = Math.Max(0, OffsetY) + BlurRadius;

        var dstW = srcW + expandLeft + expandRight;
        var dstH = srcH + expandTop + expandBottom;

        // Step 1: Extract source alpha channel into an expanded buffer at the shadow offset.
        var shadowAlpha = new float[dstW * dstH];

        // Shadow glyph position in the expanded buffer.
        var shadowOriginX = expandLeft + OffsetX;
        var shadowOriginY = expandTop + OffsetY;

        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                float alpha;
                if (glyph.Format == PixelFormat.Rgba32)
                {
                    var srcIdx = y * glyph.Pitch + x * 4 + 3;
                    alpha = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] / 255f : 0f;
                }
                else
                {
                    var srcIdx = y * glyph.Pitch + x;
                    alpha = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] / 255f : 0f;
                }

                var dx = shadowOriginX + x;
                var dy = shadowOriginY + y;
                if (dx >= 0 && dx < dstW && dy >= 0 && dy < dstH)
                    shadowAlpha[dy * dstW + dx] = alpha * Opacity;
            }
        }

        // Step 2: Apply box blur if requested.
        if (BlurRadius > 0)
            shadowAlpha = BoxBlur(shadowAlpha, dstW, dstH, BlurRadius);

        // Step 3: Composite shadow + original glyph into RGBA output.
        var dst = new byte[dstW * dstH * 4];

        // Draw shadow layer first.
        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                var sa = shadowAlpha[y * dstW + x];
                if (sa <= 0) continue;

                var idx = (y * dstW + x) * 4;
                dst[idx + 0] = ShadowR;
                dst[idx + 1] = ShadowG;
                dst[idx + 2] = ShadowB;
                dst[idx + 3] = (byte)Math.Min(255, (int)(sa * 255));
            }
        }

        // Draw original glyph on top using alpha-over compositing.
        var glyphOriginX = expandLeft;
        var glyphOriginY = expandTop;

        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                byte srcR, srcG, srcB, srcA;

                if (glyph.Format == PixelFormat.Rgba32)
                {
                    var si = y * glyph.Pitch + x * 4;
                    if (si + 3 >= glyph.BitmapData.Length) continue;
                    srcR = glyph.BitmapData[si];
                    srcG = glyph.BitmapData[si + 1];
                    srcB = glyph.BitmapData[si + 2];
                    srcA = glyph.BitmapData[si + 3];
                }
                else
                {
                    var si = y * glyph.Pitch + x;
                    if (si >= glyph.BitmapData.Length) continue;
                    srcR = 255;
                    srcG = 255;
                    srcB = 255;
                    srcA = glyph.BitmapData[si];
                }

                if (srcA == 0) continue;

                var di = ((glyphOriginY + y) * dstW + (glyphOriginX + x)) * 4;

                // Alpha-over blending.
                var dstA = dst[di + 3];
                var sA = srcA / 255f;
                var dA = dstA / 255f;
                var outA = sA + dA * (1f - sA);

                if (outA > 0)
                {
                    dst[di + 0] = (byte)((srcR * sA + dst[di + 0] * dA * (1f - sA)) / outA);
                    dst[di + 1] = (byte)((srcG * sA + dst[di + 1] * dA * (1f - sA)) / outA);
                    dst[di + 2] = (byte)((srcB * sA + dst[di + 2] * dA * (1f - sA)) / outA);
                    dst[di + 3] = (byte)Math.Min(255, (int)(outA * 255));
                }
            }
        }

        // Update metrics for the expanded bitmap.
        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX - expandLeft,
            BearingY: metrics.BearingY + expandTop,
            Advance: metrics.Advance,
            Width: metrics.Width + expandLeft + expandRight,
            Height: metrics.Height + expandTop + expandBottom);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstW * 4,
            Metrics = newMetrics,
            Format = PixelFormat.Rgba32
        };
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

        // Horizontal pass.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sx = Math.Clamp(x + k, 0, width - 1);
                    sum += src[y * width + sx];
                }
                temp[y * width + x] = sum * invKernel;
            }
        }

        // Vertical pass.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    sum += temp[sy * width + x];
                }
                dst[y * width + x] = sum * invKernel;
            }
        }

        return dst;
    }
}
