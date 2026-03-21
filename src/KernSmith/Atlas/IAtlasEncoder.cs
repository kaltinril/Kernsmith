namespace KernSmith.Atlas;

/// <summary>
/// Encodes atlas pixel data to an image format such as PNG, TGA, or DDS.
/// </summary>
public interface IAtlasEncoder
{
    /// <summary>
    /// Encodes raw pixel data into an image file format (PNG, TGA, or DDS).
    /// </summary>
    /// <param name="pixelData">Raw pixel buffer.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="format">Pixel format of the input data.</param>
    /// <returns>The encoded image file bytes.</returns>
    byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format);

    /// <summary>
    /// File extension including the leading dot (e.g., ".png", ".tga", ".dds").
    /// </summary>
    string FileExtension { get; }
}
