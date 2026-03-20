using Bmfontier.Font.Models;

namespace Bmfontier.Rasterizer;

/// <summary>
/// Generates all layers from effects and composites them back-to-front
/// into a single RGBA glyph.
/// </summary>
internal static class GlyphCompositor
{
    /// <summary>
    /// Generates all layers from effects and composites them back-to-front.
    /// </summary>
    public static RasterizedGlyph Composite(
        RasterizedGlyph sourceGlyph,
        IReadOnlyList<IGlyphEffect> effects)
    {
        if (effects.Count == 0)
            return sourceGlyph;

        if (sourceGlyph.Width == 0 || sourceGlyph.Height == 0 || sourceGlyph.BitmapData.Length == 0)
            return sourceGlyph;

        // 1. Extract alpha from source (handle both grayscale and RGBA).
        var srcW = sourceGlyph.Width;
        var srcH = sourceGlyph.Height;
        var alphaData = ExtractAlpha(sourceGlyph);
        var alphaPitch = srcW; // one byte per pixel in the extracted alpha

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

        // Generate shadow from the outline silhouette (if outline exists),
        // otherwise from the raw glyph alpha.
        if (shadowEffect != null)
        {
            if (outlineLayer != null)
            {
                // Extract alpha from the outline layer to use as the shadow source.
                var outlineAlpha = new byte[outlineLayer.Width * outlineLayer.Height];
                for (var i = 0; i < outlineAlpha.Length; i++)
                    outlineAlpha[i] = outlineLayer.RgbaData[i * 4 + 3];

                // Generate shadow from the outlined shape, adjusting metrics for the outline offset.
                var outlineMetrics = new Font.Models.GlyphMetrics(
                    BearingX: sourceGlyph.Metrics.BearingX + outlineLayer.OffsetX,
                    BearingY: sourceGlyph.Metrics.BearingY - outlineLayer.OffsetY,
                    Advance: sourceGlyph.Metrics.Advance,
                    Width: outlineLayer.Width,
                    Height: outlineLayer.Height);

                var shadowLayer = shadowEffect.Generate(
                    outlineAlpha, outlineLayer.Width, outlineLayer.Height,
                    outlineLayer.Width, outlineMetrics);

                // Adjust shadow offset to account for the outline's own offset.
                layers.Add(shadowLayer with
                {
                    OffsetX = shadowLayer.OffsetX + outlineLayer.OffsetX,
                    OffsetY = shadowLayer.OffsetY + outlineLayer.OffsetY
                });
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
            var bodyLayer = CreateDefaultBodyLayer(alphaData, srcW, srcH, alphaPitch, sourceGlyph);
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

            for (var y = 0; y < layer.Height; y++)
            {
                for (var x = 0; x < layer.Width; x++)
                {
                    var si = (y * layer.Width + x) * 4;
                    if (si + 3 >= layer.RgbaData.Length) continue;

                    var srcR = layer.RgbaData[si];
                    var srcG = layer.RgbaData[si + 1];
                    var srcB = layer.RgbaData[si + 2];
                    var srcA = layer.RgbaData[si + 3];

                    if (srcA == 0) continue;

                    var dx = layerCanvasX + x;
                    var dy = layerCanvasY + y;
                    if (dx < 0 || dx >= canvasW || dy < 0 || dy >= canvasH) continue;

                    var di = (dy * canvasW + dx) * 4;

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
        }

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
    /// Extracts the alpha channel from a glyph, handling both grayscale and RGBA formats.
    /// Returns a byte array with one byte per pixel.
    /// </summary>
    private static byte[] ExtractAlpha(RasterizedGlyph glyph)
    {
        var w = glyph.Width;
        var h = glyph.Height;
        var alpha = new byte[w * h];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (glyph.Format == PixelFormat.Rgba32)
                {
                    var srcIdx = y * glyph.Pitch + x * 4 + 3;
                    alpha[y * w + x] = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                }
                else
                {
                    var srcIdx = y * glyph.Pitch + x;
                    alpha[y * w + x] = srcIdx < glyph.BitmapData.Length ? glyph.BitmapData[srcIdx] : (byte)0;
                }
            }
        }

        return alpha;
    }

    /// <summary>
    /// Creates a default white body layer from the alpha mask.
    /// Used when effects like outline or shadow are present but no gradient/body effect was specified.
    /// </summary>
    private static GlyphLayer CreateDefaultBodyLayer(byte[] alphaData, int width, int height, int pitch, RasterizedGlyph sourceGlyph)
    {
        var rgba = new byte[width * height * 4];

        // If the source was RGBA (e.g., color font), preserve the original colors.
        if (sourceGlyph.Format == PixelFormat.Rgba32)
        {
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
                    rgba[di + 0] = 255;
                    rgba[di + 1] = 255;
                    rgba[di + 2] = 255;
                    rgba[di + 3] = alpha;
                }
            }
        }

        return new GlyphLayer(rgba, width, height, OffsetX: 0, OffsetY: 0, ZOrder: 2);
    }
}
