using System.Buffers;
using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Generates all layers from effects and composites them back-to-front
/// into a single RGBA glyph.
/// </summary>
internal static class GlyphCompositor
{
    /// <summary>
    /// Applies all effects to a source glyph and composites the resulting layers into a single RGBA bitmap.
    /// </summary>
    /// <param name="sourceGlyph">The rasterized source glyph (grayscale or RGBA) to composite.</param>
    /// <param name="effects">The effects to apply, sorted internally by Z-order (shadow Z=0, outline Z=1, body Z=2).</param>
    /// <param name="fillR">Body fill red channel. Tints the default body layer when no gradient/body effect is present.</param>
    /// <param name="fillG">Body fill green channel.</param>
    /// <param name="fillB">Body fill blue channel.</param>
    /// <param name="fillA">Body fill alpha; multiplies the glyph mask alpha. The default <c>(255,255,255,255)</c> reproduces the legacy opaque-white body.</param>
    public static RasterizedGlyph Composite(
        RasterizedGlyph sourceGlyph,
        IReadOnlyList<IGlyphEffect> effects,
        byte fillR = 255,
        byte fillG = 255,
        byte fillB = 255,
        byte fillA = 255)
    {
        // A non-white/non-opaque fill color must still tint the default body layer
        // even when no other effects are present, so don't early-out in that case.
        var hasFillTint = fillR != 255 || fillG != 255 || fillB != 255 || fillA != 255;
        if (effects.Count == 0 && !hasFillTint)
            return sourceGlyph;

        if (sourceGlyph.Width == 0 || sourceGlyph.Height == 0 || sourceGlyph.BitmapData.Length == 0)
            return sourceGlyph;

        // 1. The alpha mask the effects read. Every reader indexes it as y * pitch + x for
        // x < width, y < height and reads 0 at or past its length, so a grayscale glyph's own
        // bytes at its own pitch read exactly as a width-pitch copy would; an RGBA glyph's
        // alpha is extracted into a rented buffer (only its first width * height bytes are read).
        var srcW = sourceGlyph.Width;
        var srcH = sourceGlyph.Height;
        byte[]? rentedAlpha = null;
        byte[]? rentedBody = null;
        byte[] alphaData;
        int alphaPitch;
        if (sourceGlyph.Format == PixelFormat.Rgba32)
        {
            rentedAlpha = ArrayPool<byte>.Shared.Rent(srcW * srcH);
            ExtractAlpha(sourceGlyph, rentedAlpha);
            alphaData = rentedAlpha;
            alphaPitch = srcW;
        }
        else
        {
            alphaData = sourceGlyph.BitmapData;
            alphaPitch = sourceGlyph.Pitch;
        }

        // 2. Generate layers with dependency awareness.
        // Shadow (Z=0) should use the outlined shape when outline (Z=1) is present,
        // so the shadow matches the full silhouette including the border.
        var layers = new List<GlyphLayer>(effects.Count);
        bool hasBodyEffect = false;

        // Separate effects by type.
        IGlyphEffect? outlineEffect = null;
        IGlyphEffect? shadowEffect = null;
        var otherEffects = new List<IGlyphEffect>();

        foreach (var effect in effects)
        {
            if (effect.ZOrder == 0) shadowEffect = effect;
            else if (effect.ZOrder == 1) outlineEffect = effect;
            else otherEffects.Add(effect);
            if (effect.ZOrder == 2) hasBodyEffect = true;
        }

        // Generate outline layer first (if present).
        GlyphLayer? outlineLayer = null;
        if (outlineEffect != null)
        {
            outlineLayer = outlineEffect.Generate(alphaData, srcW, srcH, alphaPitch, sourceGlyph.Metrics);
            layers.Add(outlineLayer);
        }

        // Generate shadow from the full glyph silhouette.
        // When outline is present, merge outline alpha with the original glyph alpha
        // so the shadow covers both the border and the glyph body.
        if (shadowEffect != null)
        {
            if (outlineLayer != null)
            {
                // Merge outline alpha with original glyph alpha into the outline-sized canvas.
                // The shadow silhouette must be fully opaque everywhere the final glyph will be
                // (outline ring + body interior), otherwise anti-aliased boundary pixels between
                // the ring and body produce sub-255 alpha, causing lighter shadow artifacts.
                // mergedAlpha is fully written (every element) before it is read, so no clear is needed.
                var mergedSize = outlineLayer.Width * outlineLayer.Height;
                var mergedAlpha = ArrayPool<byte>.Shared.Rent(mergedSize);
                try
                {
                    // First, copy outline alpha.
                    for (var i = 0; i < mergedSize; i++)
                        mergedAlpha[i] = outlineLayer.RgbaData[i * 4 + 3];

                    // Then, overlay the original glyph alpha at the correct offset.
                    // The glyph sits at (-outlineLayer.OffsetX, -outlineLayer.OffsetY) within the outline canvas.
                    var glyphOffX = -outlineLayer.OffsetX;
                    var glyphOffY = -outlineLayer.OffsetY;
                    for (var y = 0; y < srcH; y++)
                    {
                        for (var x = 0; x < srcW; x++)
                        {
                            var dx = glyphOffX + x;
                            var dy = glyphOffY + y;
                            if (dx < 0 || dx >= outlineLayer.Width || dy < 0 || dy >= outlineLayer.Height) continue;

                            var srcIdx = y * alphaPitch + x;
                            var srcAlpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                            var dstIdx = dy * outlineLayer.Width + dx;
                            mergedAlpha[dstIdx] = Math.Max(mergedAlpha[dstIdx], srcAlpha);
                        }
                    }

                    var outlineMetrics = new Font.Models.GlyphMetrics(
                        BearingX: sourceGlyph.Metrics.BearingX + outlineLayer.OffsetX,
                        BearingY: sourceGlyph.Metrics.BearingY - outlineLayer.OffsetY,
                        Advance: sourceGlyph.Metrics.Advance,
                        Width: outlineLayer.Width,
                        Height: outlineLayer.Height);

                    var shadowLayer = shadowEffect.Generate(
                        mergedAlpha, outlineLayer.Width, outlineLayer.Height,
                        outlineLayer.Width, outlineMetrics);

                    layers.Add(shadowLayer with
                    {
                        OffsetX = shadowLayer.OffsetX + outlineLayer.OffsetX,
                        OffsetY = shadowLayer.OffsetY + outlineLayer.OffsetY
                    });
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(mergedAlpha);
                }
            }
            else
            {
                layers.Add(shadowEffect.Generate(alphaData, srcW, srcH, alphaPitch, sourceGlyph.Metrics));
            }
        }

        // Generate remaining effects (body/gradient).
        foreach (var effect in otherEffects)
        {
            layers.Add(effect.Generate(alphaData, srcW, srcH, alphaPitch, sourceGlyph.Metrics));
        }

        // If no body effect was provided, create a default white body layer
        // so the glyph itself is still visible.
        if (!hasBodyEffect)
        {
            rentedBody = ArrayPool<byte>.Shared.Rent(srcW * srcH * 4);
            var bodyLayer = CreateDefaultBodyLayer(rentedBody, alphaData, srcW, srcH, alphaPitch, sourceGlyph, fillR, fillG, fillB, fillA);
            layers.Add(bodyLayer);
        }

        // 3. Sort layers by ZOrder (back to front).
        layers.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

        // 4. Calculate canvas size (union of all layer bounds).
        // All offsets are relative to the original glyph origin (0,0).
        var minX = 0;
        var minY = 0;
        var maxX = srcW;
        var maxY = srcH;

        foreach (var layer in layers)
        {
            minX = Math.Min(minX, layer.OffsetX);
            minY = Math.Min(minY, layer.OffsetY);
            maxX = Math.Max(maxX, layer.OffsetX + layer.Width);
            maxY = Math.Max(maxY, layer.OffsetY + layer.Height);
        }

        var canvasW = maxX - minX;
        var canvasH = maxY - minY;

        // 5. Composite back-to-front using alpha-over blending.
        var dst = new byte[canvasW * canvasH * 4];

        foreach (var layer in layers)
        {
            // Where this layer starts on the canvas
            var layerCanvasX = layer.OffsetX - minX;
            var layerCanvasY = layer.OffsetY - minY;

            // A rented body layer is longer than its pixels; only Width * Height are read.
            var data = layer.RgbaData;
            var dataLength = Math.Min(data.Length, layer.Width * layer.Height * 4);
            for (var y = 0; y < layer.Height; y++)
            {
                var dy = layerCanvasY + y;
                if (dy < 0 || dy >= canvasH) continue;

                for (var x = 0; x < layer.Width; x++)
                {
                    var si = (y * layer.Width + x) * 4;
                    if (si + 3 >= dataLength) continue;

                    var srcA = data[si + 3];
                    if (srcA == 0) continue;

                    var dx = layerCanvasX + x;
                    if (dx < 0 || dx >= canvasW) continue;

                    BlendOver(dst, (dy * canvasW + dx) * 4, data[si], data[si + 1], data[si + 2], srcA);
                }
            }
        }

        // Nothing refers to the rented buffers past this point. (An exception above just leaves
        // them to the garbage collector.)
        if (rentedAlpha != null)
            ArrayPool<byte>.Shared.Return(rentedAlpha);
        if (rentedBody != null)
            ArrayPool<byte>.Shared.Return(rentedBody);

        // 6. Adjust metrics based on composite canvas offset.
        // minX/minY represent how much the canvas extended beyond the original origin.
        var metrics = sourceGlyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX + minX,   // minX is negative when canvas extends left
            BearingY: metrics.BearingY - minY,    // minY is negative when canvas extends up
            Advance: metrics.Advance,
            Width: canvasW,
            Height: canvasH);

        return new RasterizedGlyph
        {
            Codepoint = sourceGlyph.Codepoint,
            GlyphIndex = sourceGlyph.GlyphIndex,
            BitmapData = dst,
            Width = canvasW,
            Height = canvasH,
            Pitch = canvasW * 4,
            Metrics = newMetrics,
            Format = PixelFormat.Rgba32
        };
    }

    /// <summary>
    /// Alpha-over of one straight-alpha source pixel onto <paramref name="dst"/> at <paramref name="di"/>.
    /// </summary>
    /// <remarks>
    /// The float expression below defines the result; the two shortcuts reproduce it exactly.
    /// An opaque source has <c>sA = 1</c>, so <c>outA = 1 + dA * 0 = 1</c> and every channel is
    /// <c>(src * 1 + d * dA * 0) / 1 = src</c>. An empty destination has <c>dA = 0</c>, so the
    /// destination term is +0 whatever its color and the result depends on the source channel
    /// and alpha alone: <see cref="OverEmpty"/> tabulates that same expression.
    /// </remarks>
    internal static void BlendOver(byte[] dst, int di, byte srcR, byte srcG, byte srcB, byte srcA)
    {
        if (srcA == 255)
        {
            dst[di + 0] = srcR;
            dst[di + 1] = srcG;
            dst[di + 2] = srcB;
            dst[di + 3] = 255;
            return;
        }

        var dstA = dst[di + 3];
        if (dstA == 0)
        {
            var row = OverEmpty(srcA);
            dst[di + 0] = row[srcR];
            dst[di + 1] = row[srcG];
            dst[di + 2] = row[srcB];
            dst[di + 3] = row[256];
            return;
        }

        BlendOverFloat(dst, di, srcR, srcG, srcB, srcA, dstA);
    }

    private static void BlendOverFloat(byte[] dst, int di, byte srcR, byte srcG, byte srcB, byte srcA, byte dstA)
    {
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

    /// <summary>Per source alpha (1-254): <see cref="OverEmpty"/> rows, built on first use.</summary>
    private static readonly byte[]?[] s_overEmpty = new byte[]?[256];

    /// <summary>
    /// <see cref="BlendOverFloat"/> of every source channel value (indices 0-255) and the alpha
    /// (index 256) over a transparent black pixel at source alpha <paramref name="srcA"/>
    /// (1-254). Rows are immutable once published; two threads building one at once build
    /// identical bytes.
    /// </summary>
    private static byte[] OverEmpty(byte srcA)
    {
        var row = Volatile.Read(ref s_overEmpty[srcA]);
        if (row != null)
            return row;

        row = new byte[257];
        var pixel = new byte[4];
        for (var c = 0; c < 256; c++)
        {
            pixel[0] = (byte)c;
            pixel[3] = 0;
            BlendOverFloat(pixel, 0, (byte)c, 0, 0, srcA, 0);
            row[c] = pixel[0];
            row[256] = pixel[3];
        }

        Volatile.Write(ref s_overEmpty[srcA], row);
        return row;
    }

    /// <summary>
    /// Extracts the alpha channel of an RGBA glyph into the first width * height bytes of
    /// <paramref name="alpha"/>, one byte per pixel (0 where the glyph's data runs out).
    /// </summary>
    private static void ExtractAlpha(RasterizedGlyph glyph, byte[] alpha)
    {
        var w = glyph.Width;
        var h = glyph.Height;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcIdx = y * glyph.Pitch + x * 4 + 3;
                alpha[y * w + x] = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
            }
        }
    }

    /// <summary>
    /// Creates a default body layer from the alpha mask, tinted with the supplied fill color.
    /// Used when effects like outline or shadow are present but no gradient/body effect was specified.
    /// The glyph's rasterized alpha is the mask; <paramref name="fillR"/>/<paramref name="fillG"/>/
    /// <paramref name="fillB"/> become the body RGB and <paramref name="fillA"/> multiplies the glyph alpha.
    /// The default (255,255,255,255) reproduces the legacy opaque-white body exactly.
    /// </summary>
    /// <param name="rgba">Receives the layer's pixels; at least width * height * 4 bytes (only those are written).</param>
    /// <param name="alphaData">Per-pixel alpha mask (one byte per pixel) for the glyph.</param>
    /// <param name="width">Mask width in pixels.</param>
    /// <param name="height">Mask height in pixels.</param>
    /// <param name="pitch">Row stride of <paramref name="alphaData"/> in bytes.</param>
    /// <param name="sourceGlyph">The source glyph; its original RGBA colors are preserved when the source format is RGBA (e.g. color fonts).</param>
    /// <param name="fillR">Body fill red channel.</param>
    /// <param name="fillG">Body fill green channel.</param>
    /// <param name="fillB">Body fill blue channel.</param>
    /// <param name="fillA">Body fill alpha; multiplies the glyph mask alpha. <c>255</c> is a bit-exact pass-through of the original alpha.</param>
    private static GlyphLayer CreateDefaultBodyLayer(byte[] rgba, byte[] alphaData, int width, int height, int pitch, RasterizedGlyph sourceGlyph,
        byte fillR = 255, byte fillG = 255, byte fillB = 255, byte fillA = 255)
    {
        // If the source was RGBA (e.g., color font), preserve the original colors; pixels past
        // the end of its data stay transparent.
        if (sourceGlyph.Format == PixelFormat.Rgba32)
        {
            Array.Clear(rgba, 0, width * height * 4);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var si = y * sourceGlyph.Pitch + x * 4;
                    var di = (y * width + x) * 4;
                    if (si + 3 < sourceGlyph.BitmapData.Length)
                    {
                        rgba[di + 0] = sourceGlyph.BitmapData[si + 0];
                        rgba[di + 1] = sourceGlyph.BitmapData[si + 1];
                        rgba[di + 2] = sourceGlyph.BitmapData[si + 2];
                        rgba[di + 3] = sourceGlyph.BitmapData[si + 3];
                    }
                }
            }
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = y * pitch + x;
                    var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;
                    var di = (y * width + x) * 4;
                    rgba[di + 0] = fillR;
                    rgba[di + 1] = fillG;
                    rgba[di + 2] = fillB;
                    // Multiply the glyph mask alpha by the fill alpha. With fillA == 255 this is a
                    // bit-exact pass-through of the original alpha (alpha * 255 / 255 == alpha).
                    rgba[di + 3] = fillA == 255 ? alpha : (byte)(alpha * fillA / 255);
                }
            }
        }

        return new GlyphLayer(rgba, width, height, OffsetX: 0, OffsetY: 0, ZOrder: 2);
    }

}
