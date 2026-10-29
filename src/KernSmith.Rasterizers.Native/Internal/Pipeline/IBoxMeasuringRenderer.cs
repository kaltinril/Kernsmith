using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// A renderer whose metrics depend on nothing but the glyph's box and advance, so a glyph can
/// be measured from <see cref="GlyphPipeline.TryPrepareBox"/> without decoding its outline.
/// </summary>
/// <remarks>
/// An implementation's <see cref="IGlyphRenderer.Measure"/> must be exactly
/// <see cref="MeasureBox"/> of the prepared glyph's box and advance. A renderer whose extent
/// reads the outline (LCD widens by the tight horizontal extent; color layers union their own
/// boxes) must not implement this. <c>NativeMetricsEquivalenceTests</c> holds every mode to it.
/// </remarks>
internal interface IBoxMeasuringRenderer : IGlyphRenderer
{
    /// <summary>The metrics <see cref="IGlyphRenderer.Render"/> produces for a non-blank glyph
    /// with this box and advance.</summary>
    GlyphMetrics MeasureBox(GlyphBox box, int advance, RasterOptions options);
}
