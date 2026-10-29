using System.Buffers.Binary;

namespace KernSmith.Font.Woff2;

/// <summary>
/// Reverses the WOFF2 <c>hmtx</c> transform (W3C WOFF2 section 5.4, transform version 1):
/// left side bearings the encoder dropped are restored from each glyph's <c>glyf</c> xMin
/// (0 for an empty glyph).
/// </summary>
internal static class Woff2HmtxTransform
{
    private const byte LsbArrayOmitted = 0x01;
    private const byte LeftSideBearingArrayOmitted = 0x02;

    /// <param name="transformed">The transformed hmtx bytes.</param>
    /// <param name="numGlyphs">Glyph count from <c>maxp</c>.</param>
    /// <param name="numberOfHMetrics">From <c>hhea</c>.</param>
    /// <param name="glyf">The final (reconstructed or stored) <c>glyf</c> table.</param>
    /// <param name="loca">The final <c>loca</c> table.</param>
    /// <param name="indexFormat">The format <paramref name="loca"/> is written in.</param>
    public static byte[] Reconstruct(
        ReadOnlySpan<byte> transformed, int numGlyphs, int numberOfHMetrics,
        ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, int indexFormat)
    {
        if (numberOfHMetrics < 1 || numberOfHMetrics > numGlyphs)
            throw Woff2Decoder.Invalid($"hhea.numberOfHMetrics ({numberOfHMetrics}) is out of range for {numGlyphs} glyphs");

        var reader = new Woff2Reader(transformed, "transformed hmtx table");
        var flags = reader.ReadByte();
        if ((flags & ~(LsbArrayOmitted | LeftSideBearingArrayOmitted)) != 0)
            throw Woff2Decoder.Invalid("transformed hmtx sets reserved flag bits");
        if ((flags & (LsbArrayOmitted | LeftSideBearingArrayOmitted)) == 0)
            throw Woff2Decoder.Invalid("transformed hmtx omits neither side-bearing array");

        var monospacedCount = numGlyphs - numberOfHMetrics;
        var advances = reader.ReadBytes(numberOfHMetrics * 2);
        var hasLsbs = (flags & LsbArrayOmitted) == 0;
        var hasLeftSideBearings = (flags & LeftSideBearingArrayOmitted) == 0;
        var lsbs = hasLsbs ? reader.ReadBytes(numberOfHMetrics * 2) : default;
        var leftSideBearings = hasLeftSideBearings ? reader.ReadBytes(monospacedCount * 2) : default;
        if (reader.Remaining != 0)
            throw Woff2Decoder.Invalid("transformed hmtx has trailing data");

        var hmtx = new byte[numberOfHMetrics * 4 + monospacedCount * 2];
        for (var i = 0; i < numberOfHMetrics; i++)
        {
            advances.Slice(i * 2, 2).CopyTo(hmtx.AsSpan(i * 4));
            var lsb = hasLsbs
                ? BinaryPrimitives.ReadInt16BigEndian(lsbs.Slice(i * 2))
                : XMin(glyf, loca, indexFormat, i);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(i * 4 + 2), lsb);
        }

        for (var i = 0; i < monospacedCount; i++)
        {
            var glyphId = numberOfHMetrics + i;
            var lsb = hasLeftSideBearings
                ? BinaryPrimitives.ReadInt16BigEndian(leftSideBearings.Slice(i * 2))
                : XMin(glyf, loca, indexFormat, glyphId);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(numberOfHMetrics * 4 + i * 2), lsb);
        }

        return hmtx;
    }

    /// <summary>The glyph's xMin, or 0 for an empty glyph.</summary>
    private static short XMin(ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, int indexFormat, int glyphId)
    {
        int start, end;
        if (indexFormat == 0)
        {
            if ((glyphId + 2) * 2 > loca.Length)
                throw Woff2Decoder.Invalid("loca is too short for the hmtx glyph count");
            start = BinaryPrimitives.ReadUInt16BigEndian(loca.Slice(glyphId * 2)) * 2;
            end = BinaryPrimitives.ReadUInt16BigEndian(loca.Slice(glyphId * 2 + 2)) * 2;
        }
        else
        {
            if ((glyphId + 2) * 4 > loca.Length)
                throw Woff2Decoder.Invalid("loca is too short for the hmtx glyph count");
            start = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(loca.Slice(glyphId * 4)), int.MaxValue);
            end = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(loca.Slice(glyphId * 4 + 4)), int.MaxValue);
        }

        if (end <= start)
            return 0;
        if (start + 4 > glyf.Length)
            throw Woff2Decoder.Invalid("loca points past the end of glyf");
        return BinaryPrimitives.ReadInt16BigEndian(glyf.Slice(start + 2));
    }
}
