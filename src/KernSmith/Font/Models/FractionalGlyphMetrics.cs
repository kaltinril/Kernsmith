namespace KernSmith.Font.Models;

/// <summary>
/// A glyph's horizontal metrics before rounding, in pixels at the size it was rasterized at, as
/// a backend reports them on <see cref="Rasterizer.RasterizedGlyph.Fractional"/> when
/// <see cref="Rasterizer.RasterOptions.FractionalMetrics"/> is on. The pipeline carries them
/// through super sampling and effects into the <c>.fnt</c> extended metadata
/// (<see cref="Output.Model.FractionalCharMetrics"/>) in 1/64 px steps.
/// </summary>
/// <param name="Advance">The unhinted advance: the font's advance at this size, after any synthetic
/// width or bold transform, not rounded.</param>
/// <param name="BearingX">The unhinted left side bearing at this size: the distance from the pen
/// origin to the left edge of the outline's control box, not rounded.</param>
/// <param name="AdvanceDelta">What hinting added to the advance (hinted minus unhinted), or null when
/// the glyph was not hinted or the backend cannot say.</param>
/// <param name="BearingXDelta">How far hinting moved the ink's left edge (hinted minus unhinted), or
/// null when the glyph was not hinted or the backend cannot say.</param>
public readonly record struct FractionalGlyphMetrics(float Advance, float BearingX, float? AdvanceDelta = null, float? BearingXDelta = null);
