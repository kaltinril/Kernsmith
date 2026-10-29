using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Bitmaps;
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
    /// <c>COLR</c> layers or color bitmaps render as color (Phase 172) and every other glyph
    /// through <see cref="Select"/>.
    /// </summary>
    /// <param name="options">The request.</param>
    /// <param name="colorGlyphs">The face's color glyphs, or null when it has none.</param>
    /// <param name="pipeline">The face's pipeline, which color layers are prepared through.</param>
    /// <param name="bitmapGlyphs">The face's color bitmap strikes (<c>CBDT</c>/<c>sbix</c>), or null when it has none.</param>
    /// <remarks>
    /// Precedence with color on: <c>COLR</c> (version 1, then 0), then a bitmap, then the outline.
    /// With color off, bitmaps draw only for a face that has no outlines, as grayscale ink coverage.
    /// With single-channel SDF, color glyphs still render as color and only the others become
    /// distance fields — FreeType's behavior. Multi-channel fields are rejected instead: color in
    /// an MSDF atlas would read as distances. A distance field without color on a bitmap-only face
    /// is rejected too: there is no outline to measure.
    /// </remarks>
    /// <exception cref="NotSupportedException">If no renderer handles the requested mode,
    /// MSDF/MTSDF is requested together with color on a face that has color glyphs, or a distance
    /// field is requested without color on a bitmap-only face.</exception>
    public static IGlyphRenderer SelectForFace(RasterOptions options, ColorGlyphs? colorGlyphs, GlyphPipeline pipeline, BitmapGlyphs? bitmapGlyphs = null)
    {
        var renderer = Select(options);

        if (bitmapGlyphs is { IsBitmapOnly: true } && !options.ColorFont)
        {
            if (options.EffectiveSdfMode != SdfMode.None)
                throw new NotSupportedException(
                    "This font draws its glyphs as color bitmaps (CBDT/CBLC or sbix) and has no outlines, so the Native "
                    + "rasterizer cannot make distance fields from it; turn on color rendering or turn off SDF.");
            return new BitmapGlyphRenderer(bitmapGlyphs, pipeline, renderer, color: false);
        }

        if (!options.ColorFont || (colorGlyphs is null && bitmapGlyphs is null))
            return renderer;

        if (options.EffectiveSdfMode is SdfMode.Msdf or SdfMode.Mtsdf)
            throw new NotSupportedException("Native rasterizer cannot combine color glyphs with MSDF/MTSDF rendering.");

        if (bitmapGlyphs is not null)
            renderer = new BitmapGlyphRenderer(bitmapGlyphs, pipeline, renderer, color: true);

        return colorGlyphs is null ? renderer : new ColorLayerRenderer(colorGlyphs, pipeline, renderer);
    }
}
