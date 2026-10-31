using System.Buffers.Binary;
using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// A malformed WOFF 1.0 file is a <see cref="FontParsingException"/> — never an overflow,
/// index or <see cref="System.IO.InvalidDataException"/> — and a small file cannot make the
/// decoder allocate far more than its tables can actually decompress to.
/// </summary>
public class WoffHostileInputTests
{
    [Fact]
    public void TotalSfntSizeWithTheHighBitSet_ThrowsFontParsingException()
    {
        var woff = Woff1(totalSfntSize: 0x8000_0000);

        Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(woff));
    }

    [Fact]
    public void TotalSfntSizeSmallerThanTheTableDirectory_ThrowsFontParsingException()
    {
        var woff = Woff1(totalSfntSize: 0, tables: [Table("head", new byte[54])]);

        Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(woff));
    }

    [Fact]
    public void CorruptZlibData_ThrowsFontParsingException()
    {
        // compLength < origLength marks the table compressed; the bytes are not a zlib stream.
        var woff = Woff1(totalSfntSize: 12 + 16 + 100, tables: [Table("head", new byte[20], origLength: 100)]);

        Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(woff));
    }

    [Fact]
    public void HugeTotalSfntSizeOnATinyFile_DoesNotAllocateIt()
    {
        // A 44-byte header claiming a 100 MB font with no tables.
        var woff = Woff1(totalSfntSize: 100 * 1024 * 1024);

        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            WoffDecompressor.Decompress(woff);
        }
        catch (FontParsingException)
        {
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1_000_000);
    }

    [Fact]
    public void CompressedTableClaimingMoreThanDeflateCanExpandTo_IsRejectedBeforeAllocating()
    {
        // 20 compressed bytes cannot inflate to 90 MB (deflate expands at most ~1032x).
        const int OrigLength = 90 * 1024 * 1024;
        var woff = Woff1(totalSfntSize: 12 + 16 + OrigLength, tables: [Table("head", new byte[20], origLength: OrigLength)]);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(woff));

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1_000_000);
    }

    private static (string Tag, byte[] Data, int OrigLength) Table(string tag, byte[] data, int? origLength = null) =>
        (tag, data, origLength ?? data.Length);

    /// <summary>A WOFF 1.0 file with the given (stored) tables and header totalSfntSize.</summary>
    private static byte[] Woff1(uint totalSfntSize, params (string Tag, byte[] Data, int OrigLength)[] tables)
    {
        int dataStart = 44 + (20 * tables.Length);
        int length = dataStart + tables.Sum(t => (t.Data.Length + 3) & ~3);
        var woff = new byte[length];
        "wOFF"u8.CopyTo(woff);
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(8), (uint)length);
        BinaryPrimitives.WriteUInt16BigEndian(woff.AsSpan(12), (ushort)tables.Length);
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(16), totalSfntSize);

        int offset = dataStart;
        for (int i = 0; i < tables.Length; i++)
        {
            var entry = woff.AsSpan(44 + (20 * i));
            System.Text.Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tables[i].Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)tables[i].OrigLength);
            tables[i].Data.CopyTo(woff, offset);
            offset += (tables[i].Data.Length + 3) & ~3;
        }

        return woff;
    }
}
