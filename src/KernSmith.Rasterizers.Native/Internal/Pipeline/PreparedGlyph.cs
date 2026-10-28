using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// A glyph after stages 1–4 (decode, transform, bounds, scale/hint): everything a renderer
/// needs, and everything <c>GetGlyphMetrics</c> reports, so the two can never disagree.
/// </summary>
/// <param name="Outline">The outline to draw. Its coordinates map to pixels through
/// <paramref name="ToPixels"/> — renderers must use that mapping, never a scale of their own,
/// because a hinter may hand back an outline already in pixel space.</param>
/// <param name="ToPixels">Maps <paramref name="Outline"/> coordinates to pixel space, Y down,
/// with the baseline on row 0.</param>
/// <param name="Box">The pixel grid the bitmap fills, relative to the pen and baseline.</param>
/// <param name="Advance">The horizontal advance, in whole pixels.</param>
internal readonly record struct PreparedGlyph(GlyphOutline Outline, OutlineTransform ToPixels, GlyphBox Box, int Advance)
{
    /// <summary>True when there is nothing to draw: a real advance, but no bitmap.</summary>
    public bool IsBlank => Outline.IsEmpty || Box.IsEmpty;
}
