using System.Buffers.Binary;
using System.IO.Compression;

namespace KernSmith.Rasterizers.Native.Tests.Goldens;

/// <summary>
/// A minimal PNG codec for the golden reference images: 8-bit grayscale or RGBA, no interlace.
/// </summary>
/// <remarks>
/// Goldens compare decoded pixels, never PNG bytes: the deflate output differs between runtimes
/// (.NET 9 moved to zlib-ng), so the same pixels can encode to different files.
/// </remarks>
internal static class GoldenPng
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes <paramref name="image"/> (one byte per channel, rows top to bottom).</summary>
    public static byte[] Encode(GoldenImage image)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;                                   // bit depth
        header[9] = (byte)(image.Channels == 4 ? 6 : 0); // RGBA or grayscale
        WriteChunk(output, "IHDR", header);

        int stride = image.Width * image.Channels;
        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                for (int y = 0; y < image.Height; y++)
                {
                    zlib.WriteByte(0); // filter: none
                    zlib.Write(image.Pixels, y * stride, stride);
                }
            }

            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Decodes an 8-bit grayscale or RGBA, non-interlaced PNG.</summary>
    public static GoldenImage Decode(byte[] png)
    {
        if (!png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("not a PNG");

        int width = 0, height = 0, channels = 0;
        using var idat = new MemoryStream();
        int offset = Signature.Length;
        while (offset + 8 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 8 || data[12] != 0)
                    throw new InvalidDataException("only 8-bit, non-interlaced PNGs are supported");

                channels = data[9] switch
                {
                    0 => 1,
                    6 => 4,
                    _ => throw new InvalidDataException($"unsupported PNG color type {data[9]}"),
                };
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }

            offset += 12 + length;
        }

        int stride = width * channels;
        var filtered = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
            zlib.ReadExactly(filtered);

        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = filtered[y * (stride + 1)];
            var source = filtered.AsSpan((y * (stride + 1)) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            var above = y > 0 ? pixels.AsSpan((y - 1) * stride, stride) : new byte[stride];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? row[x - channels] : 0;
                int b = above[x];
                int c = x >= channels ? above[x - channels] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"unknown PNG filter {filter}"),
                };
                row[x] = (byte)(source[x] + predictor);
            }
        }

        return new GoldenImage(width, height, channels, pixels);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = 0xFFFFFFFFu;
        crc = Crc(crc, typeBytes);
        crc = Crc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ 0xFFFFFFFFu);
        output.Write(word);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }
}
