using System.Buffers.Binary;
using System.IO.Compression;

namespace KernSmith.Atlas;

/// <summary>
/// Encodes atlas pixel data to PNG: 8-bit grayscale or RGBA, non-interlaced, one IDAT chunk
/// deflated by the runtime's zlib.
/// </summary>
/// <remarks>
/// Every row uses filter type 0 (None). On atlas pages (mostly empty background between glyphs,
/// color emoji and gradient pages included) that compresses smaller than per-row adaptive filtering
/// and costs nothing to choose; measured against StbImageWriteSharp's adaptive filters and own
/// deflate, the output is 16-42% smaller and 3-6x faster to produce.
/// The bytes depend on the runtime's zlib (.NET 9 moved to zlib-ng), so the same pixels can encode
/// to different files on different runtimes; the decoded pixels never differ.
/// </remarks>
internal sealed class PngEncoder : IAtlasEncoder
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public string FileExtension => ".png";

    public byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(pixelData);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var (bytesPerPixel, colorType) = format switch
        {
            PixelFormat.Grayscale8 => (1, (byte)0),
            PixelFormat.Rgba32 => (4, (byte)6),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format.")
        };

        long stride = (long)width * bytesPerPixel;
        if (stride * height > pixelData.Length)
            throw new ArgumentException(
                $"pixelData length {pixelData.Length} is less than expected {stride * height} for {width}x{height} at {bytesPerPixel} bytes/pixel.",
                nameof(pixelData));

        // Atlas pages are mostly background, so the deflated image is typically 5-30% of raw.
        using var output = new MemoryStream((int)Math.Min(int.MaxValue, 1024 + stride * height / 4));
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;          // bit depth
        header[9] = colorType;  // 0 = grayscale, 6 = RGBA
        header[10] = 0;         // compression method: deflate
        header[11] = 0;         // filter method: adaptive (every row uses type 0)
        header[12] = 0;         // no interlace
        WriteChunk(output, "IHDR"u8, header);

        // IDAT: length placeholder and type, then the zlib stream straight into the output.
        long idatStart = output.Position;
        output.Write(stackalloc byte[4]);
        output.Write("IDAT"u8);
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            int rowLength = (int)stride;
            ReadOnlySpan<byte> filterNone = [0];
            for (int y = 0; y < height; y++)
            {
                zlib.Write(filterNone);
                zlib.Write(pixelData, y * rowLength, rowLength);
            }
        }

        long dataLength = output.Position - idatStart - 8;
        if (dataLength > int.MaxValue)
            throw new InvalidOperationException("The compressed image is too large for a single PNG chunk.");

        var buffer = output.GetBuffer();
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan((int)idatStart, 4), (int)dataLength);
        WriteCrc(output, buffer.AsSpan((int)idatStart + 4, 4 + (int)dataLength));

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(MemoryStream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);

        int start = (int)output.Position;
        output.Write(type);
        output.Write(data);
        WriteCrc(output, output.GetBuffer().AsSpan(start, type.Length + data.Length));
    }

    private static void WriteCrc(MemoryStream output, ReadOnlySpan<byte> typeAndData)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, PngCrc32.Compute(typeAndData));
        output.Write(word);
    }
}
