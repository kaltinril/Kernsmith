namespace KernSmith.Imaging.Internal;

/// <summary>
/// The CRC-32 PNG chunks carry (ISO 3309 / ITU-T V.42: polynomial 0x04C11DB7, processed
/// least-significant bit first, so the reflected constant 0xEDB88320; register preset to all ones
/// and inverted at the end), table-driven one byte at a time.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>The CRC of <paramref name="bytes"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFFu;
        var table = Table;
        foreach (byte b in bytes)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildTable()
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
