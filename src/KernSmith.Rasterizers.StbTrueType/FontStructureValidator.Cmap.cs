namespace KernSmith.Rasterizers.StbTrueType;

internal sealed partial class FontStructureValidator
{
    /// <summary>
    /// Checks the <c>cmap</c> header and every encoding record's subtable offset, then the
    /// subtable stb_truetype selects: the last record for platform 0, or platform 3 encoding 1
    /// or 10. Formats 0, 4, 6, 12 and 13 — the ones stb reads beyond the format field — must
    /// fit their declared length, and that length must fit the table. Other formats map nothing
    /// in stb, so only their format field is read.
    /// </summary>
    private void ValidateCmap()
    {
        int c = _cmap.Offset;
        int length = _cmap.Length;
        if (length < 4)
            throw Fail("cmap", c, $"table is {length} bytes, shorter than its 4-byte header.");

        int numTables = U16(c + 2);
        if (4L + (8L * numTables) > length)
            throw Fail("cmap", c + 2, $"{numTables} encoding records run past the table ({length} bytes).");

        long selected = -1;
        for (int i = 0; i < numTables; i++)
        {
            int record = c + 4 + (8 * i);
            int platform = U16(record);
            int encoding = U16(record + 2);
            uint offset = U32(record + 4);
            if (offset > length - 2L)
                throw Fail("cmap", record + 4,
                    $"encoding record {i} ({platform},{encoding}) points its subtable outside the table (offset {offset}).");
            if (platform == 0 || (platform == 3 && encoding is 1 or 10))
                selected = offset;
        }

        if (selected < 0)
            throw Fail("cmap", c, "no Unicode subtable (platform 0, or platform 3 encoding 1 or 10).");

        ValidateCmapSubtable(c + (int)selected, length - (int)selected);
    }

    private void ValidateCmapSubtable(int sub, int available)
    {
        int format = U16(sub);
        switch (format)
        {
            case 0:
                {
                    int length = SubtableLength16(sub, available, 6);
                    if (length < 6)
                        throw Fail("cmap", sub + 2, $"format 0 length {length} is shorter than its 6-byte header.");
                    break;
                }

            case 6:
                {
                    int length = SubtableLength16(sub, available, 10);
                    int entryCount = U16(sub + 8);
                    if (10L + (2L * entryCount) > length)
                        throw Fail("cmap", sub + 8, $"format 6 entryCount {entryCount} runs past the subtable length {length}.");
                    break;
                }

            case 4:
                ValidateFormat4(sub, available);
                break;

            case 12:
            case 13:
                {
                    if (available < 16)
                        throw Fail("cmap", sub, $"format {format} header runs past the cmap table.");
                    uint length = U32(sub + 4);
                    if (length > available)
                        throw Fail("cmap", sub + 4, $"format {format} subtable length runs past the cmap table (length {length}, {available} bytes left).");
                    uint numGroups = U32(sub + 12);
                    if (16L + (12L * numGroups) > length)
                        throw Fail("cmap", sub + 12, $"format {format}: {numGroups} groups run past the subtable length {length}.");

                    long previousEnd = -1;
                    for (int g = 0; g < numGroups; g++)
                    {
                        int group = sub + 16 + (12 * g);
                        uint start = U32(group);
                        uint end = U32(group + 4);
                        if (start > end)
                            throw Fail("cmap", group, $"format {format} group {g}: startCharCode {start} is after endCharCode {end}.");
                        if (start <= previousEnd)
                            throw Fail("cmap", group, $"format {format} group {g} is out of order or overlaps the previous group.");
                        previousEnd = end;
                    }

                    break;
                }
        }
    }

    /// <summary>Reads a 16-bit subtable length, after checking the fixed header fits.</summary>
    private int SubtableLength16(int sub, int available, int header)
    {
        int format = U16(sub);
        if (available < header)
            throw Fail("cmap", sub, $"format {format} header runs past the cmap table.");
        int length = U16(sub + 2);
        if (length > available)
            throw Fail("cmap", sub + 2, $"format {format} subtable length runs past the cmap table (length {length}, {available} bytes left).");
        return length;
    }

    /// <summary>
    /// Format 4: stb_truetype binary-searches the endCode array with the subtable's own
    /// searchRange, entrySelector and rangeShift, so those must be the values the spec derives
    /// from segCountX2; segments must be ordered and closed by 0xFFFF; and every glyphIdArray
    /// entry an idRangeOffset reaches must lie inside the subtable.
    /// </summary>
    private void ValidateFormat4(int sub, int available)
    {
        int length = SubtableLength16(sub, available, 14);
        int segCountX2 = U16(sub + 6);
        if (segCountX2 == 0 || (segCountX2 & 1) != 0)
            throw Fail("cmap", sub + 6, $"format 4 segCountX2 {segCountX2} is not a positive even number.");
        int segCount = segCountX2 / 2;
        if (16L + (8L * segCount) > length)
            throw Fail("cmap", sub + 6, $"format 4 segment arrays run past the subtable length {length}.");

        int power = 1;
        while (power * 2 <= segCount)
            power *= 2;
        int searchRange = U16(sub + 8);
        int entrySelector = U16(sub + 10);
        int rangeShift = U16(sub + 12);
        if (searchRange != 2 * power || entrySelector != Log2(power) || rangeShift != segCountX2 - (2 * power))
            throw Fail("cmap", sub + 8,
                $"format 4 searchRange {searchRange}, entrySelector {entrySelector} and rangeShift {rangeShift} disagree "
                + $"with segCountX2 {segCountX2} (expected {2 * power}, {Log2(power)}, {segCountX2 - (2 * power)}).");

        int endCodes = sub + 14;
        int startCodes = endCodes + segCountX2 + 2;
        int idRangeOffsets = startCodes + (2 * segCountX2);
        int previousEnd = -1;
        for (int i = 0; i < segCount; i++)
        {
            int end = U16(endCodes + (2 * i));
            int start = U16(startCodes + (2 * i));
            if (start > end)
                throw Fail("cmap", startCodes + (2 * i), $"format 4 segment {i}: startCode {start} is after endCode {end}.");
            if (start <= previousEnd)
                throw Fail("cmap", startCodes + (2 * i), $"format 4 segment {i} is out of order or overlaps the previous segment.");
            previousEnd = end;

            int rangeOffsetAt = idRangeOffsets + (2 * i);
            int rangeOffset = U16(rangeOffsetAt);
            if (rangeOffset != 0 && (long)rangeOffsetAt + rangeOffset + (2L * (end - start)) + 2 > sub + (long)length)
                throw Fail("cmap", rangeOffsetAt,
                    $"format 4 segment {i}: idRangeOffset {rangeOffset} reaches past the subtable length {length}.");
        }

        if (previousEnd != 0xFFFF)
            throw Fail("cmap", endCodes + segCountX2 - 2, $"format 4 last segment ends at {previousEnd}, not 0xFFFF.");
    }

    private static int Log2(int power) => System.Numerics.BitOperations.Log2((uint)power);
}
