namespace KernSmith;

/// <summary>
/// What a <see cref="BmFontIncrementalSession"/> does when a newly added glyph does not
/// fit in the free space of the existing atlas pages. Existing placements are never
/// moved under any policy.
/// </summary>
public enum AdditionOverflowPolicy
{
    /// <summary>
    /// Enlarge the pages in place: the smaller page dimension is doubled (power-of-two
    /// growth, mirroring <see cref="FontGeneratorOptions.AutofitTexture"/>'s bump),
    /// capped at <see cref="FontGeneratorOptions.MaxTextureWidth"/>/<see cref="FontGeneratorOptions.MaxTextureHeight"/>,
    /// repeating until the glyph fits. Existing placements remain valid — pages only
    /// gain free strips. When both dimensions are already at their maximum and the
    /// glyph still does not fit, an <see cref="AtlasPackingException"/> is thrown.
    /// <see cref="Output.GlyphAdditionResult.PageGrown"/> tells the caller to reallocate the
    /// texture and copy old contents at (0,0). This is the default.
    /// </summary>
    Grow = 0,

    /// <summary>
    /// Append a fresh, fully-free page and place the glyph there. Never fails (unless a
    /// single glyph is larger than an entire page, which throws
    /// <see cref="AtlasPackingException"/>). Page dimensions never change.
    /// </summary>
    NewPage = 1,

    /// <summary>
    /// Throw <see cref="AtlasPackingException"/> immediately on the first glyph that does
    /// not fit. For consumers with a fixed-size texture that cannot be reallocated.
    /// </summary>
    Throw = 2
}
