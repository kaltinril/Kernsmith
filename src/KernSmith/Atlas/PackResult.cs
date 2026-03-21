namespace KernSmith.Atlas;

public sealed class PackResult
{
    public required IReadOnlyList<GlyphPlacement> Placements { get; init; }
    public required int PageCount { get; init; }
    public required int PageWidth { get; init; }
    public required int PageHeight { get; init; }
}
