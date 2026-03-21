namespace KernSmith.Font.Models;

/// <summary>
/// Per-glyph positioning metrics in pixels, computed during rasterization.
/// </summary>
public readonly record struct GlyphMetrics(
    int BearingX,
    int BearingY,
    int Advance,
    int Width,
    int Height);
