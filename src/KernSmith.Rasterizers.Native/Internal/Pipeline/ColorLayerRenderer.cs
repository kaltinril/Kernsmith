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
internal sealed class ColorLayerRenderer : IGlyphRenderer, IDrawsWithoutOutline
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
    public bool DrawsWithoutOutline(int glyphIndex) =>
        HasLayers(glyphIndex) || (_fallback is IDrawsWithoutOutline inner && inner.DrawsWithoutOutline(glyphIndex));

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        // A None box depends on which pixels the threshold keeps, so measuring means drawing.
        if (options.AntiAlias == AntiAliasMode.None && HasLayers(glyph.GlyphIndex))
            return Metrics(Draw(glyph.GlyphIndex, options).Box, glyph.Advance);

        if (_colorGlyphs.TryGetPaintGraph(glyph.GlyphIndex, out var graph))
            return Metrics(_paintGraphs.Measure(graph, glyph.GlyphIndex, options), glyph.Advance);

        if (!_colorGlyphs.HasLayers(glyph.GlyphIndex))
            return _fallback.Measure(glyph, options);

        var (_, box) = Layout(glyph.GlyphIndex, options);
        return Metrics(box, glyph.Advance);
    }

    /// <summary>
    /// <see cref="Measure"/> for a version 0 layered glyph, decoding only what the box shortcut
    /// cannot measure: the advance from the base glyph's <see cref="GlyphPipeline.TryPrepareBox"/>
    /// (its outline is never drawn) and the box from the union of its layers', each equal to
    /// what <see cref="GlyphPipeline.Prepare"/> gives it.
    /// </summary>
    /// <returns>False — measure the prepared glyph instead — for a glyph with a version 1 record
    /// (checked without reading its graph, which <see cref="Measure"/> then reads once), a glyph
    /// without layers, <see cref="AntiAliasMode.None"/> (its box comes from the pixels), or a
    /// base glyph that cannot be measured from its box.</returns>
    /// <exception cref="RasterizationException">If a layer's data is malformed.</exception>
    /// <exception cref="FontFormatException">If the base glyph's data is malformed.</exception>
    public bool TryMeasureFromBoxes(int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms, out GlyphMetrics metrics)
    {
        metrics = default!;
        if (options.AntiAlias == AntiAliasMode.None || !_colorGlyphs.HasLayers(glyphIndex) || _colorGlyphs.HasPaintGraphRecord(glyphIndex)
            || !_pipeline.TryPrepareBox(glyphIndex, options, transforms, out _, out int advance))
            return false;

        var colorLayers = new List<ColorLayer>();
        _colorGlyphs.GetLayers(glyphIndex, colorLayers);
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var layer in colorLayers)
        {
            // A layer the shortcut cannot measure (a composite, say) is prepared in full; either
            // way its box is the one Layout would union, and an empty box is a blank layer.
            GlyphBox b;
            try
            {
                if (!_pipeline.TryPrepareBox(layer.GlyphIndex, options, transforms, out b, out _))
                {
                    var prepared = _pipeline.Prepare(layer.GlyphIndex, options, transforms);
                    b = prepared.IsBlank ? default : prepared.Box;
                }
            }
            catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
            {
                throw new RasterizationException($"Color layer glyph {layer.GlyphIndex} of glyph {glyphIndex} is malformed: {ex.Message}", ex);
            }

            if (b.IsEmpty)
                continue;

            left = Math.Min(left, b.MinX);
            top = Math.Min(top, b.MinY);
            right = Math.Max(right, b.MinX + b.Width);
            bottom = Math.Max(bottom, b.MinY + b.Height);
        }

        if (right == int.MinValue)
        {
            metrics = Metrics(default, advance);
            return true;
        }

        // The same limit Layout enforces on the same union.
        GlyphLimits.EnsureGlyphBox(glyphIndex, (long)right - left, (long)bottom - top, options);
        metrics = Metrics(new GlyphBox(left, top, right - left, bottom - top), advance);
        return true;
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        if (!HasLayers(glyphIndex))
            return _fallback.Render(codepoint, glyphIndex, glyph, options);

        var (box, pixels) = Draw(glyphIndex, options);
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

    /// <summary>Draws a paint graph or version 0 layers; under <see cref="AntiAliasMode.None"/>
    /// cropped to the ink as plain coverage is.</summary>
    private (GlyphBox Box, byte[] Pixels) Draw(int glyphIndex, RasterOptions options)
    {
        GlyphBox box;
        byte[] pixels;
        if (_colorGlyphs.TryGetPaintGraph(glyphIndex, out var graph))
        {
            (box, pixels) = _paintGraphs.Render(graph, glyphIndex, options);
        }
        else
        {
            (var layers, box) = Layout(glyphIndex, options);
            pixels = box.IsEmpty ? [] : Composite(layers, box, options.AntiAlias);
        }

        return options.AntiAlias == AntiAliasMode.None ? CropToInk(box, pixels) : (box, pixels);
    }

    /// <summary>
    /// Drops fully transparent edge rows and columns, as <see cref="ScanlineRasterizer.RasterizeMono"/>
    /// does for plain coverage, so a color glyph that paints its own outline keeps the same box.
    /// A fully transparent glyph keeps its box.
    /// </summary>
    private static (GlyphBox Box, byte[] Pixels) CropToInk(GlyphBox box, byte[] rgba)
    {
        if (box.IsEmpty)
            return (box, rgba);

        int width = box.Width, height = box.Height;
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (rgba[(((y * width) + x) * 4) + 3] == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0 || (left == 0 && top == 0 && right == width - 1 && bottom == height - 1))
            return (box, rgba);

        int croppedWidth = right - left + 1, croppedHeight = bottom - top + 1;
        var cropped = new byte[croppedWidth * croppedHeight * 4];
        for (int row = 0; row < croppedHeight; row++)
            Array.Copy(rgba, (((top + row) * width) + left) * 4, cropped, row * croppedWidth * 4, croppedWidth * 4);

        return (new GlyphBox(box.MinX + left, box.MinY + top, croppedWidth, croppedHeight), cropped);
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

        if (layers.Count == 0)
            return (layers, default);

        // Each layer passed the pipeline's box check, but layers far apart can still span too much.
        GlyphLimits.EnsureGlyphBox(glyphIndex, (long)right - left, (long)bottom - top, options);
        return (layers, new GlyphBox(left, top, right - left, bottom - top));
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
            byte[] coverage;
            using (var edges = OutlineFlattener.FlattenPooled(glyph.Outline, glyph.ToPixels, includeHorizontal: antiAlias == AntiAliasMode.None))
                coverage = ScanlineRasterizer.Rasterize(edges.Edges, box.Width, box.Height, box.MinX, box.MinY, antiAlias).Bitmap;

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
