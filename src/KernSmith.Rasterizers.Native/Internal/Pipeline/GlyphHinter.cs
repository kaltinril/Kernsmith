using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The pixel-space hint hook at the end of stage 4. A no-op today; Phase 174's auto-hinter
/// fills in <see cref="Apply"/>.
/// </summary>
internal static class GlyphHinter
{
    /// <summary>
    /// Returns the glyph to render. A hinter may move outline points, the box and the advance;
    /// to work in pixels it returns a pixel-space outline with an identity
    /// <see cref="PreparedGlyph.ToPixels"/> mapping (scale 1, baseline 0).
    /// </summary>
    public static PreparedGlyph Apply(PreparedGlyph glyph, RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return glyph;
    }
}
