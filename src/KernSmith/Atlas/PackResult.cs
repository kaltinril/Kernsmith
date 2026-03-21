namespace KernSmith.Atlas;

/// <summary>
/// Result of atlas packing: glyph placements, page count, and page dimensions.
/// </summary>
public sealed class PackResult
{
    /// <summary>Where each glyph was placed (page index and XY position).</summary>
    public required IReadOnlyList<GlyphPlacement> Placements { get; init; }

    /// <summary>Total number of atlas pages needed.</summary>
    public required int PageCount { get; init; }

    /// <summary>Width of each page in pixels.</summary>
    public required int PageWidth { get; init; }

    /// <summary>Height of each page in pixels.</summary>
    public required int PageHeight { get; init; }
}
