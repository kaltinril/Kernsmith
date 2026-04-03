namespace KernSmith.Atlas;

/// <summary>
/// Encodes atlas pages as uncompressed TGA (Targa) images.
/// </summary>
internal sealed class TgaEncoder : IAtlasEncoder
{
    public string FileExtension => ".tga";

    public byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format)
    {
        // TGA stores pixels bottom-to-top by default, but we use the origin bit
        // in the image descriptor to indicate top-left origin (bit 5 = 1).
        var bpp = format == PixelFormat.Rgba32 ? 32 : 8;
        var imageType = format == PixelFormat.Rgba32 ? (byte)2 : (byte)3; // 2 = uncompressed true-color, 3 = uncompressed grayscale
        var bytesPerPixel = bpp / 8;
        var expectedSize = width * height * bytesPerPixel;
        if (pixelData.Length < expectedSize)
            throw new ArgumentException($"pixelData length {pixelData.Length} is less than expected {expectedSize} for {width}x{height} at {bytesPerPixel} bytes/pixel.", nameof(pixelData));

        // TGA header is 18 bytes.
        var header = new byte[18];
        header[0] = 0;              // ID length
        header[1] = 0;              // Color map type (none)
        header[2] = imageType;      // Image type
        // Color map spec (5 bytes) — all zero for no color map
        // Image spec
        header[8] = 0;              // X origin (low)
        header[9] = 0;              // X origin (high)
        header[10] = 0;             // Y origin (low)
        header[11] = 0;             // Y origin (high)
        header[12] = (byte)(width & 0xFF);        // Width (low)
        header[13] = (byte)((width >> 8) & 0xFF); // Width (high)
        header[14] = (byte)(height & 0xFF);        // Height (low)
        header[15] = (byte)((height >> 8) & 0xFF); // Height (high)
        header[16] = (byte)bpp;     // Bits per pixel
        header[17] = 0x20;          // Image descriptor: bit 5 = top-left origin

        if (format == PixelFormat.Rgba32)
            header[17] = 0x28;      // Top-left origin + 8 alpha bits

        var rowBytes = width * bytesPerPixel;
        var result = new byte[18 + pixelData.Length];
        Array.Copy(header, 0, result, 0, 18);

        if (format == PixelFormat.Rgba32)
        {
            // TGA uses BGRA byte order; our pixel data is RGBA.
            for (var row = 0; row < height; row++)
            {
                var srcOffset = row * rowBytes;
                var dstOffset = 18 + row * rowBytes;

                // Copy the whole row first (G and A bytes land in the right place).
                Buffer.BlockCopy(pixelData, srcOffset, result, dstOffset, rowBytes);

                // Then swap R and B in place.
                for (var x = 0; x < width; x++)
                {
                    var di = dstOffset + x * 4;
                    (result[di + 0], result[di + 2]) = (result[di + 2], result[di + 0]);
                }
            }
        }
        else
        {
            // Grayscale: copy directly.
            Array.Copy(pixelData, 0, result, 18, pixelData.Length);
        }

        return result;
    }
}
