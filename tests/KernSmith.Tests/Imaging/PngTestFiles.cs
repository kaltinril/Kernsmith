using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace KernSmith.Tests.Imaging;

/// <summary>The committed PngSuite and a tiny PNG writer for hand-built test images.</summary>
internal static class PngTestFiles
{
    public static readonly string SuiteDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PngSuite");

    /// <summary>Every PngSuite image, by file name. The suite's corrupt files start with 'x'.</summary>
    public static string[] SuiteFileNames() =>
        Directory.GetFiles(SuiteDir, "*.png")
            .Select(Path.GetFileName)
            .Where(n => !n!.Equals("PngSuite.png", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray()!;

    public static byte[] Read(string fileName) => File.ReadAllBytes(Path.Combine(SuiteDir, fileName));

    public static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>One chunk: length, type, data, CRC over type and data.</summary>
    public static byte[] Chunk(string type, ReadOnlySpan<byte> data, bool corruptCrc = false)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, chunk.AsSpan(4, 4));
        data.CopyTo(chunk.AsSpan(8));
        uint crc = Crc(chunk.AsSpan(4, 4 + data.Length));
        if (corruptCrc)
            crc ^= 1;
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), crc);
        return chunk;
    }

    public static byte[] Ihdr(int width, int height, int bitDepth, int colorType, int interlace = 0)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(data, width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), height);
        data[8] = (byte)bitDepth;
        data[9] = (byte)colorType;
        data[12] = (byte)interlace;
        return Chunk("IHDR", data);
    }

    public static byte[] Idat(ReadOnlySpan<byte> rawScanlines) => Chunk("IDAT", Zlib(rawScanlines));

    public static byte[] Iend() => Chunk("IEND", []);

    public static byte[] Zlib(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);
        return output.ToArray();
    }

    public static byte[] Png(params byte[][] chunks)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        foreach (var chunk in chunks)
            output.Write(chunk);
        return output.ToArray();
    }

    /// <summary>Bit-at-a-time CRC-32 (ISO 3309 / PNG), deliberately not table-driven like the decoder's.</summary>
    public static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    /// <summary>Recomputes every chunk CRC in place, so a mutation reaches the decoder past the CRC check.</summary>
    public static void FixCrcs(byte[] png)
    {
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            if (length > (uint)(png.Length - pos - 12))
                return;
            int len = (int)length;
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(pos + 8 + len), Crc(png.AsSpan(pos + 4, 4 + len)));
            pos += 12 + len;
        }
    }
}
