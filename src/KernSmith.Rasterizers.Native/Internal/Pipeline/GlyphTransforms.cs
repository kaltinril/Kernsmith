using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>Builds the ordered outline transform list for a request.</summary>
internal static class GlyphTransforms
{
    /// <summary>
    /// The transforms to apply, in order. Empty today: <see cref="RasterOptions.Bold"/> and
    /// <see cref="RasterOptions.Italic"/> are ignored until Phase 167 adds entries here, and
    /// Phase 168 (stroke, condensed) appends after them.
    /// </summary>
    public static IReadOnlyList<IGlyphTransform> For(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return [];
    }
}
