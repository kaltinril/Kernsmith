using System.Buffers.Binary;
using System.IO.Compression;
using KernSmith.Font.Woff2;

namespace KernSmith.Font;

/// <summary>
/// Decompresses WOFF and WOFF2 font data to standard TTF/OTF (sfnt) format.
/// </summary>
internal static class WoffDecompressor
{
    private const uint WoffSignature = 0x774F4646; // "wOFF"
    private const uint Woff2Signature = 0x774F4632; // "wOF2"
    private const int WoffHeaderSize = 44;
    private const int WoffTableDirEntrySize = 20;

    /// <summary>Deflate's largest possible expansion: a 258-byte match costs at least two bits.</summary>
    private const long MaxDeflateRatio = 1032;

    /// <summary>
    /// Returns true if the data begins with the WOFF 1.0 signature ("wOFF").
    /// </summary>
    public static bool IsWoff(ReadOnlySpan<byte> data)
        => data.Length >= 4
           && data[0] == (byte)'w'
           && data[1] == (byte)'O'
           && data[2] == (byte)'F'
           && data[3] == (byte)'F';

    /// <summary>
    /// Returns true if the data begins with the WOFF 2.0 signature ("wOF2").
    /// </summary>
    public static bool IsWoff2(ReadOnlySpan<byte> data)
        => data.Length >= 4
           && data[0] == (byte)'w'
           && data[1] == (byte)'O'
           && data[2] == (byte)'F'
           && data[3] == (byte)'2';

    /// <summary>
    /// Decompresses WOFF or WOFF2 data into standard TTF/OTF (sfnt) bytes; a WOFF2 collection
    /// decodes to a TTC. Malformed input throws <see cref="FontParsingException"/>.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> woffData)
    {
        if (woffData.Length < 4)
            throw new FontParsingException("Data is too small to be a WOFF file.");

        var signature = BinaryPrimitives.ReadUInt32BigEndian(woffData);

        return signature switch
        {
            WoffSignature => DecompressWoff1(woffData),
            Woff2Signature => Woff2Decoder.Decompress(woffData),
            _ => throw new FontParsingException(
                $"Not a WOFF file. Expected signature 'wOFF' or 'wOF2', got 0x{signature:X8}.")
        };
    }

    private static byte[] DecompressWoff1(ReadOnlySpan<byte> woffData)
    {
        if (woffData.Length < WoffHeaderSize)
            throw new FontParsingException("WOFF header is too small (need at least 44 bytes).");

        // Parse WOFF header
        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(4));
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(woffData.Slice(12));
        var declaredSfntSize = BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(16));

        const int MaxSfntSize = 100 * 1024 * 1024; // 100 MB
        if (declaredSfntSize > MaxSfntSize)
            throw new FontParsingException(
                $"WOFF totalSfntSize ({declaredSfntSize} bytes) exceeds the maximum allowed size ({MaxSfntSize} bytes).");

        // Validate table directory fits
        var tableDirEnd = WoffHeaderSize + numTables * WoffTableDirEntrySize;
        if (tableDirEnd > woffData.Length)
            throw new FontParsingException(
                $"WOFF table directory extends beyond file (need {tableDirEnd} bytes, have {woffData.Length}).");

        // Read WOFF table directory entries
        var entries = new WoffTableEntry[numTables];
        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = WoffHeaderSize + i * WoffTableDirEntrySize;
            entries[i] = new WoffTableEntry(
                Tag: BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(entryOffset)),
                Offset: (int)BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(entryOffset + 4)),
                CompLength: (int)BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(entryOffset + 8)),
                OrigLength: (int)BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(entryOffset + 12)),
                OrigChecksum: BinaryPrimitives.ReadUInt32BigEndian(woffData.Slice(entryOffset + 16)));

            if (entries[i].Offset < 0 || entries[i].CompLength < 0 || entries[i].OrigLength < 0)
                throw new FontParsingException($"WOFF table at index {i} has invalid size fields.");
        }

        // Build the sfnt output
        // sfnt header: 12 bytes + numTables * 16 bytes for table records
        var sfntHeaderSize = 12 + numTables * 16;

        // Every table is checked before the output is allocated. The output is the size the
        // tables actually fill (directory, then each table 4-byte aligned): a larger
        // totalSfntSize would only add zero padding, so it is never allocated, and a smaller
        // one cannot hold the tables.
        long requiredSize = sfntHeaderSize;
        for (var i = 0; i < numTables; i++)
        {
            var entry = entries[i];
            if ((long)entry.Offset + entry.CompLength > woffData.Length)
                throw new FontParsingException(
                    $"WOFF table at index {i} (tag 0x{entry.Tag:X8}) references data beyond file bounds.");
            if (entry.CompLength < entry.OrigLength && entry.OrigLength > (long)entry.CompLength * MaxDeflateRatio + 1024)
                throw new FontParsingException(
                    $"WOFF table at index {i} (tag 0x{entry.Tag:X8}) cannot inflate from {entry.CompLength} to {entry.OrigLength} bytes.");
            requiredSize = ((requiredSize + 3) & ~3L) + entry.OrigLength;
        }

        if (requiredSize > declaredSfntSize)
            throw new FontParsingException(
                $"WOFF tables need {requiredSize} bytes, more than the header's totalSfntSize ({declaredSfntSize} bytes).");

        var result = new byte[Math.Min(declaredSfntSize, (requiredSize + 3) & ~3L)];

        // Write sfnt Offset Table header
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), numTables);

        // Compute searchRange, entrySelector, rangeShift
        var entrySelector = 0;
        var searchRange = 1;
        while (searchRange * 2 <= numTables)
        {
            searchRange *= 2;
            entrySelector++;
        }
        searchRange *= 16;
        var rangeShift = numTables * 16 - searchRange;

        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10), (ushort)rangeShift);

        // Decompress each table and write data, tracking output offsets
        var dataOffset = sfntHeaderSize;

        for (var i = 0; i < numTables; i++)
        {
            var entry = entries[i];

            // Align data offset to 4-byte boundary
            dataOffset = (dataOffset + 3) & ~3;

            // Decompress or copy table data
            var sourceData = woffData.Slice(entry.Offset, entry.CompLength);
            var destSpan = result.AsSpan(dataOffset, entry.OrigLength);

            if (entry.CompLength < entry.OrigLength)
            {
                // Compressed with zlib — use ZLibStream which handles the zlib header natively
                DecompressZlib(sourceData, destSpan);
            }
            else
            {
                // Uncompressed — straight copy
                sourceData.Slice(0, entry.OrigLength).CopyTo(destSpan);
            }

            // Write sfnt table record (sorted by tag, same order as WOFF)
            var recordOffset = 12 + i * 16;
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(recordOffset), entry.Tag);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(recordOffset + 4), entry.OrigChecksum);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(recordOffset + 8), (uint)dataOffset);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(recordOffset + 12), (uint)entry.OrigLength);

            dataOffset += entry.OrigLength;
        }

        return result;
    }

    private static void DecompressZlib(ReadOnlySpan<byte> compressedData, Span<byte> destination)
    {
        var sourceArray = compressedData.ToArray();
        using var inputStream = new MemoryStream(sourceArray);
        using var zlibStream = new ZLibStream(inputStream, CompressionMode.Decompress);

        var totalRead = 0;
        // ZLibStream does not support Span-based reads directly, use a temporary buffer
        var tempBuffer = new byte[destination.Length];
        try
        {
            while (totalRead < destination.Length)
            {
                var bytesRead = zlibStream.Read(tempBuffer, totalRead, destination.Length - totalRead);
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }
        }
        catch (InvalidDataException ex)
        {
            throw new FontParsingException($"WOFF zlib data is corrupt: {ex.Message}", ex);
        }

        if (totalRead != destination.Length)
            throw new FontParsingException(
                $"WOFF zlib decompression produced {totalRead} bytes, expected {destination.Length}.");

        tempBuffer.AsSpan(0, totalRead).CopyTo(destination);
    }

    private readonly record struct WoffTableEntry(
        uint Tag,
        int Offset,
        int CompLength,
        int OrigLength,
        uint OrigChecksum);
}
