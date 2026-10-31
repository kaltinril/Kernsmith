using System.Buffers.Binary;
using KernSmith.Font.Models;

namespace KernSmith.Tests.Font;

/// <summary>
/// Frozen copy of the straightforward GPOS PairPos reader (linear coverage scans, full
/// ClassDef dictionaries, per-pair subset filter). <see cref="GposEquivalenceTests"/> holds
/// <see cref="KernSmith.Font.TtfParser.GposPairs"/> to exactly this list — same pairs, same
/// order. Do not "optimise" this copy — it is the oracle.
/// </summary>
internal static class ReferenceGposParser
{
    private const int MaxGlyphIds = 65536;

    public static List<KerningPair> Parse(ReadOnlySpan<byte> table, HashSet<int>? relevant)
    {
        var pairs = new List<KerningPair>();
        if (table.IsEmpty || table.Length < 10)
            return pairs;

        var featureListOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6));
        var lookupListOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(8));
        var kernLookupIndices = FindKernFeatureLookups(table, featureListOffset);
        if (kernLookupIndices.Count == 0)
            return pairs;

        var lookupList = From(table, lookupListOffset);
        if (lookupList.Length < 2)
            return pairs;

        var lookupCount = BinaryPrimitives.ReadUInt16BigEndian(lookupList);
        foreach (var lookupIndex in kernLookupIndices)
        {
            if (lookupIndex >= lookupCount || 2 + (lookupIndex + 1) * 2 > lookupList.Length)
                continue;
            var lookupOffset = BinaryPrimitives.ReadUInt16BigEndian(lookupList.Slice(2 + lookupIndex * 2));
            if (lookupOffset >= lookupList.Length)
                continue;
            var lookup = lookupList.Slice(lookupOffset);
            if (lookup.Length < 6)
                continue;

            var lookupType = BinaryPrimitives.ReadUInt16BigEndian(lookup);
            var subTableCount = BinaryPrimitives.ReadUInt16BigEndian(lookup.Slice(4));
            for (var s = 0; s < subTableCount; s++)
            {
                if (6 + (s + 1) * 2 > lookup.Length)
                    break;
                var subTableOffset = BinaryPrimitives.ReadUInt16BigEndian(lookup.Slice(6 + s * 2));
                var subtable = From(lookup, subTableOffset);
                var effectiveType = lookupType;
                if (effectiveType == 9)
                {
                    if (subtable.Length < 8)
                        continue;
                    effectiveType = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(2));
                    var extensionOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(4));
                    if (extensionOffset < 0 || (long)subTableOffset + extensionOffset >= lookup.Length)
                        continue;
                    subtable = lookup.Slice(subTableOffset + extensionOffset);
                }

                if (effectiveType == 2)
                    ParsePairPos(subtable, pairs, relevant);
            }
        }

        return pairs;
    }

    private static List<int> FindKernFeatureLookups(ReadOnlySpan<byte> table, int featureListOffset)
    {
        var result = new List<int>();
        var featureList = From(table, featureListOffset);
        if (featureList.Length < 2)
            return result;

        var featureCount = BinaryPrimitives.ReadUInt16BigEndian(featureList);
        const uint kernTag = ('k' << 24) | ('e' << 16) | ('r' << 8) | 'n';
        for (var i = 0; i < featureCount; i++)
        {
            var recOffset = 2 + i * 6;
            if (recOffset + 6 > featureList.Length)
                break;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(featureList.Slice(recOffset));
            if (tag != kernTag)
                continue;
            var offset = BinaryPrimitives.ReadUInt16BigEndian(featureList.Slice(recOffset + 4));
            var featureTable = From(featureList, offset);
            if (featureTable.Length < 4)
                continue;
            var lookupCount = BinaryPrimitives.ReadUInt16BigEndian(featureTable.Slice(2));
            for (var j = 0; j < lookupCount; j++)
            {
                if (4 + (j + 1) * 2 > featureTable.Length)
                    break;
                result.Add(BinaryPrimitives.ReadUInt16BigEndian(featureTable.Slice(4 + j * 2)));
            }
        }

        return result;
    }

    private static void ParsePairPos(ReadOnlySpan<byte> subtable, List<KerningPair> pairs, HashSet<int>? relevant)
    {
        if (subtable.Length < 10)
            return;

        var posFormat = BinaryPrimitives.ReadUInt16BigEndian(subtable);
        var coverageOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(2));
        var valueFormat1 = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(4));
        var valueFormat2 = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(6));
        var valueRecord1Size = ValueRecordSize(valueFormat1);
        var valueRecord2Size = ValueRecordSize(valueFormat2);

        if (posFormat == 1)
            ParsePairPosFormat1(subtable, coverageOffset, valueFormat1, valueRecord1Size, valueRecord2Size, pairs, relevant);
        else if (posFormat == 2)
            ParsePairPosFormat2(subtable, coverageOffset, valueFormat1, valueRecord1Size, valueRecord2Size, pairs, relevant);
    }

    private static void ParsePairPosFormat1(
        ReadOnlySpan<byte> subtable, int coverageOffset,
        int valueFormat1, int valueRecord1Size, int valueRecord2Size,
        List<KerningPair> pairs, HashSet<int>? relevant)
    {
        if (subtable.Length < 10)
            return;

        var pairSetCount = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(8));
        var coverage = ParseCoverage(From(subtable, coverageOffset));

        for (var i = 0; i < pairSetCount; i++)
        {
            if (10 + (i + 1) * 2 > subtable.Length)
                break;
            var pairSetOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(10 + i * 2));
            var pairSet = From(subtable, pairSetOffset);
            if (pairSet.Length < 2)
                continue;

            var pairValueCount = BinaryPrimitives.ReadUInt16BigEndian(pairSet);
            var recordSize = 2 + valueRecord1Size + valueRecord2Size;

            int firstGlyph = -1;
            foreach (var (glyph, index) in coverage)
            {
                if (index == i)
                {
                    firstGlyph = glyph;
                    break;
                }
            }

            if (firstGlyph < 0)
                continue;

            for (var p = 0; p < pairValueCount; p++)
            {
                var recOffset = 2 + p * recordSize;
                if (recOffset + recordSize > pairSet.Length)
                    break;
                var secondGlyph = BinaryPrimitives.ReadUInt16BigEndian(pairSet.Slice(recOffset));
                var xAdvance = ReadXAdvanceFromValueRecord(pairSet.Slice(recOffset + 2), valueFormat1);
                if (xAdvance != 0)
                {
                    if (relevant != null && (!relevant.Contains(firstGlyph) || !relevant.Contains(secondGlyph)))
                        continue;
                    pairs.Add(new KerningPair(firstGlyph, secondGlyph, xAdvance));
                }
            }
        }
    }

    private static void ParsePairPosFormat2(
        ReadOnlySpan<byte> subtable, int coverageOffset,
        int valueFormat1, int valueRecord1Size, int valueRecord2Size,
        List<KerningPair> pairs, HashSet<int>? relevant)
    {
        if (subtable.Length < 16)
            return;

        var classDef1Offset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(8));
        var classDef2Offset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(10));
        var class1Count = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(12));
        var class2Count = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(14));

        var coverage = ParseCoverage(From(subtable, coverageOffset));
        var classDef1 = ParseClassDef(From(subtable, classDef1Offset));
        var classDef2 = ParseClassDef(From(subtable, classDef2Offset));

        var class1ToGlyphs = new Dictionary<int, List<int>>();
        foreach (var (glyph, _) in coverage)
        {
            var cls = classDef1.TryGetValue(glyph, out var c) ? c : 0;
            if (!class1ToGlyphs.TryGetValue(cls, out var list))
            {
                list = new List<int>();
                class1ToGlyphs[cls] = list;
            }
            list.Add(glyph);
        }

        var class2ToGlyphs = new Dictionary<int, List<int>>();
        var class2AssignedGlyphs = new HashSet<int>();
        foreach (var (glyph, cls) in classDef2)
        {
            if (!class2ToGlyphs.TryGetValue(cls, out var list))
            {
                list = new List<int>();
                class2ToGlyphs[cls] = list;
            }
            list.Add(glyph);
            class2AssignedGlyphs.Add(glyph);
        }

        if (relevant != null && class2Count > 0)
        {
            if (!class2ToGlyphs.TryGetValue(0, out var class0List))
            {
                class0List = new List<int>();
                class2ToGlyphs[0] = class0List;
            }
            foreach (var glyph in relevant)
            {
                if (!class2AssignedGlyphs.Contains(glyph))
                    class0List.Add(glyph);
            }
        }

        var recordPairSize = valueRecord1Size + valueRecord2Size;
        var arrayStart = 16;

        for (var c1 = 0; c1 < class1Count; c1++)
        {
            if (!class1ToGlyphs.TryGetValue(c1, out var leftGlyphs))
                continue;

            for (var c2 = 0; c2 < class2Count; c2++)
            {
                var recordOffset = arrayStart + (c1 * class2Count + c2) * recordPairSize;
                if (recordOffset + recordPairSize > subtable.Length)
                    break;

                var xAdvance = ReadXAdvanceFromValueRecord(subtable.Slice(recordOffset), valueFormat1);
                if (xAdvance == 0)
                    continue;

                if (!class2ToGlyphs.TryGetValue(c2, out var rightGlyphs))
                    continue;

                foreach (var left in leftGlyphs)
                {
                    if (relevant != null && !relevant.Contains(left))
                        continue;
                    foreach (var right in rightGlyphs)
                    {
                        if (relevant != null && !relevant.Contains(right))
                            continue;
                        pairs.Add(new KerningPair(left, right, xAdvance));
                    }
                }
            }
        }
    }

    private static ReadOnlySpan<byte> From(ReadOnlySpan<byte> span, int offset) =>
        (uint)offset <= (uint)span.Length ? span.Slice(offset) : ReadOnlySpan<byte>.Empty;

    private static List<(int glyph, int index)> ParseCoverage(ReadOnlySpan<byte> data)
    {
        var result = new List<(int glyph, int index)>();
        if (data.Length < 4)
            return result;

        var format = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (format == 1)
        {
            var count = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            for (var i = 0; i < count; i++)
            {
                if (4 + (i + 1) * 2 > data.Length)
                    break;
                result.Add((BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4 + i * 2)), i));
            }
        }
        else if (format == 2)
        {
            var rangeCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            for (var i = 0; i < rangeCount; i++)
            {
                var recOffset = 4 + i * 6;
                if (recOffset + 6 > data.Length)
                    break;
                int startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset));
                int endGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 2));
                int startIndex = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 4));
                for (var g = startGlyph; g <= endGlyph && result.Count < MaxGlyphIds; g++)
                    result.Add((g, startIndex + (g - startGlyph)));
            }
        }

        return result;
    }

    private static Dictionary<int, int> ParseClassDef(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<int, int>();
        if (data.Length < 4)
            return result;

        var format = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (format == 1)
        {
            if (data.Length < 6)
                return result;
            var startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
            for (var i = 0; i < glyphCount; i++)
            {
                if (6 + (i + 1) * 2 > data.Length)
                    break;
                result[startGlyph + i] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6 + i * 2));
            }
        }
        else if (format == 2)
        {
            var visited = 0;
            var classRangeCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            for (var i = 0; i < classRangeCount; i++)
            {
                var recOffset = 4 + i * 6;
                if (recOffset + 6 > data.Length)
                    break;
                int startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset));
                int endGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 2));
                var classValue = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 4));
                for (var g = startGlyph; g <= endGlyph && visited < MaxGlyphIds; g++, visited++)
                    result[g] = classValue;
            }
        }

        return result;
    }

    private static int ValueRecordSize(int valueFormat)
    {
        var count = 0;
        var v = valueFormat;
        while (v != 0)
        {
            count += v & 1;
            v >>= 1;
        }
        return count * 2;
    }

    private static int ReadXAdvanceFromValueRecord(ReadOnlySpan<byte> record, int valueFormat)
    {
        if ((valueFormat & 0x0004) == 0)
            return 0;
        var fieldsBefore = 0;
        if ((valueFormat & 0x0001) != 0) fieldsBefore++;
        if ((valueFormat & 0x0002) != 0) fieldsBefore++;
        var byteOffset = fieldsBefore * 2;
        if (byteOffset + 2 > record.Length)
            return 0;
        return BinaryPrimitives.ReadInt16BigEndian(record.Slice(byteOffset));
    }
}
