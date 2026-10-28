using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>Builds the ordered outline transform list for a request.</summary>
internal static class GlyphTransforms
{
    /// <summary>
    /// The transforms to apply, in order: synthetic bold, then synthetic italic — FreeType's
    /// order, so the expansion is uniform and the result is slanted as a whole. Empty when no
    /// style is requested, which keeps untransformed glyphs on their declared <c>glyf</c> box.
    /// </summary>
    /// <remarks>
    /// The rasterizer applies whatever is requested: core has already cleared
    /// <see cref="RasterOptions.Bold"/>/<see cref="RasterOptions.Italic"/> for a face that is
    /// bold/italic already (unless forced), so this must not re-check the font's style.
    ///
    /// The full order, once Phase 168's options exist: width scale, small caps, bold, italic;
    /// a stroke is built from the fully styled outline as a separate layer, never appended here.
    /// </remarks>
    /// <param name="options">The request.</param>
    /// <param name="unitsPerEm">The face's <c>head.unitsPerEm</c>, for pixel-defined strengths.</param>
    public static IReadOnlyList<IGlyphTransform> For(RasterOptions options, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Bold && !options.Italic)
            return [];

        var transforms = new List<IGlyphTransform>(2);
        if (options.Bold)
            transforms.Add(new BoldTransform(BoldTransform.StrengthInFontUnits(options, unitsPerEm)));
        if (options.Italic)
            transforms.Add(ItalicTransform.Instance);

        return transforms;
    }
}
