using KernSmith.Atlas;
using StbImageWriteSharp;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// Frozen copy of the StbImageWriteSharp-backed encoder that <see cref="PngEncoder"/> replaced,
/// kept as the oracle for <see cref="PngEncoderEquivalenceTests"/>. Do not optimise it.
/// </summary>
internal static class ReferenceStbPngEncoder
{
    public static byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format)
    {
        var components = format switch
        {
            PixelFormat.Grayscale8 => ColorComponents.Grey,
            PixelFormat.Rgba32 => ColorComponents.RedGreenBlueAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format.")
        };

        using var ms = new MemoryStream(pixelData.Length / 2);
        new ImageWriter().WritePng(pixelData, width, height, components, ms);
        return ms.ToArray();
    }
}
