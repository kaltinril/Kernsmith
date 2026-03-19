using StbImageWriteSharp;

namespace Bmfontier.Atlas;

internal sealed class StbPngEncoder : IAtlasEncoder
{
    public string FileExtension => ".png";

    public byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format)
    {
        var components = format switch
        {
            PixelFormat.Grayscale8 => ColorComponents.Grey,
            PixelFormat.Rgba32 => ColorComponents.RedGreenBlueAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format.")
        };

        using var ms = new MemoryStream();
        var writer = new ImageWriter();
        writer.WritePng(pixelData, width, height, components, ms);
        return ms.ToArray();
    }
}
