namespace KernSmith.Atlas;

/// <summary>
/// A glyph's dimensions (including padding and spacing) used as input to atlas packing.
/// </summary>
/// <param name="Id">The glyph ID (usually the Unicode character code).</param>
/// <param name="Width">Bitmap width in pixels.</param>
/// <param name="Height">Bitmap height in pixels.</param>
public readonly record struct GlyphRect(
    int Id,
    int Width,
    int Height
);
