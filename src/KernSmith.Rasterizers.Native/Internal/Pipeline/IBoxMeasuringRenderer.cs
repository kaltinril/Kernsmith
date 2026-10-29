using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// A renderer whose metrics, for some requests, depend on nothing but the glyph's box and
/// advance, so a glyph can be measured from <see cref="GlyphPipeline.TryPrepareBox"/> without
/// decoding its outline.
/// </summary>
/// <remarks>
/// Wherever <see cref="MeasuresFromBox"/> is true, an implementation's
/// <see cref="IGlyphRenderer.Measure"/> must be exactly <see cref="MeasureBox"/> of the prepared
/// glyph's box and advance. A mode whose extent reads the outline or the pixels (LCD widens by
/// the tight horizontal extent; <see cref="AntiAliasMode.None"/> crops to the ink; color layers
/// union their own boxes) must answer false. <c>NativeMetricsEquivalenceTests</c> holds every
/// mode to it.
/// </remarks>
internal interface IBoxMeasuringRenderer : IGlyphRenderer
{
    /// <summary>True when this request's metrics follow from the box and advance alone.</summary>
    bool MeasuresFromBox(RasterOptions options);

    /// <summary>The metrics <see cref="IGlyphRenderer.Render"/> produces for a non-blank glyph
    /// with this box and advance.</summary>
    GlyphMetrics MeasureBox(GlyphBox box, int advance, RasterOptions options);
}
