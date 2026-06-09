namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>cmap</c> (character-to-glyph mapping) table. Selects the best available
/// Unicode subtable and maps codepoints to glyph indices.
/// </summary>
/// <remarks>
/// Supports format 4 (segmented BMP coverage) and format 12 (segmented full-Unicode
/// coverage). When both are present the format 12 subtable is preferred because it
/// covers supplementary planes. Other formats are ignored at this phase.
/// </remarks>
internal sealed class CmapTable
{
    // Format 4 data.
    private readonly ushort[]? _endCodes;
    private readonly ushort[]? _startCodes;
    private readonly short[]? _idDeltas;
    private readonly ushort[]? _idRangeOffsets;
    private readonly ushort[]? _glyphIdArray;

    // Format 12 data.
    private readonly uint[]? _groupStartChar;
    private readonly uint[]? _groupEndChar;
    private readonly uint[]? _groupStartGlyph;

    private readonly bool _isFormat12;

    private CmapTable(
        ushort[] endCodes, ushort[] startCodes, short[] idDeltas,
        ushort[] idRangeOffsets, ushort[] glyphIdArray)
    {
        _endCodes = endCodes;
        _startCodes = startCodes;
        _idDeltas = idDeltas;
        _idRangeOffsets = idRangeOffsets;
        _glyphIdArray = glyphIdArray;
        _isFormat12 = false;
    }

    private CmapTable(uint[] startChar, uint[] endChar, uint[] startGlyph)
    {
        _groupStartChar = startChar;
        _groupEndChar = endChar;
        _groupStartGlyph = startGlyph;
        _isFormat12 = true;
    }

    /// <summary>The cmap subtable format that was selected (4 or 12).</summary>
    public int Format => _isFormat12 ? 12 : 4;

    /// <summary>
    /// Maps a Unicode codepoint to its glyph index, returning 0 (the missing-glyph
    /// index) when the codepoint is not covered.
    /// </summary>
    public int GetGlyphIndex(int codepoint)
    {
        if (codepoint < 0)
            return 0;
        return _isFormat12 ? LookupFormat12((uint)codepoint) : LookupFormat4((uint)codepoint);
    }

    private int LookupFormat4(uint codepoint)
    {
        if (codepoint > 0xFFFF)
            return 0;

        ushort[] endCodes = _endCodes!;
        ushort[] startCodes = _startCodes!;
        short[] idDeltas = _idDeltas!;
        ushort[] idRangeOffsets = _idRangeOffsets!;
        ushort[] glyphIds = _glyphIdArray!;
        ushort c = (ushort)codepoint;

        int segCount = endCodes.Length;
        for (int seg = 0; seg < segCount; seg++)
        {
            if (c > endCodes[seg])
                continue;
            if (c < startCodes[seg])
                return 0; // Segments are sorted; a gap means the codepoint is unmapped.

            ushort rangeOffset = idRangeOffsets[seg];
            if (rangeOffset == 0)
                return (ushort)(c + idDeltas[seg]);

            // idRangeOffset indexes into glyphIdArray using the obsolete pointer-arithmetic
            // layout from the spec: the value is a byte offset from its own slot. We re-express
            // it as an index into the flattened glyphIdArray.
            int glyphIndexIndex = (rangeOffset / 2) + (c - startCodes[seg]) - (segCount - seg);
            if (glyphIndexIndex < 0 || glyphIndexIndex >= glyphIds.Length)
                return 0;

            ushort glyphId = glyphIds[glyphIndexIndex];
            return glyphId == 0 ? 0 : (ushort)(glyphId + idDeltas[seg]);
        }

        return 0;
    }

    private int LookupFormat12(uint codepoint)
    {
        uint[] startChar = _groupStartChar!;
        uint[] endChar = _groupEndChar!;
        uint[] startGlyph = _groupStartGlyph!;

        int lo = 0;
        int hi = startChar.Length - 1;
        while (lo <= hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            if (codepoint < startChar[mid])
                hi = mid - 1;
            else if (codepoint > endChar[mid])
                lo = mid + 1;
            else
                return (int)(startGlyph[mid] + (codepoint - startChar[mid]));
        }

        return 0;
    }

    /// <summary>
    /// Parses the <c>cmap</c> table and selects the best Unicode subtable.
    /// </summary>
    /// <exception cref="FontFormatException">If no supported Unicode subtable is found.</exception>
    public static CmapTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        reader.Skip(2); // version
        ushort numTables = reader.ReadUInt16();

        uint bestFormat4Offset = 0;
        uint bestFormat12Offset = 0;

        for (int i = 0; i < numTables; i++)
        {
            ushort platformId = reader.ReadUInt16();
            ushort encodingId = reader.ReadUInt16();
            uint subtableOffset = reader.ReadUInt32();

            if (!IsUnicodeEncoding(platformId, encodingId))
                continue;

            if ((int)subtableOffset + 2 > data.Length)
                continue;

            ushort format = ReadFormatAt(data, subtableOffset);
            if (format == 12)
                bestFormat12Offset = subtableOffset;
            else if (format == 4 && bestFormat4Offset == 0)
                bestFormat4Offset = subtableOffset;
        }

        if (bestFormat12Offset != 0)
            return ParseFormat12(data, bestFormat12Offset);
        if (bestFormat4Offset != 0)
            return ParseFormat4(data, bestFormat4Offset);

        throw new FontFormatException("cmap", 0, "no supported Unicode subtable (format 4 or 12) found.");
    }

    private static bool IsUnicodeEncoding(ushort platformId, ushort encodingId) =>
        platformId switch
        {
            0 => true,                                    // Unicode platform (any encoding)
            3 => encodingId is 1 or 10,                   // Windows BMP (1) or full Unicode (10)
            _ => false
        };

    private static ushort ReadFormatAt(ReadOnlySpan<byte> data, uint offset)
    {
        var reader = new FontReader(data);
        reader.Seek((int)offset);
        return reader.ReadUInt16();
    }

    private static CmapTable ParseFormat4(ReadOnlySpan<byte> data, uint offset)
    {
        var reader = new FontReader(data);
        reader.Seek((int)offset);

        reader.Skip(2); // format (already known to be 4)
        ushort length = reader.ReadUInt16();
        reader.Skip(2); // language
        ushort segCountX2 = reader.ReadUInt16();
        int segCount = segCountX2 / 2;
        reader.Skip(6); // searchRange, entrySelector, rangeShift

        var endCodes = new ushort[segCount];
        for (int i = 0; i < segCount; i++)
            endCodes[i] = reader.ReadUInt16();

        reader.Skip(2); // reservedPad

        var startCodes = new ushort[segCount];
        for (int i = 0; i < segCount; i++)
            startCodes[i] = reader.ReadUInt16();

        var idDeltas = new short[segCount];
        for (int i = 0; i < segCount; i++)
            idDeltas[i] = reader.ReadInt16();

        var idRangeOffsets = new ushort[segCount];
        for (int i = 0; i < segCount; i++)
            idRangeOffsets[i] = reader.ReadUInt16();

        // The remaining bytes of the subtable form glyphIdArray.
        int consumed = reader.Position - (int)offset;
        int glyphIdBytes = length - consumed;
        int glyphIdCount = glyphIdBytes > 0 ? glyphIdBytes / 2 : 0;
        var glyphIdArray = new ushort[glyphIdCount];
        for (int i = 0; i < glyphIdCount; i++)
            glyphIdArray[i] = reader.ReadUInt16();

        return new CmapTable(endCodes, startCodes, idDeltas, idRangeOffsets, glyphIdArray);
    }

    private static CmapTable ParseFormat12(ReadOnlySpan<byte> data, uint offset)
    {
        var reader = new FontReader(data);
        reader.Seek((int)offset);

        reader.Skip(2); // format (already known to be 12)
        reader.Skip(2); // reserved
        reader.Skip(4); // length
        reader.Skip(4); // language
        uint numGroups = reader.ReadUInt32();

        var startChar = new uint[numGroups];
        var endChar = new uint[numGroups];
        var startGlyph = new uint[numGroups];

        for (uint i = 0; i < numGroups; i++)
        {
            startChar[i] = reader.ReadUInt32();
            endChar[i] = reader.ReadUInt32();
            startGlyph[i] = reader.ReadUInt32();
        }

        return new CmapTable(startChar, endChar, startGlyph);
    }
}
