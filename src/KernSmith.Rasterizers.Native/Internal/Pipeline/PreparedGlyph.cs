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
    /// <summary>
    /// The glyph this was prepared from. Set by <see cref="GlyphPipeline.Prepare"/>; a hint
    /// stage must carry it over (build its result with <c>with</c>), because
    /// <see cref="IGlyphRenderer.Measure"/> has nothing else to identify the glyph by.
    /// </summary>
    public int GlyphIndex { get; init; }

    /// <summary>
    /// The advance in font units before scaling and rounding, set by <see cref="GlyphPipeline.Prepare"/>,
    /// for a hint stage that rebuilds the advance from the fitted outline and needs the exact
    /// side bearings rather than the rounded <see cref="Advance"/>.
    /// </summary>
    public float AdvanceWidth { get; init; }

    /// <summary>
    /// What the <c>Normal</c> hint stage knows about the glyph's spacing — the font's own side
    /// bearings and whether each facing edge is a straight stem — or null when the glyph's x
    /// axis was not fitted (unhinted, y-only, past the x taper). The kerning bake (<see cref="KernSmith.Rasterizer.RasterOptions.BakeHintingIntoKerning"/>)
    /// reads it alongside the rendered box to find the pairs that would touch.
    /// </summary>
    public HintSpacing? Spacing { get; init; }

    /// <summary>True when there is nothing to draw: a real advance, but no bitmap.</summary>
    public bool IsBlank => Outline.IsEmpty || Box.IsEmpty;
}

/// <summary>The <c>Normal</c> hint stage's spacing facts about a glyph.</summary>
/// <param name="FontLeft">The font's own left side bearing at this size, in pixels, before hinting.</param>
/// <param name="FontRight">The font's own right side bearing at this size, in pixels, before hinting.</param>
/// <param name="LeftStem">True when the fitted ink's left edge is a straight vertical stem.</param>
/// <param name="RightStem">True when the fitted ink's right edge is a straight vertical stem.</param>
internal readonly record struct HintSpacing(float FontLeft, float FontRight, bool LeftStem, bool RightStem);
