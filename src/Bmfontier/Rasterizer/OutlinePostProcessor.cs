using Bmfontier.Font.Models;

namespace Bmfontier.Rasterizer;

/// <summary>
/// Post-processor that adds an outline (border) around glyphs.
/// </summary>
public sealed class OutlinePostProcessor : IGlyphPostProcessor
{
    private readonly int _outlineWidth;

    public OutlinePostProcessor(int outlineWidth)
    {
        _outlineWidth = outlineWidth;
    }

    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (_outlineWidth <= 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var srcPitch = glyph.Pitch;
        var ow = _outlineWidth;

        var dstW = srcW + 2 * ow;
        var dstH = srcH + 2 * ow;
        var dst = new byte[dstW * dstH];

        // Pass 1: For each pixel in the expanded bitmap, check if any source pixel
        // within outlineWidth distance is non-zero. If so, set to 255 (outline).
        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                // Map back to source coordinates.
                var sx = x - ow;
                var sy = y - ow;

                // Scan a square of radius outlineWidth in the original bitmap.
                var found = false;
                var minScanX = Math.Max(0, sx - ow);
                var maxScanX = Math.Min(srcW - 1, sx + ow);
                var minScanY = Math.Max(0, sy - ow);
                var maxScanY = Math.Min(srcH - 1, sy + ow);

                for (var scanY = minScanY; scanY <= maxScanY && !found; scanY++)
                {
                    for (var scanX = minScanX; scanX <= maxScanX && !found; scanX++)
                    {
                        if (glyph.BitmapData[scanY * srcPitch + scanX] > 0)
                        {
                            // Check Euclidean distance.
                            var dx = scanX - sx;
                            var dy = scanY - sy;
                            if (dx * dx + dy * dy <= ow * ow)
                                found = true;
                        }
                    }
                }

                if (found)
                    dst[y * dstW + x] = 255;
            }
        }

        // Pass 2: Copy the original glyph bitmap on top, centered at the outline offset.
        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                var srcVal = glyph.BitmapData[y * srcPitch + x];
                if (srcVal > 0)
                    dst[(y + ow) * dstW + (x + ow)] = srcVal;
            }
        }

        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX - ow,
            BearingY: metrics.BearingY + ow,
            Advance: metrics.Advance,
            Width: metrics.Width + 2 * ow,
            Height: metrics.Height + 2 * ow);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstW,
            Metrics = newMetrics,
            Format = glyph.Format
        };
    }
}
