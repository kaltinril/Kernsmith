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
    /// <exception cref="ArgumentOutOfRangeException">If an LCD request names an undefined
    /// <see cref="RasterOptions.LcdFilter"/> or <see cref="RasterOptions.LcdSubpixelOrder"/>.</exception>
    public static IGlyphRenderer Select(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Distance fields: single-channel (Phase 169), multi-channel (Phase 170).
        switch (options.EffectiveSdfMode)
        {
            case SdfMode.Sdf:
                return SdfRenderer.Instance;
            case SdfMode.Msdf:
                return MsdfRenderer.Msdf;
            case SdfMode.Mtsdf:
                return MsdfRenderer.Mtsdf;
        }

        // LCD subpixel (Phase 173). Light renders as coverage: its hinting already ran in
        // the pipeline's hint stage (GlyphHinter, Phase 174).
        if (options.AntiAlias == AntiAliasMode.Lcd)
            return LcdRenderer.For(options.LcdFilter, options.LcdSubpixelOrder);

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
    /// <remarks>
    /// With single-channel SDF, color glyphs still render as color layers and only glyphs
    /// without layers become distance fields — FreeType's behavior. Multi-channel fields are
    /// rejected instead: color layers in an MSDF atlas would read as distances.
    /// </remarks>
    /// <exception cref="NotSupportedException">If no renderer handles the requested mode, or
    /// MSDF/MTSDF is requested together with color on a face that has color glyphs.</exception>
    public static IGlyphRenderer SelectForFace(RasterOptions options, ColorGlyphs? colorGlyphs, GlyphPipeline pipeline)
    {
        var renderer = Select(options);
        if (!options.ColorFont || colorGlyphs is null)
            return renderer;

        if (options.EffectiveSdfMode is SdfMode.Msdf or SdfMode.Mtsdf)
            throw new NotSupportedException("Native rasterizer cannot combine color glyphs with MSDF/MTSDF rendering.");

        return new ColorLayerRenderer(colorGlyphs, pipeline, renderer);
    }
}
