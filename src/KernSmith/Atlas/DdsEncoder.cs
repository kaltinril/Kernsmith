namespace KernSmith.Atlas;

/// <summary>
/// Encodes atlas pages as uncompressed DDS (DirectDraw Surface) images.
/// Supports RGBA (A8R8G8B8) and luminance (L8) pixel formats.
/// </summary>
internal sealed class DdsEncoder : IAtlasEncoder
{
    public string FileExtension => ".dds";

    // DDS header constants
    private const uint DdsMagic = 0x20534444; // "DDS "
    private const uint HeaderSize = 124;
    private const uint PixelFormatSize = 32;

    // DDSD flags
    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPitch = 0x8;
    private const uint DdsdPixelFormat = 0x1000;

    // DDPF flags
    private const uint DdpfAlphaPixels = 0x1;
    private const uint DdpfRgb = 0x40;
    private const uint DdpfLuminance = 0x20000;

    // DDSCAPS
    private const uint DdscapsTexture = 0x1000;

    public byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format)
    {
        var isRgba = format == PixelFormat.Rgba32;
        var bpp = isRgba ? 32 : 8;
        var bytesPerPixel = bpp / 8;
        var pitch = width * bytesPerPixel;

        // Total size: 4 (magic) + 124 (header) + pixel data
        var result = new byte[4 + HeaderSize + height * pitch];
        var offset = 0;

        // Magic number
        WriteUInt32(result, ref offset, DdsMagic);

        // DDS_HEADER
        WriteUInt32(result, ref offset, HeaderSize); // dwSize
        WriteUInt32(result, ref offset, DdsdCaps | DdsdHeight | DdsdWidth | DdsdPitch | DdsdPixelFormat); // dwFlags
        WriteUInt32(result, ref offset, (uint)height); // dwHeight
        WriteUInt32(result, ref offset, (uint)width);  // dwWidth
        WriteUInt32(result, ref offset, (uint)pitch);  // dwPitchOrLinearSize
        WriteUInt32(result, ref offset, 0); // dwDepth
        WriteUInt32(result, ref offset, 0); // dwMipMapCount

        // dwReserved1[11]
        for (var i = 0; i < 11; i++)
            WriteUInt32(result, ref offset, 0);

        // DDS_PIXELFORMAT
        WriteUInt32(result, ref offset, PixelFormatSize); // dwSize
        if (isRgba)
        {
            WriteUInt32(result, ref offset, DdpfAlphaPixels | DdpfRgb); // dwFlags
            WriteUInt32(result, ref offset, 0);           // dwFourCC (not used for uncompressed)
            WriteUInt32(result, ref offset, 32);          // dwRGBBitCount
            WriteUInt32(result, ref offset, 0x00FF0000);  // dwRBitMask
            WriteUInt32(result, ref offset, 0x0000FF00);  // dwGBitMask
            WriteUInt32(result, ref offset, 0x000000FF);  // dwBBitMask
            WriteUInt32(result, ref offset, 0xFF000000);  // dwABitMask
        }
        else
        {
            WriteUInt32(result, ref offset, DdpfLuminance); // dwFlags
            WriteUInt32(result, ref offset, 0);             // dwFourCC
            WriteUInt32(result, ref offset, 8);             // dwRGBBitCount
            WriteUInt32(result, ref offset, 0xFF);          // dwRBitMask (luminance)
            WriteUInt32(result, ref offset, 0);             // dwGBitMask
            WriteUInt32(result, ref offset, 0);             // dwBBitMask
            WriteUInt32(result, ref offset, 0);             // dwABitMask
        }

        // dwCaps, dwCaps2, dwCaps3, dwCaps4, dwReserved2
        WriteUInt32(result, ref offset, DdscapsTexture);
        WriteUInt32(result, ref offset, 0);
        WriteUInt32(result, ref offset, 0);
        WriteUInt32(result, ref offset, 0);
        WriteUInt32(result, ref offset, 0);

        // Pixel data: DDS stores pixels top-to-bottom.
        if (isRgba)
        {
            // Convert RGBA to BGRA byte order for DDS.
            for (var row = 0; row < height; row++)
            {
                var srcRowOffset = row * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var si = srcRowOffset + x * 4;
                    if (si + 3 >= pixelData.Length) continue;

                    result[offset++] = pixelData[si + 2]; // B
                    result[offset++] = pixelData[si + 1]; // G
                    result[offset++] = pixelData[si + 0]; // R
                    result[offset++] = pixelData[si + 3]; // A
                }
            }
        }
        else
        {
            // Grayscale: copy directly.
            Array.Copy(pixelData, 0, result, offset, Math.Min(pixelData.Length, height * pitch));
        }

        return result;
    }

    private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
    {
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
        buffer[offset++] = (byte)((value >> 24) & 0xFF);
    }
}
