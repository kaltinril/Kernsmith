using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Color;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>Render-mode dispatch: picks the <see cref="IGlyphRenderer"/> for a request.</summary>
internal static class GlyphRenderers
{
    /// <summary>
    /// The renderer for <paramref name="options"/>. The rasterizer calls this before looking the
    /// codepoint up, so an unsupported mode is rejected for every glyph, mapped or not.
    /// </summary>
    /// <remarks>
    /// <see cref="RasterOptions.ColorFont"/> is not a mode here: color layers wrap whichever
    /// renderer this returns (see <see cref="SelectForFace"/>), and a face
    /// without color glyphs renders exactly as if it were off.
    /// </remarks>
    /// <exception cref="NotSupportedException">If no renderer handles the requested mode.</exception>
    public static IGlyphRenderer Select(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Signed distance field (Phases 169/170).
        if (options.Sdf)
            throw new NotSupportedException("Native rasterizer does not support SDF rendering.");

        // LCD subpixel (Phase 173). Light has no hinting to be light about yet (Phase 174), so
        // it renders as coverage; core negotiation already degrades it to Grayscale.
        if (options.AntiAlias == AntiAliasMode.Lcd)
            return LcdRenderer.Instance;

        return CoverageRenderer.Instance;
    }

    /// <summary>
    /// The renderer for <paramref name="options"/> on a face: with
    /// <see cref="RasterOptions.ColorFont"/> on and color glyphs present, glyphs with
    /// <c>COLR</c> layers render as color (Phase 172) and every other glyph through
    /// <see cref="Select"/>.
    /// </summary>
    /// <param name="options">The request.</param>
    /// <param name="colorGlyphs">The face's color glyphs, or null when it has none.</param>
    /// <param name="pipeline">The face's pipeline, which color layers are prepared through.</param>
    /// <exception cref="NotSupportedException">If no renderer handles the requested mode.</exception>
    public static IGlyphRenderer SelectForFace(RasterOptions options, ColorGlyphs? colorGlyphs, GlyphPipeline pipeline)
    {
        var renderer = Select(options);
        return options.ColorFont && colorGlyphs is not null
            ? new ColorLayerRenderer(colorGlyphs, pipeline, renderer)
            : renderer;
    }
}
