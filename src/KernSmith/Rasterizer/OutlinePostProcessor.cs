using System.Buffers;
using KernSmith.Atlas;
using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Adds a colored outline (border) around each glyph. Works with both grayscale and RGBA glyphs.
/// The outline is anti-aliased for smooth edges.
/// </summary>
public sealed class OutlinePostProcessor : IGlyphPostProcessor
{
    private readonly int _outlineWidth;
    private readonly byte _outlineR;
    private readonly byte _outlineG;
    private readonly byte _outlineB;

    internal int OutlineWidth => _outlineWidth;
    internal byte OutlineR => _outlineR;
    internal byte OutlineG => _outlineG;
    internal byte OutlineB => _outlineB;

    /// <summary>False when anti-aliasing is off: the outline is a hard 0/255 threshold instead of a soft ramp.</summary>
    internal bool Smooth { get; init; } = true;

    /// <summary>Corner shape (distance metric) of the outline.</summary>
    internal OutlineShape Shape { get; init; } = OutlineShape.Round;

    /// <summary>
    /// Creates an outline effect with the given thickness and color.
    /// </summary>
    /// <param name="outlineWidth">Thickness in pixels.</param>
    /// <param name="outlineR">Outline color red (0-255). Default is black.</param>
    /// <param name="outlineG">Outline color green (0-255).</param>
    /// <param name="outlineB">Outline color blue (0-255).</param>
    public OutlinePostProcessor(int outlineWidth, byte outlineR = 0, byte outlineG = 0, byte outlineB = 0)
    {
        _outlineWidth = outlineWidth;
        _outlineR = outlineR;
        _outlineG = outlineG;
        _outlineB = outlineB;
    }

    /// <inheritdoc />
    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        if (_outlineWidth <= 0 || glyph.BitmapData.Length == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var ow = _outlineWidth;

        var dstW = srcW + 2 * ow;
        var dstH = srcH + 2 * ow;

        // Step 1: Extract source alpha into an expanded buffer, centered.
        // expandedAlpha is read in full by the EDT (border pixels are never written here),
        // so a rented buffer must be cleared first.
        var size = dstW * dstH;
        float[] squaredDist;
        var expandedAlpha = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            Array.Clear(expandedAlpha, 0, size);

            for (var y = 0; y < srcH; y++)
            {
                for (var x = 0; x < srcW; x++)
                {
                    byte alpha;
                    if (glyph.Format == PixelFormat.Rgba32)
                    {
                        var srcIdx = y * glyph.Pitch + x * 4 + 3;
                        alpha = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                    }
                    else
                    {
                        var srcIdx = y * glyph.Pitch + x;
                        alpha = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                    }

                    expandedAlpha[(y + ow) * dstW + (x + ow)] = alpha;
                }
            }

            // Step 2: Distance from the glyph. Anti-aliased: to the edge its coverage describes,
            // as OutlineEffect measures it (nothing at or past the end of the ramp is needed).
            // Otherwise whole-pixel distance from every covered pixel.
            squaredDist = new float[size];
            if (Smooth)
                OutlineDistanceTransform.ComputeSquaredFromCoverage(expandedAlpha, dstW, dstH, Shape, ow + 1f, squaredDist);
            else
                OutlineDistanceTransform.ComputeSquared(expandedAlpha, dstW, dstH, Shape, squaredDist);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(expandedAlpha);
        }

        // Step 3: Build RGBA output with outline color and anti-aliased alpha.
        var dst = new byte[dstW * dstH * 4];

        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                float outlineAlpha;
                if (Smooth)
                {
                    var dist = MathF.Sqrt(squaredDist[y * dstW + x]);
                    // Anti-aliased outline alpha, as OutlineEffect's: the share of the pixel within
                    // ow of the glyph's edge (distances are between pixel centres, and a fully
                    // covered pixel's edge is half a pixel past its centre).
                    outlineAlpha = Math.Clamp(255f * (ow + 1f - dist), 0f, 255f);
                }
                else
                {
                    // No anti-aliasing: hard threshold so the outline is exactly ow solid pixels.
                    outlineAlpha = squaredDist[y * dstW + x] <= (float)ow * ow ? 255f : 0f;
                }

                if (outlineAlpha <= 0)
                    continue;

                var idx = (y * dstW + x) * 4;
                dst[idx + 0] = _outlineR;
                dst[idx + 1] = _outlineG;
                dst[idx + 2] = _outlineB;
                dst[idx + 3] = (byte)outlineAlpha;
            }
        }

        // Step 4: Composite original glyph on top using alpha-over blending.
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

                var di = ((y + ow) * dstW + (x + ow)) * 4;

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
            Pitch = dstW * 4,
            Metrics = newMetrics,
            Format = PixelFormat.Rgba32
        };
    }
}
