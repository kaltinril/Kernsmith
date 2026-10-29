using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Color;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The color render mode for <c>COLR</c> glyphs. Version 1 paint graphs, which take precedence,
/// render through <see cref="PaintGraphRenderer"/>. For version 0 glyphs each layer glyph runs through the
/// normal pipeline (decode, transforms, bounds, scale/hint), its coverage is tinted with its
/// palette color, and the layers are composited bottom to top with source-over onto a canvas
/// sized to the union of the layer boxes. Glyphs without color layers go to the wrapped
/// renderer unchanged.
/// </summary>
/// <remarks>
/// Output is <see cref="PixelFormat.Rgba32"/> with straight (not premultiplied) alpha — what the
/// atlas builder, the image encoders and <c>AtlasPage.GetPremultipliedRgbaPixelData</c> assume.
/// Compositing runs premultiplied internally. Layers are always rasterized as coverage, honoring
/// <see cref="AntiAliasMode.None"/> per layer; whatever mode the wrapped renderer implements
/// (SDF, say) applies only to glyphs without layers, as with FreeType.
///
/// The base glyph supplies the advance only; its own outline is not drawn and may be empty, so
/// the rasterizer hands such glyphs here even when the base outline is blank (see
/// <see cref="HasLayers"/>).
/// </remarks>
internal sealed class ColorLayerRenderer : IGlyphRenderer
{
    private readonly ColorGlyphs _colorGlyphs;
    private readonly GlyphPipeline _pipeline;
    private readonly IGlyphRenderer _fallback;
    private readonly PaintGraphRenderer _paintGraphs;

    /// <summary>Wraps the renderer chosen for the request's other options.</summary>
    /// <param name="colorGlyphs">The face's resolved color layers and selected palette.</param>
    /// <param name="pipeline">The face's pipeline, used to prepare each layer glyph.</param>
    /// <param name="fallback">Renders glyphs that have no color layers.</param>
    public ColorLayerRenderer(ColorGlyphs colorGlyphs, GlyphPipeline pipeline, IGlyphRenderer fallback)
    {
        ArgumentNullException.ThrowIfNull(colorGlyphs);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(fallback);
        _colorGlyphs = colorGlyphs;
        _pipeline = pipeline;
        _fallback = fallback;
        _paintGraphs = new PaintGraphRenderer(pipeline);
    }

    /// <summary>True when the glyph renders from a paint graph or color layers rather than its own outline.</summary>
    public bool HasLayers(int glyphIndex) => _colorGlyphs.HasPaintGraph(glyphIndex) || _colorGlyphs.HasLayers(glyphIndex);

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        if (_colorGlyphs.TryGetPaintGraph(glyph.GlyphIndex, out var graph))
            return Metrics(_paintGraphs.Measure(graph, glyph.GlyphIndex, options), glyph.Advance);

        if (!_colorGlyphs.HasLayers(glyph.GlyphIndex))
            return _fallback.Measure(glyph, options);

        var (_, box) = Layout(glyph.GlyphIndex, options);
        return Metrics(box, glyph.Advance);
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        GlyphBox box;
        byte[] pixels;
        if (_colorGlyphs.TryGetPaintGraph(glyphIndex, out var graph))
        {
            (box, pixels) = _paintGraphs.Render(graph, glyphIndex, options);
        }
        else if (_colorGlyphs.HasLayers(glyphIndex))
        {
            (var layers, box) = Layout(glyphIndex, options);
            pixels = box.IsEmpty ? [] : Composite(layers, box, options.AntiAlias);
        }
        else
        {
            return _fallback.Render(codepoint, glyphIndex, glyph, options);
        }

        var metrics = Metrics(box, glyph.Advance);

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = pixels,
            Width = metrics.Width,
            Height = metrics.Height,
            Pitch = metrics.Width * 4,
            Metrics = metrics,
            Format = PixelFormat.Rgba32
        };
    }

    /// <summary>Prepares every non-blank layer and the union of their boxes.</summary>
    private (List<(PreparedGlyph Glyph, PaletteColor Color)> Layers, GlyphBox Box) Layout(int glyphIndex, RasterOptions options)
    {
        var colorLayers = new List<ColorLayer>();
        _colorGlyphs.GetLayers(glyphIndex, colorLayers);

        // The same synthetic styles as a plain glyph, applied to every layer before compositing.
        var transforms = GlyphTransforms.For(options, _pipeline.UnitsPerEm);
        var layers = new List<(PreparedGlyph, PaletteColor)>(colorLayers.Count);
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

        foreach (var layer in colorLayers)
        {
            var prepared = PrepareLayer(layer.GlyphIndex, glyphIndex, options, transforms);
            if (prepared.IsBlank)
                continue;

            var b = prepared.Box;
            left = Math.Min(left, b.MinX);
            top = Math.Min(top, b.MinY);
            right = Math.Max(right, b.MinX + b.Width);
            bottom = Math.Max(bottom, b.MinY + b.Height);
            layers.Add((prepared, layer.Color));
        }

        var box = layers.Count == 0 ? default : new GlyphBox(left, top, right - left, bottom - top);
        return (layers, box);
    }

    private PreparedGlyph PrepareLayer(int layerGlyph, int baseGlyph, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms)
    {
        try
        {
            return _pipeline.Prepare(layerGlyph, options, transforms);
        }
        catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
        {
            throw new RasterizationException($"Color layer glyph {layerGlyph} of glyph {baseGlyph} is malformed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Rasterizes each layer's coverage and composites it, source-over, onto the canvas;
    /// returns straight RGBA.
    /// </summary>
    private static byte[] Composite(List<(PreparedGlyph Glyph, PaletteColor Color)> layers, GlyphBox canvasBox, AntiAliasMode antiAlias)
    {
        int width = canvasBox.Width;
        var canvas = new float[width * canvasBox.Height * 4]; // premultiplied R, G, B, A in 0..1

        foreach (var (glyph, color) in layers)
        {
            var box = glyph.Box;
            var edges = OutlineFlattener.Flatten(glyph.Outline, glyph.ToPixels, includeHorizontal: antiAlias == AntiAliasMode.None);
            var coverage = ScanlineRasterizer.Rasterize(edges, box.Width, box.Height, box.MinX, box.MinY, antiAlias).Bitmap;

            float alpha = color.A / 255f;
            float red = color.R / 255f * alpha;
            float green = color.G / 255f * alpha;
            float blue = color.B / 255f * alpha;
            int offsetX = box.MinX - canvasBox.MinX;
            int offsetY = box.MinY - canvasBox.MinY;

            for (int row = 0; row < box.Height; row++)
            {
                int source = row * box.Width;
                int target = (((row + offsetY) * width) + offsetX) * 4;
                for (int col = 0; col < box.Width; col++, target += 4)
                {
                    byte c = coverage[source + col];
                    if (c == 0)
                        continue;

                    float k = c / 255f;
                    float remaining = 1f - (alpha * k);
                    canvas[target] = (red * k) + (canvas[target] * remaining);
                    canvas[target + 1] = (green * k) + (canvas[target + 1] * remaining);
                    canvas[target + 2] = (blue * k) + (canvas[target + 2] * remaining);
                    canvas[target + 3] = (alpha * k) + (canvas[target + 3] * remaining);
                }
            }
        }

        var pixels = new byte[canvas.Length];
        for (int i = 0; i < canvas.Length; i += 4)
        {
            float a = canvas[i + 3];
            byte a8 = ToByte(a);
            if (a8 == 0)
                continue;

            pixels[i] = ToByte(canvas[i] / a);
            pixels[i + 1] = ToByte(canvas[i + 1] / a);
            pixels[i + 2] = ToByte(canvas[i + 2] / a);
            pixels[i + 3] = a8;
        }

        return pixels;
    }

    private static byte ToByte(float unit) => (byte)Math.Clamp((int)MathF.Round(unit * 255f), 0, 255);

    private static GlyphMetrics Metrics(GlyphBox box, int advance) =>
        box.IsEmpty
            ? new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: advance, Width: 0, Height: 0)
            : new GlyphMetrics(BearingX: box.BearingX, BearingY: box.BearingY, Advance: advance, Width: box.Width, Height: box.Height);
}
