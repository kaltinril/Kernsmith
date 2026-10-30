using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>Builds the ordered outline transform list for a request.</summary>
internal static class GlyphTransforms
{
    /// <summary>
    /// The transforms to apply, in order: width scale (condensed/expanded), synthetic bold, then
    /// synthetic italic at <see cref="RasterOptions.ItalicAngle"/>. Bold before italic is
    /// FreeType's order, so the expansion is uniform and the result is slanted as a whole; the
    /// width scale comes first so the bold strength and the slant stay true. Empty when nothing is
    /// requested, which keeps untransformed glyphs on their declared <c>glyf</c> box.
    /// </summary>
    /// <remarks>
    /// The rasterizer applies whatever is requested: core has already cleared
    /// <see cref="RasterOptions.Bold"/>/<see cref="RasterOptions.Italic"/> for a face that is
    /// bold/italic already (unless forced), so this must not re-check the font's style.
    ///
    /// Small caps (geometry only, no option) would go between width scale and bold. The vector
    /// outline layer is drawn from the fully transformed glyph (<see cref="OutlineLayer"/>),
    /// never appended here.
    /// </remarks>
    /// <param name="options">The request.</param>
    /// <param name="unitsPerEm">The face's <c>head.unitsPerEm</c>, for pixel-defined strengths.</param>
    public static IReadOnlyList<IGlyphTransform> For(RasterOptions options, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool widthScaled = options.WidthPercent != 100;
        if (!options.Bold && !options.Italic && !widthScaled)
            return [];

        var transforms = new List<IGlyphTransform>(3);
        if (widthScaled)
            transforms.Add(new WidthScaleTransform(options.WidthPercent / 100f));
        if (options.Bold)
            transforms.Add(new BoldTransform(BoldTransform.StrengthInFontUnits(options, unitsPerEm)));
        if (options.Italic)
            transforms.Add(ItalicTransform.ForAngle(options.ItalicAngle));

        return transforms;
    }
}
