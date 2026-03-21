namespace KernSmith;

/// <summary>
/// Pixel format for glyph bitmaps and atlas pages.
/// </summary>
public enum PixelFormat
{
    /// <summary>1 byte per pixel, used for normal rendering.</summary>
    Grayscale8 = 0,

    /// <summary>4 bytes per pixel, used for color fonts or composed atlas pages.</summary>
    Rgba32 = 1
}
