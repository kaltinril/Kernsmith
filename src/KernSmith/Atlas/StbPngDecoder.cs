using StbImageSharp;

namespace KernSmith.Atlas;

internal static class StbPngDecoder
{
    internal static (byte[] Pixels, int Width, int Height) DecodePng(byte[] pngBytes)
    {
        var result = ImageResult.FromMemory(pngBytes, ColorComponents.RedGreenBlueAlpha);
        return (result.Data, result.Width, result.Height);
    }
}
