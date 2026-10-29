namespace KernSmith;

/// <summary>
/// How the glyph outline (<see cref="FontGeneratorOptions.Outline"/>) is drawn.
/// </summary>
public enum OutlineMode
{
    /// <summary>
    /// The pipeline's own bitmap outline, grown from each rendered glyph by a distance transform
    /// (default). Every backend draws outlines this way.
    /// </summary>
    Effect,

    /// <summary>
    /// The rasterizer draws the outline from the glyph's vector outline itself, so corners, tight
    /// curves and narrow counters follow the true shape instead of the rendered pixels. Same width,
    /// color, layering, cells and corner shape as <see cref="Effect"/> (<see cref="FontGeneratorOptions.OutlineShape"/>
    /// picks a round, square or diamond pen); the ring reaches exactly the width from the true
    /// outline with a crisp anti-aliased edge, where the effect fades over 1.5 pixels and sits a
    /// little closer to the glyph, so vector rings look slightly heavier. Used where the backend reports
    /// <see cref="Rasterizer.IRasterizerCapabilities.SupportsOutlineStroke"/>; other backends, and
    /// glyphs the backend cannot stroke (custom glyphs, color glyphs), keep <see cref="Effect"/>.
    /// Cannot be combined with distance fields (SDF, MSDF, MTSDF).
    /// </summary>
    Vector
}
