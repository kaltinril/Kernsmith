namespace KernSmith.Output;

/// <summary>
/// Result of an atlas size query, describing the estimated atlas dimensions without performing rasterization.
/// </summary>
public sealed class AtlasSizeInfo
{
    /// <summary>Estimated atlas width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Estimated atlas height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Estimated number of atlas pages.</summary>
    public int PageCount { get; init; }

    /// <summary>Number of glyphs that will be included.</summary>
    public int GlyphCount { get; init; }

    /// <summary>Estimated packing efficiency (0.0 to 1.0).</summary>
    public float EstimatedEfficiency { get; init; }
}
