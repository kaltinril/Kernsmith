namespace KernSmith.Atlas;

/// <summary>
/// A rectangular region of an atlas page that is already taken by an existing glyph,
/// used as input to incremental packing (<see cref="IAtlasPacker.PackInto"/>).
/// </summary>
/// <param name="PageIndex">Which atlas page the region is on (zero-based).</param>
/// <param name="X">X pixel position of the top-left corner on the page.</param>
/// <param name="Y">Y pixel position of the top-left corner on the page.</param>
/// <param name="Width">Region width in pixels.</param>
/// <param name="Height">Region height in pixels.</param>
public readonly record struct OccupiedRect(
    int PageIndex,
    int X,
    int Y,
    int Width,
    int Height
);
