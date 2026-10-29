using System.Buffers.Binary;

namespace KernSmith.Imaging.Internal;

/// <summary>
/// How one image's samples map to RGBA: the header's layout plus its <c>PLTE</c> and
/// <c>tRNS</c> data.
/// </summary>
internal sealed class PixelFormat
{
    private readonly PngHeader _header;

    public PixelFormat(PngHeader header)
    {
        _header = header;
        Channels = header.ColorType switch
        {
            PngColorType.Grayscale or PngColorType.Indexed => 1,
            PngColorType.GrayscaleAlpha => 2,
            PngColorType.Truecolor => 3,
            _ => 4,
        };
        BitsPerPixel = Channels * header.BitDepth;
    }

    /// <summary>Samples per pixel as stored (a palette index counts as one).</summary>
    public int Channels { get; }

    /// <summary>Bits per stored pixel.</summary>
    public int BitsPerPixel { get; }

    /// <summary>The filter's byte distance to the corresponding byte of the previous pixel (at least 1).</summary>
    public int FilterStride => Math.Max(1, BitsPerPixel / 8);

    /// <summary>Palette entries as RGBA, alpha from <c>tRNS</c>; only for indexed-color images.</summary>
    public byte[]? Palette { get; private set; }

    /// <summary>The number of valid <see cref="Palette"/> entries.</summary>
    public int PaletteCount { get; private set; }

    public bool HasPalette => PaletteCount > 0;

    /// <summary>True when a gray or truecolor sample value is marked fully transparent.</summary>
    public bool HasTransparentColor { get; private set; }

    /// <summary>The transparent gray sample, or red, green and blue, at the image's bit depth.</summary>
    public int TransparentGray { get; private set; }
    public int TransparentRed { get; private set; }
    public int TransparentGreen { get; private set; }
    public int TransparentBlue { get; private set; }

    /// <summary>
    /// Validates a <c>PLTE</c> chunk; keeps it for indexed-color images (for truecolor it is only
    /// a quantization suggestion).
    /// </summary>
    /// <exception cref="PngFormatException">If the chunk is repeated, malformed or not allowed for the color type.</exception>
    public void SetPalette(ReadOnlySpan<byte> body)
    {
        if (_header.ColorType is PngColorType.Grayscale or PngColorType.GrayscaleAlpha)
            throw new PngFormatException("A grayscale PNG must not have a PLTE chunk.");
        if (_paletteSeen)
            throw new PngFormatException("The PNG has more than one PLTE chunk.");
        _paletteSeen = true;

        if (body.Length == 0 || body.Length % 3 != 0 || body.Length > 256 * 3)
            throw new PngFormatException($"The PLTE chunk is {body.Length} bytes; it must hold 1 to 256 three-byte entries.");

        int count = body.Length / 3;
        if (_header.ColorType != PngColorType.Indexed)
            return;
        if (count > 1 << _header.BitDepth)
            throw new PngFormatException($"The PLTE chunk has {count} entries, more than a {_header.BitDepth}-bit index can address.");

        var palette = new byte[256 * 4];
        for (int i = 0; i < count; i++)
        {
            palette[i * 4] = body[i * 3];
            palette[(i * 4) + 1] = body[(i * 3) + 1];
            palette[(i * 4) + 2] = body[(i * 3) + 2];
            palette[(i * 4) + 3] = 255;
        }

        Palette = palette;
        PaletteCount = count;
    }

    private bool _paletteSeen;

    /// <summary>
    /// Applies a <c>tRNS</c> chunk when it is well formed for the color type; otherwise ignores it,
    /// as the spec allows for an ancillary chunk with unexpected contents.
    /// </summary>
    public void TrySetTransparency(ReadOnlySpan<byte> body)
    {
        int mask = (1 << _header.BitDepth) - 1;
        switch (_header.ColorType)
        {
            case PngColorType.Indexed:
                if (Palette is null || body.Length > PaletteCount)
                    return;
                for (int i = 0; i < body.Length; i++)
                    Palette[(i * 4) + 3] = body[i];
                return;

            case PngColorType.Grayscale:
                if (body.Length != 2)
                    return;
                // Only the low bit-depth bits of the two-byte sample count.
                TransparentGray = BinaryPrimitives.ReadUInt16BigEndian(body) & mask;
                HasTransparentColor = true;
                return;

            case PngColorType.Truecolor:
                if (body.Length != 6)
                    return;
                TransparentRed = BinaryPrimitives.ReadUInt16BigEndian(body) & mask;
                TransparentGreen = BinaryPrimitives.ReadUInt16BigEndian(body[2..]) & mask;
                TransparentBlue = BinaryPrimitives.ReadUInt16BigEndian(body[4..]) & mask;
                HasTransparentColor = true;
                return;

            default:
                // Prohibited with an alpha channel; ignored.
                return;
        }
    }
}
