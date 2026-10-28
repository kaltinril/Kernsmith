using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>Render-mode dispatch: picks the <see cref="IGlyphRenderer"/> for a request.</summary>
internal static class GlyphRenderers
{
    /// <summary>
    /// The renderer for <paramref name="options"/>. The rasterizer calls this before looking the
    /// codepoint up, so an unsupported mode is rejected for every glyph, mapped or not.
    /// </summary>
    /// <exception cref="NotSupportedException">If no renderer handles the requested mode.</exception>
    public static IGlyphRenderer Select(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Color layers (Phase 172). Checked before SDF, so a request for both reports color.
        if (options.ColorFont)
            throw new NotSupportedException("Native rasterizer does not support color font rendering.");

        // Signed distance field (Phase 169).
        if (options.Sdf)
            return SdfRenderer.Instance;

        // LCD subpixel (Phase 173) branches on options.AntiAlias here; until then every
        // anti-alias mode is coverage.
        return CoverageRenderer.Instance;
    }
}
