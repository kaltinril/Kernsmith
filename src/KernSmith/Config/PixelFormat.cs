namespace KernSmith;

/// <summary>
/// Pixel format for glyph bitmaps and atlas pages.
/// </summary>
public enum PixelFormat
{
    /// <summary>1 byte per pixel (alpha only). Used for standard glyph rendering.</summary>
    Grayscale8 = 0,

    /// <summary>4 bytes per pixel (R, G, B, A). Used for color fonts, effects, and composited atlas pages.</summary>
    Rgba32 = 1
}
