namespace KernSmith.Font.Models;

/// <summary>
/// Per-glyph horizontal metrics in unscaled font design units (the same space as
/// <see cref="FontInfo.UnitsPerEm"/> and <see cref="FontInfo.Ascender"/>).
/// </summary>
/// <param name="AdvanceWidth">Horizontal advance to the next glyph, in font design units.</param>
/// <param name="LeftSideBearing">Distance from the glyph origin to the left edge of the glyph's bounding box, in font design units.</param>
public readonly record struct GlyphDesignMetrics(int AdvanceWidth, int LeftSideBearing);
