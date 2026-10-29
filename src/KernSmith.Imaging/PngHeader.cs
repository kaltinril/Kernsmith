namespace KernSmith.Imaging;

/// <summary>The image properties a PNG declares in its <c>IHDR</c> chunk.</summary>
/// <param name="Width">Width in pixels, at least 1.</param>
/// <param name="Height">Height in pixels, at least 1.</param>
/// <param name="BitDepth">Bits per sample (or per palette index): 1, 2, 4, 8 or 16.</param>
/// <param name="ColorType">How samples are arranged in a pixel.</param>
/// <param name="Interlaced">True for Adam7 interlacing.</param>
public readonly record struct PngHeader(int Width, int Height, int BitDepth, PngColorType ColorType, bool Interlaced);
