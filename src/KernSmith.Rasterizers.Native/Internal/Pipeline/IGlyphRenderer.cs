using KernSmith.Font.Models;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Stage 5 of the glyph pipeline: turns a <see cref="PreparedGlyph"/> into pixels in one render
/// mode (coverage today; SDF, LCD and color layers later). Chosen per request by
/// <see cref="GlyphRenderers.Select"/>.
/// </summary>
/// <remarks>
/// <see cref="Measure"/> must return exactly the metrics <see cref="Render"/> puts on the glyph —
/// a mode that pads the bitmap (SDF spread) pads both. Blank glyphs never reach a renderer; the
/// rasterizer answers them itself.
/// </remarks>
internal interface IGlyphRenderer
{
    /// <summary>The metrics <see cref="Render"/> would produce, without rendering.</summary>
    GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options);

    /// <summary>Renders a non-blank glyph.</summary>
    RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options);
}
