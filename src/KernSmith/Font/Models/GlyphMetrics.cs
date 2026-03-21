namespace KernSmith.Font.Models;

/// <summary>
/// Per-glyph metrics in pixels (post-rasterization).
/// </summary>
public readonly record struct GlyphMetrics(
    int BearingX,
    int BearingY,
    int Advance,
    int Width,
    int Height);
