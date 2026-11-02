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
    /// The outline as it was before the hint stage, in font units (the very object
    /// <see cref="Outline"/> is when nothing hinted the glyph), set by <see cref="GlyphPipeline.Prepare"/>
    /// and carried through a hint stage's <c>with</c>. The fractional-metrics report
    /// (<see cref="KernSmith.Rasterizer.RasterOptions.FractionalMetrics"/>) reads the font's
    /// left side bearing from it and measures how far hinting moved the ink against it.
    /// </summary>
    public GlyphOutline? UnhintedOutline { get; init; }

    /// <summary>
    /// What the <c>Normal</c> hint stage knows about the glyph's spacing — the font's own side
    /// bearings and whether each facing edge is a straight stem; for a glyph whose fit the
    /// <c>gasp</c> table withheld (<see cref="FitWithheld"/>), the bearings with no stems, since
    /// it is set in whole columns all the same — or null when no x fit ran or would have run
    /// (unhinted, y-only, a face with no x axis to fit, past the x taper). The kerning bake (<see cref="KernSmith.Rasterizer.RasterOptions.BakeHintingIntoKerning"/>)
    /// reads it alongside the rendered box to find the pairs that would touch.
    /// </summary>
    public HintSpacing? Spacing { get; init; }

    /// <summary>
    /// True when the <c>Normal</c> hint stage ran for this request but left the outline
    /// unfitted because the font's <c>gasp</c> table asks for no grid-fitting at this size
    /// (<see cref="KernSmith.Rasterizers.Native.Internal.Hinting.HintingProfile.GridFitsAt"/>).
    /// The bilevel stroke renderer draws fitted outlines only, so
    /// <see cref="CoverageRenderer.MonoRenderingFor(PreparedGlyph, KernSmith.Rasterizer.RasterOptions)"/>
    /// falls back to the threshold for such a glyph.
    /// </summary>
    public bool FitWithheld { get; init; }

    /// <summary>
    /// The integer ppem the <c>Normal</c> stage fitted and scaled the glyph at, for a face that
    /// prefers integer ppem (<see cref="KernSmith.Rasterizers.Native.Internal.Hinting.HintingProfile.HintPpem"/>);
    /// 0 when the glyph is at the request's own ppem. The renderer's size-dependent stages — the
    /// stroke-rendering decision and thin-stroke darkening — read it (<see cref="RenderPpem"/>)
    /// so they see the size the fit did, and a request for 8.03 ppem renders exactly as one for 8.
    /// </summary>
    public float HintPpem { get; init; }

    /// <summary>The ppem the renderer's size-dependent stages use: <see cref="HintPpem"/> when set, else the request's.</summary>
    public float RenderPpem(KernSmith.Rasterizer.RasterOptions options) =>
        HintPpem > 0f ? HintPpem : options.Size * options.Dpi / 72f;

    /// <summary>True when there is nothing to draw: a real advance, but no bitmap.</summary>
    public bool IsBlank => Outline.IsEmpty || Box.IsEmpty;
}

/// <summary>The <c>Normal</c> hint stage's spacing facts about a glyph.</summary>
/// <param name="FontLeft">The font's own left side bearing at this size, in pixels, before hinting.</param>
/// <param name="FontRight">The font's own right side bearing at this size, in pixels, before hinting.</param>
/// <param name="LeftStem">True when the fitted ink's left edge is a straight vertical stem.</param>
/// <param name="RightStem">True when the fitted ink's right edge is a straight vertical stem.</param>
internal readonly record struct HintSpacing(float FontLeft, float FontRight, bool LeftStem, bool RightStem);
