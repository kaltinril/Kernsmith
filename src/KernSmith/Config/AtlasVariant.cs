namespace KernSmith;

/// <summary>
/// Declares an additional character-set rendering, packed into its own atlas region(s)
/// and described by its own <see cref="Output.Model.BmFontModel"/>, so it can be sampled
/// as an ordinary RGBA glyph with no shader required to unpack it. See phase-181 plan doc
/// (issue #175) for background.
/// </summary>
/// <param name="Name">
/// Identifies this variant (e.g. "shadow"). Used as the dictionary key in
/// <see cref="Output.BmFontResult.VariantModels"/> and as the file-name suffix when written to disk.
/// </param>
/// <param name="Kind">The kind of variant rendering to produce.</param>
/// <param name="BlurRadius">Blur radius applied to the variant shape, in pixels.</param>
/// <param name="HardShadow">When true, produces a crisp silhouette instead of a soft-edged one.</param>
public sealed record AtlasVariant(
    string Name,
    AtlasVariantKind Kind,
    int BlurRadius = 0,
    bool HardShadow = false);

/// <summary>The kind of additional character-set rendering an <see cref="AtlasVariant"/> produces.</summary>
public enum AtlasVariantKind
{
    /// <summary>
    /// A flat dropshadow silhouette per glyph: coverage only, no baked offset or color.
    /// The consumer translates and tints it at draw time.
    /// </summary>
    ShadowSilhouette = 0
}
