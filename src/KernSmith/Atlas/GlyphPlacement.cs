namespace KernSmith.Atlas;

/// <summary>
/// A glyph's assigned position within the atlas after packing.
/// </summary>
/// <param name="Id">The glyph ID (usually the Unicode character code).</param>
/// <param name="PageIndex">Which atlas page it's on (zero-based).</param>
/// <param name="X">X pixel position of the top-left corner on the page.</param>
/// <param name="Y">Y pixel position of the top-left corner on the page.</param>
public readonly record struct GlyphPlacement(
    int Id,
    int PageIndex,
    int X,
    int Y
);
