using StbImageSharp;

namespace KernSmith.Atlas;

/// <summary>
/// Decodes PNG image data into raw RGBA pixel buffers using StbImageSharp.
/// </summary>
internal static class StbPngDecoder
{
    /// <summary>
    /// Decodes a PNG byte array into RGBA pixel data with dimensions.
    /// </summary>
    /// <param name="pngBytes">The PNG file bytes to decode.</param>
    /// <returns>A tuple of the decoded RGBA pixels, width, and height.</returns>
    internal static (byte[] Pixels, int Width, int Height) DecodePng(byte[] pngBytes)
    {
        var result = ImageResult.FromMemory(pngBytes, ColorComponents.RedGreenBlueAlpha);
        return (result.Data, result.Width, result.Height);
    }
}
