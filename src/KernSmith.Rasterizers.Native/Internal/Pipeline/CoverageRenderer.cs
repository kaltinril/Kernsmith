using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The default render mode: flattens the outline and fills it with the signed-area scanline
/// rasterizer into 8-bit coverage. <see cref="AntiAliasMode.None"/> thresholds it and crops the
/// glyph box to the ink (<see cref="ScanlineRasterizer.RasterizeMono"/>); Grayscale and Light
/// render as grayscale. <see cref="AntiAliasMode.Lcd"/> goes to <see cref="LcdRenderer"/>.
/// </summary>
/// <remarks>
/// A <see cref="AntiAliasMode.None"/> box depends on which pixels pass the threshold, so
/// <see cref="Measure"/> renders it too (into pooled scratch) to stay equal to <see cref="Render"/>,
/// and only the other modes measure from the box alone.
/// </remarks>
internal sealed class CoverageRenderer : IBoxMeasuringRenderer
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly CoverageRenderer Instance = new();

    private CoverageRenderer()
    {
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        var box = glyph.Box;
        if (options.AntiAlias != AntiAliasMode.None)
            return MeasureBox(glyph.GlyphIndex, box, glyph.Advance, options);

        using var edges = Edges(glyph, options);
        var ink = ScanlineRasterizer.MeasureMono(edges.Edges, box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY, MonoRenderingFor(glyph, options));
        return new(BearingX: ink.BearingX, BearingY: ink.BearingY, Advance: glyph.Advance, Width: ink.Width, Height: ink.Height);
    }

    /// <summary>
    /// How a bilevel request is rendered: <see cref="HintLevel.Normal"/>, whose fit puts stems
    /// on whole pixels, draws the rest of the glyph as strokes (<see cref="MonoStrokeRasterizer"/>)
    /// — while the fit runs, that is, below the taper's end, so large text is exactly unhinted
    /// output; every other level, and every size past the taper, keeps the 50% threshold with
    /// dropout control.
    /// </summary>
    internal static MonoRendering MonoRenderingFor(RasterOptions options) =>
        MonoRenderingAt(options, options.Size * options.Dpi / 72f);

    /// <summary>
    /// <see cref="MonoRenderingFor(RasterOptions)"/> for a prepared glyph: judged at the ppem
    /// the fit ran at (<see cref="PreparedGlyph.RenderPpem"/>), and a glyph the <c>Normal</c>
    /// stage left unfitted on the font's <c>gasp</c> say-so (<see cref="PreparedGlyph.FitWithheld"/>)
    /// keeps the threshold too, since the stroke renderer is for outlines whose stems the fit
    /// put on whole pixels.
    /// </summary>
    internal static MonoRendering MonoRenderingFor(PreparedGlyph glyph, RasterOptions options) =>
        glyph.FitWithheld ? MonoRendering.Threshold : MonoRenderingAt(options, glyph.RenderPpem(options));

    private static MonoRendering MonoRenderingAt(RasterOptions options, float ppem) =>
        GlyphHinter.EffectiveHintLevel(options) == HintLevel.Normal && NormalHinter.Strength(ppem) > 0f
            ? MonoRendering.Strokes
            : MonoRendering.Threshold;

    /// <inheritdoc />
    public bool MeasuresFromBox(RasterOptions options) => options.AntiAlias != AntiAliasMode.None;

    /// <inheritdoc />
    public GlyphMetrics MeasureBox(int glyphIndex, GlyphBox box, int advance, RasterOptions options) =>
        new(
            BearingX: box.BearingX,
            BearingY: box.BearingY,
            Advance: advance,
            Width: box.Width,
            Height: box.Height);

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        var box = glyph.Box;
        using var edges = Edges(glyph, options);
        var raster = options.AntiAlias == AntiAliasMode.None
            ? ScanlineRasterizer.RasterizeMono(edges.Edges, box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY, MonoRenderingFor(glyph, options))
            : ScanlineRasterizer.Rasterize(edges.Edges, box.Width, box.Height, box.MinX, box.MinY, options.AntiAlias, box.BearingX, box.BearingY);

        if (options.AntiAlias != AntiAliasMode.None)
        {
            // Normal's thin stems sit in one column at their true coverage; at small sizes
            // they are lifted toward solid (structure first, then tone).
            if (GlyphHinter.EffectiveHintLevel(options) == HintLevel.Normal)
                ThinStrokeDarkening.Apply(raster.Bitmap, raster.Width, raster.Height, glyph.RenderPpem(options));

            // Gamma is a tone curve on grayscale coverage (the FreeType backend's semantics);
            // mono has no partial coverage to curve, and the default (1.8) is the identity, so
            // default output is untouched.
            GammaCurve.Apply(raster.Bitmap, options.Gamma);
        }

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = raster.Bitmap,
            Width = raster.Width,
            Height = raster.Height,
            Pitch = raster.Width,
            Metrics = new GlyphMetrics(
                BearingX: raster.BearingX,
                BearingY: raster.BearingY,
                Advance: glyph.Advance,
                Width: raster.Width,
                Height: raster.Height),
            Format = PixelFormat.Grayscale8
        };
    }

    private static OutlineFlattener.PooledEdges Edges(PreparedGlyph glyph, RasterOptions options)
    {
        bool mono = options.AntiAlias == AntiAliasMode.None;
        bool strokes = mono && MonoRenderingFor(glyph, options) == MonoRendering.Strokes;
        return OutlineFlattener.FlattenPooled(
            glyph.Outline, glyph.ToPixels,
            tolerance: strokes ? MonoStrokeRasterizer.Flatness : OutlineFlattener.DefaultTolerance,
            includeHorizontal: mono);
    }
}
