using System.Buffers.Binary;

namespace KernSmith.Atlas;

/// <summary>
/// The CRC-32 PNG chunks carry (reflected polynomial 0xEDB88320, register preset to all ones and
/// inverted at the end), eight bytes per step through eight derived tables ("slicing-by-8").
/// </summary>
internal static class PngCrc32
{
    // Tables[k][b] is the CRC of byte b followed by k zero bytes; Tables[0] is the classic table.
    private static readonly uint[] Tables = BuildTables();

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var t = Tables;
        uint crc = 0xFFFFFFFFu;

        while (bytes.Length >= 8)
        {
            uint low = BinaryPrimitives.ReadUInt32LittleEndian(bytes) ^ crc;
            uint high = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
            crc = t[(7 * 256) + (low & 0xFF)]
                ^ t[(6 * 256) + ((low >> 8) & 0xFF)]
                ^ t[(5 * 256) + ((low >> 16) & 0xFF)]
                ^ t[(4 * 256) + (low >> 24)]
                ^ t[(3 * 256) + (high & 0xFF)]
                ^ t[(2 * 256) + ((high >> 8) & 0xFF)]
                ^ t[256 + ((high >> 16) & 0xFF)]
                ^ t[high >> 24];
            bytes = bytes[8..];
        }

        foreach (byte b in bytes)
            crc = t[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return ~crc;
    }

    private static uint[] BuildTables()
    {
        var t = new uint[8 * 256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }

        for (int k = 1; k < 8; k++)
            for (int n = 0; n < 256; n++)
            {
                uint previous = t[((k - 1) * 256) + n];
                t[(k * 256) + n] = t[previous & 0xFF] ^ (previous >> 8);
            }

        return t;
    }
}
