using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Color;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Renders a COLR version 1 paint graph (<see cref="ColrGlyphGraph"/>) to straight-alpha RGBA,
/// for <see cref="ColorLayerRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Canvas: the bounds of the graph's clip regions — the glyph's clip box when it has one (the
/// reader wraps the root in it), otherwise the union of its <c>PaintGlyph</c> outlines as
/// transformed, combined per composite mode as the spec's boundedness rules say. An unbounded
/// graph (a fill with no clip) renders nothing. An untransformed glyph clip contributes the box
/// the pipeline gives that glyph, so a one-glyph graph is exactly as big as the plain glyph.
/// </para>
/// <para>
/// Every node renders to a canvas-sized premultiplied, linear-light surface (null when it drew
/// nothing); a clip multiplies its child's surface by the clip's anti-aliased coverage after the
/// child is complete, so nested clips and composites never double-apply edge coverage.
/// Gradients are sampled once at each pixel center, mapped back into the paint's font-unit
/// space. Linear light is what the spec requires for gradient interpolation and compositing;
/// the result is converted back to 8-bit sRGB, un-premultiplied.
/// </para>
/// <para>
/// Glyph clips go through the face pipeline (decode, transforms, bounds, scale/hint), and the
/// paint transform is applied to the prepared outline in its own coordinate space, conjugated
/// through its <see cref="PreparedGlyph.ToPixels"/> — so the result is right whether or not a
/// hint stage moved the outline into pixel space. Clip boxes get the same synthetic styles
/// (bold, italic) as the glyph clips they bound.
/// </para>
/// </remarks>
internal sealed class PaintGraphRenderer
{
    private readonly GlyphPipeline _pipeline;

    /// <summary>Creates a renderer over a face's pipeline, which prepares the clip glyphs.</summary>
    public PaintGraphRenderer(GlyphPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <summary>The pixel box <see cref="Render"/> fills; empty when the graph draws nothing.</summary>
    public GlyphBox Measure(ColrGlyphGraph graph, int baseGlyph, RasterOptions options) =>
        new Session(_pipeline, baseGlyph, options).Layout(graph);

    /// <summary>Renders the graph into its box as straight RGBA.</summary>
    public (GlyphBox Box, byte[] Pixels) Render(ColrGlyphGraph graph, int baseGlyph, RasterOptions options)
    {
        var session = new Session(_pipeline, baseGlyph, options);
        var box = session.Layout(graph);
        return (box, box.IsEmpty ? [] : session.Render(graph.Root!, box));
    }

    /// <summary>An axis-aligned rectangle in pixel space (Y down).</summary>
    private readonly record struct PixelRect(double MinX, double MinY, double MaxX, double MaxY)
    {
        public static PixelRect? Union(PixelRect? a, PixelRect? b) =>
            a is not { } x ? b : b is not { } y ? a
                : new PixelRect(Math.Min(x.MinX, y.MinX), Math.Min(x.MinY, y.MinY), Math.Max(x.MaxX, y.MaxX), Math.Max(x.MaxY, y.MaxY));

        public static PixelRect? Intersect(PixelRect? a, PixelRect? b)
        {
            if (a is not { } x || b is not { } y)
                return null;
            var r = new PixelRect(Math.Max(x.MinX, y.MinX), Math.Max(x.MinY, y.MinY), Math.Min(x.MaxX, y.MaxX), Math.Min(x.MaxY, y.MaxY));
            return r.MinX < r.MaxX && r.MinY < r.MaxY ? r : null;
        }
    }

    /// <summary>The bounds of a sub-graph: whether it is bounded at all, and where it can draw.</summary>
    private readonly record struct Extent(bool Bounded, PixelRect? Rect)
    {
        public static readonly Extent Nothing = new(true, null);
        public static readonly Extent Unbounded = new(false, null);
    }

    /// <summary>State for one measure or render of one glyph.</summary>
    private sealed class Session
    {
        private readonly GlyphPipeline _pipeline;
        private readonly int _baseGlyph;
        private readonly RasterOptions _options;
        private readonly IReadOnlyList<IGlyphTransform> _transforms;
        private readonly OutlineTransform _fontToPixels;
        private readonly Affine2D _fontToPixelMatrix;
        private readonly Dictionary<int, PreparedGlyph> _prepared = [];
        private GlyphBox _box;

        public Session(GlyphPipeline pipeline, int baseGlyph, RasterOptions options)
        {
            _pipeline = pipeline;
            _baseGlyph = baseGlyph;
            _options = options;
            _transforms = GlyphTransforms.For(options, pipeline.UnitsPerEm);
            _fontToPixels = pipeline.FontUnitsToPixels(options);
            _fontToPixelMatrix = ToMatrix(_fontToPixels);
        }

        // ---- Layout ----------------------------------------------------------------------

        public GlyphBox Layout(ColrGlyphGraph graph)
        {
            if (graph.Root is null)
                return default;

            var extent = Bounds(graph.Root, Affine2D.Identity);
            if (!extent.Bounded || extent.Rect is not { } rect)
                return default;

            int left = (int)Math.Floor(rect.MinX);
            int top = (int)Math.Floor(rect.MinY);
            int right = (int)Math.Ceiling(rect.MaxX);
            int bottom = (int)Math.Ceiling(rect.MaxY);
            return right > left && bottom > top ? new GlyphBox(left, top, right - left, bottom - top) : default;
        }

        private Extent Bounds(ColrPaint? paint, Affine2D ctm)
        {
            switch (paint)
            {
                case null:
                    return Extent.Nothing;

                case SolidPaint or GradientPaint:
                    return Extent.Unbounded;

                case GlyphClipPaint glyph:
                    return new Extent(true, GlyphRect(glyph.GlyphIndex, ctm));

                case RectClipPaint rect:
                    return new Extent(true, Bound(ClipRectangle(rect, ctm), _fontToPixels));

                case TransformPaint transform:
                    return Bounds(transform.Child, ctm * transform.Transform);

                case LayersPaint layers:
                {
                    bool bounded = true;
                    PixelRect? union = null;
                    foreach (var layer in layers.Layers)
                    {
                        var e = Bounds(layer, ctm);
                        bounded &= e.Bounded;
                        union = PixelRect.Union(union, e.Rect);
                    }

                    return new Extent(bounded, union);
                }

                case CompositePaint composite:
                {
                    var s = Bounds(composite.Source, ctm);
                    var b = Bounds(composite.Backdrop, ctm);
                    switch (composite.Mode)
                    {
                        case CompositeMode.Clear:
                            return Extent.Nothing;
                        case CompositeMode.Source or CompositeMode.SourceOut:
                            return s;
                        case CompositeMode.Destination or CompositeMode.DestinationOut:
                            return b;
                        case CompositeMode.SourceIn or CompositeMode.DestinationIn:
                            if (s.Bounded && b.Bounded)
                                return new Extent(true, PixelRect.Intersect(s.Rect, b.Rect));
                            return s.Bounded ? s : b;
                        default:
                            return new Extent(s.Bounded && b.Bounded, PixelRect.Union(s.Rect, b.Rect));
                    }
                }

                default:
                    return Extent.Nothing;
            }
        }

        /// <summary>Where a glyph clip under <paramref name="ctm"/> can draw, in pixels.</summary>
        private PixelRect? GlyphRect(int glyphIndex, Affine2D ctm)
        {
            var glyph = Prepare(glyphIndex);
            if (glyph.IsBlank)
                return null;

            if (ctm == Affine2D.Identity)
            {
                var b = glyph.Box;
                return new PixelRect(b.MinX, b.MinY, b.MinX + b.Width, b.MinY + b.Height);
            }

            return Bound(TransformOutline(glyph, ctm), glyph.ToPixels);
        }

        private static PixelRect? Bound(GlyphOutline outline, OutlineTransform toPixels)
        {
            if (outline.IsEmpty)
                return null;

            var tight = OutlineBounds.Tight(outline);
            double x0 = toPixels.ToPixelX(tight.XMin), x1 = toPixels.ToPixelX(tight.XMax);
            double y0 = toPixels.ToPixelY(tight.YMin), y1 = toPixels.ToPixelY(tight.YMax);
            var rect = new PixelRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            return rect.MinX < rect.MaxX && rect.MinY < rect.MaxY ? rect : null;
        }

        // ---- Rendering -------------------------------------------------------------------

        public byte[] Render(ColrPaint root, GlyphBox box)
        {
            _box = box;
            var surface = Paint(root, Affine2D.Identity);

            var pixels = new byte[box.Width * box.Height * 4];
            if (surface is null)
                return pixels;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                float a = Math.Clamp(surface[i + 3], 0f, 1f);
                byte a8 = (byte)MathF.Round(a * 255f);
                if (a8 == 0)
                    continue;

                pixels[i] = SrgbTransfer.Encode(surface[i] / a);
                pixels[i + 1] = SrgbTransfer.Encode(surface[i + 1] / a);
                pixels[i + 2] = SrgbTransfer.Encode(surface[i + 2] / a);
                pixels[i + 3] = a8;
            }

            return pixels;
        }

        /// <summary>Renders a node to a canvas-sized premultiplied surface; null when it draws nothing.</summary>
        private float[]? Paint(ColrPaint? paint, Affine2D ctm)
        {
            switch (paint)
            {
                case SolidPaint solid:
                    return Fill(solid.Color);

                case GradientPaint gradient:
                    return Gradient(gradient, ctm);

                case GlyphClipPaint glyph:
                {
                    var prepared = Prepare(glyph.GlyphIndex);
                    if (prepared.IsBlank)
                        return null;
                    var outline = ctm == Affine2D.Identity ? prepared.Outline : TransformOutline(prepared, ctm);
                    return Clip(Coverage(outline, prepared.ToPixels), glyph.Child, ctm);
                }

                case RectClipPaint rect:
                    return Clip(Coverage(ClipRectangle(rect, ctm), _fontToPixels), rect.Child, ctm);

                case TransformPaint transform:
                    return Paint(transform.Child, ctm * transform.Transform);

                case LayersPaint layers:
                {
                    float[]? result = null;
                    foreach (var layer in layers.Layers)
                        result = Composite(CompositeMode.SourceOver, Paint(layer, ctm), result);
                    return result;
                }

                case CompositePaint composite:
                {
                    var backdrop = Paint(composite.Backdrop, ctm);
                    var source = Paint(composite.Source, ctm);
                    return Composite(composite.Mode, source, backdrop);
                }

                default:
                    return null;
            }
        }

        private float[]? Clip(byte[]? coverage, ColrPaint child, Affine2D ctm)
        {
            if (coverage is null)
                return null;

            var surface = Paint(child, ctm);
            if (surface is null)
                return null;

            for (int p = 0, i = 0; p < coverage.Length; p++, i += 4)
            {
                float k = coverage[p] / 255f;
                surface[i] *= k;
                surface[i + 1] *= k;
                surface[i + 2] *= k;
                surface[i + 3] *= k;
            }

            return surface;
        }

        /// <summary>Coverage of an outline over the canvas (thresholded, with dropout control, under
        /// <see cref="AntiAliasMode.None"/>); null when it covers nothing.</summary>
        private byte[]? Coverage(GlyphOutline outline, OutlineTransform toPixels)
        {
            if (outline.IsEmpty)
                return null;

            var edges = OutlineFlattener.Flatten(outline, toPixels, includeHorizontal: _options.AntiAlias == AntiAliasMode.None);
            var coverage = ScanlineRasterizer.Rasterize(edges, _box.Width, _box.Height, _box.MinX, _box.MinY, _options.AntiAlias).Bitmap;
            return Array.Exists(coverage, c => c != 0) ? coverage : null;
        }

        private float[] Fill(LinearColor color)
        {
            var surface = new float[_box.Width * _box.Height * 4];
            for (int i = 0; i < surface.Length; i += 4)
            {
                surface[i] = color.R;
                surface[i + 1] = color.G;
                surface[i + 2] = color.B;
                surface[i + 3] = color.A;
            }

            return surface;
        }

        private static float[]? Composite(CompositeMode mode, float[]? source, float[]? backdrop)
        {
            if (mode == CompositeMode.SourceOver && (source is null || backdrop is null))
                return source ?? backdrop;
            if (source is null && backdrop is null)
                return null;

            var result = new float[(source ?? backdrop)!.Length];
            for (int i = 0; i < result.Length; i += 4)
            {
                var s = source is null ? default : new LinearColor(source[i], source[i + 1], source[i + 2], source[i + 3]);
                var b = backdrop is null ? default : new LinearColor(backdrop[i], backdrop[i + 1], backdrop[i + 2], backdrop[i + 3]);
                var o = CompositeOps.Apply(mode, s, b);
                result[i] = o.R;
                result[i + 1] = o.G;
                result[i + 2] = o.B;
                result[i + 3] = o.A;
            }

            return result;
        }

        // ---- Gradients -------------------------------------------------------------------

        private float[]? Gradient(GradientPaint gradient, Affine2D ctm)
        {
            if (!ColorLineSampler.TryCreate(gradient.Stops, gradient.Extend, out var line))
                return null;

            // Pixel center → font units → the gradient's own space.
            if (!(_fontToPixelMatrix * ctm).TryInvert(out var toPaint))
                return null;

            Func<double, double, double?>? parameter = gradient switch
            {
                LinearGradientPaint linear => LinearParameter(linear),
                RadialGradientPaint radial => RadialParameter(radial),
                SweepGradientPaint sweep => SweepParameter(sweep, line),
                _ => null,
            };
            if (parameter is null)
                return null;

            var surface = new float[_box.Width * _box.Height * 4];
            int i = 0;
            for (int row = 0; row < _box.Height; row++)
            {
                double py = _box.MinY + row + 0.5;
                for (int col = 0; col < _box.Width; col++, i += 4)
                {
                    var (x, y) = toPaint.Apply(_box.MinX + col + 0.5, py);
                    if (parameter(x, y) is not { } t)
                        continue;

                    var c = line.Sample(t);
                    surface[i] = c.R;
                    surface[i + 1] = c.G;
                    surface[i + 2] = c.B;
                    surface[i + 3] = c.A;
                }
            }

            return surface;
        }

        /// <summary>
        /// t at a point: the line through it parallel to p0p2 meets p0p1 at p0 + t·(p1 − p0).
        /// Null (draw nothing) when p1 or p2 coincides with p0 or p0p2 is (nearly) parallel to p0p1.
        /// </summary>
        private static Func<double, double, double?>? LinearParameter(LinearGradientPaint g)
        {
            double ax = g.X1 - g.X0, ay = g.Y1 - g.Y0;   // p0 → p1
            double dx = g.X2 - g.X0, dy = g.Y2 - g.Y0;   // p0 → p2
            double denominator = (ax * dy) - (ay * dx);
            double lengths = Math.Sqrt(((ax * ax) + (ay * ay)) * ((dx * dx) + (dy * dy)));
            if (lengths == 0 || Math.Abs(denominator) <= lengths * 1e-6)
                return null;

            return (x, y) => ((((x - g.X0) * dy) - ((y - g.Y0) * dx)) / denominator);
        }

        /// <summary>
        /// Two-circle gradient: t is the largest ω with r(ω) ≥ 0 whose circle passes through
        /// the point, solving |p − c(ω)| = r(ω); points on no such circle are not painted.
        /// </summary>
        private static Func<double, double, double?>? RadialParameter(RadialGradientPaint g)
        {
            double cdx = g.X1 - g.X0, cdy = g.Y1 - g.Y0, dr = g.R1 - g.R0;
            if ((cdx == 0 && cdy == 0 && dr == 0) || (g.R0 == 0 && g.R1 == 0))
                return null;

            double a = (cdx * cdx) + (cdy * cdy) - (dr * dr);
            double scale = (cdx * cdx) + (cdy * cdy) + (dr * dr);
            bool linear = Math.Abs(a) <= scale * 1e-12;

            return (x, y) =>
            {
                double px = x - g.X0, py = y - g.Y0;
                double b = (px * cdx) + (py * cdy) + (g.R0 * dr);
                double c = (px * px) + (py * py) - (g.R0 * g.R0);

                if (linear)
                {
                    if (b == 0)
                        return null;
                    double w = c / (2 * b);
                    return g.R0 + (w * dr) >= 0 ? w : null;
                }

                double discriminant = (b * b) - (a * c);
                if (discriminant < 0)
                    return null;
                double root = Math.Sqrt(discriminant);
                double w1 = (b + root) / a, w2 = (b - root) / a;
                double high = Math.Max(w1, w2), low = Math.Min(w1, w2);
                if (g.R0 + (high * dr) >= 0)
                    return high;
                return g.R0 + (low * dr) >= 0 ? low : null;
            };
        }

        /// <summary>
        /// t = (angle − start) / (end − start), with the angle counter-clockwise from +x in
        /// [0°, 360°). Equal angles draw nothing unless padded, when they split the plane.
        /// </summary>
        private static Func<double, double, double?>? SweepParameter(SweepGradientPaint g, ColorLineSampler line)
        {
            double start = g.StartDegrees, end = g.EndDegrees;
            bool equal = start == end;
            if (equal && line.Extend != ColorLineExtend.Pad)
                return null;

            return (x, y) =>
            {
                double angle = Math.Atan2(y - g.CenterY, x - g.CenterX) * (180 / Math.PI);
                if (angle < 0)
                    angle += 360;
                if (equal)
                    return angle < start ? double.NegativeInfinity : double.PositiveInfinity;
                return (angle - start) / (end - start);
            };
        }

        // ---- Geometry --------------------------------------------------------------------

        private PreparedGlyph Prepare(int glyphIndex)
        {
            if (_prepared.TryGetValue(glyphIndex, out var cached))
                return cached;

            PreparedGlyph glyph;
            try
            {
                glyph = _pipeline.Prepare(glyphIndex, _options, _transforms);
            }
            catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
            {
                throw new RasterizationException($"Color glyph {_baseGlyph} clips to glyph {glyphIndex}, which is malformed: {ex.Message}", ex);
            }

            _prepared[glyphIndex] = glyph;
            return glyph;
        }

        /// <summary>
        /// The prepared outline moved by the paint transform <paramref name="ctm"/> (font units),
        /// expressed in the outline's own space: ToPixels⁻¹ · F · ctm · F⁻¹ · ToPixels, where F is
        /// the font-unit to pixel mapping. With an unhinted outline ToPixels = F and this is ctm.
        /// </summary>
        private GlyphOutline TransformOutline(PreparedGlyph glyph, Affine2D ctm)
        {
            var toPixels = ToMatrix(glyph.ToPixels);
            if (!toPixels.TryInvert(out var fromPixels) || !_fontToPixelMatrix.TryInvert(out var pixelsToFont))
                return GlyphOutline.Empty;

            return Transform(glyph.Outline, fromPixels * _fontToPixelMatrix * ctm * pixelsToFont * toPixels);
        }

        /// <summary>
        /// A clip rectangle (font units), styled like the glyph clips and then moved by
        /// <paramref name="ctm"/>, as an outline in font units.
        /// </summary>
        /// <remarks>
        /// A clip box bounds the glyph's outlines, so the synthetic styles that grow or slant those
        /// outlines grow or slant it too; otherwise it would cut the styled ink off.
        /// </remarks>
        private GlyphOutline ClipRectangle(RectClipPaint rect, Affine2D ctm)
        {
            if (!(rect.XMin < rect.XMax && rect.YMin < rect.YMax))
                return GlyphOutline.Empty;

            OutlineCommand[] commands =
            [
                OutlineCommand.Move((float)rect.XMin, (float)rect.YMin),
                OutlineCommand.Line((float)rect.XMax, (float)rect.YMin),
                OutlineCommand.Line((float)rect.XMax, (float)rect.YMax),
                OutlineCommand.Line((float)rect.XMin, (float)rect.YMax),
                OutlineCommand.CloseAt((float)rect.XMin, (float)rect.YMin),
            ];

            var styled = new DecodedGlyph(GlyphOutline.FromCommands(commands), 0f);
            foreach (var transform in _transforms)
                styled = transform.Apply(styled);

            return Transform(styled.Outline, ctm);
        }

        private static GlyphOutline Transform(GlyphOutline outline, Affine2D m)
        {
            if (outline.IsEmpty)
                return outline;

            // Béziers are affine-invariant: mapping every control point maps the curve exactly.
            var commands = new OutlineCommand[outline.Commands.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                var c = outline.Commands[i];
                var (ex, ey) = m.Apply(c.EndX, c.EndY);
                if (c.Type == OutlineCommandType.CubicTo)
                {
                    var (c1x, c1y) = m.Apply(c.Ctrl1X, c.Ctrl1Y);
                    var (c2x, c2y) = m.Apply(c.Ctrl2X, c.Ctrl2Y);
                    commands[i] = c with
                    {
                        EndX = (float)ex, EndY = (float)ey,
                        Ctrl1X = (float)c1x, Ctrl1Y = (float)c1y, Ctrl2X = (float)c2x, Ctrl2Y = (float)c2y,
                    };
                }
                else
                {
                    commands[i] = c with { EndX = (float)ex, EndY = (float)ey };
                }
            }

            return GlyphOutline.FromCommands(commands);
        }

        /// <summary><see cref="OutlineTransform"/> as a matrix: x' = s·x, y' = ascent − s·y.</summary>
        private static Affine2D ToMatrix(OutlineTransform t) => new(t.Scale, 0, 0, -t.Scale, 0, t.AscentPixels);
    }
}
