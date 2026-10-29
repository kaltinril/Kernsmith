using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>
/// The parts of a <c>CFF </c> (Adobe TN#5176) or <c>CFF2</c> (OpenType) table that glyph outlines
/// need: the CharStrings, global subroutines, and the per-glyph Private DICT data (local
/// subroutines, widths) — which, for CID-keyed CFF and for CFF2, comes from the Font DICT that
/// FDSelect assigns the glyph.
/// </summary>
/// <remarks>
/// Only the first font of a CFF FontSet is read; OpenType requires exactly one. Names, strings,
/// encodings and hinting values are not needed to draw outlines and are skipped. Everything is
/// parsed and validated up front, so a malformed table fails at load time with a
/// <see cref="FontFormatException"/> rather than per glyph.
/// </remarks>
internal sealed class CffFont
{
    /// <summary>FDSelect entries for glyphs no FDSelect range covers.</summary>
    private const ushort Unmapped = ushort.MaxValue;

    private readonly CffPrivate[] _privates;
    private readonly ushort[]? _fdSelect;
    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _charsetOffset;
    private readonly int? _variationStoreOffset;
    private ushort[]? _charset;

    private CffFont(
        bool isCff2,
        CffIndex charStrings,
        CffIndex globalSubrs,
        CffPrivate[] privates,
        ushort[]? fdSelect,
        int[] regionCounts,
        ReadOnlyMemory<byte> table,
        int charsetOffset,
        bool isCidKeyed,
        int? variationStoreOffset = null)
    {
        IsCff2 = isCff2;
        CharStrings = charStrings;
        GlobalSubrs = globalSubrs;
        _privates = privates;
        _fdSelect = fdSelect;
        RegionCounts = regionCounts;
        _table = table;
        _charsetOffset = charsetOffset;
        IsCidKeyed = isCidKeyed;
        _variationStoreOffset = variationStoreOffset;
    }

    /// <summary>True for a <c>CFF2</c> table.</summary>
    public bool IsCff2 { get; }

    /// <summary>True for a CID-keyed CFF (ROS in the Top DICT) — glyphs get their Private DICT through FDSelect.</summary>
    public bool IsCidKeyed { get; }

    /// <summary>One charstring per glyph, indexed by glyph id.</summary>
    public CffIndex CharStrings { get; }

    /// <summary>The global subroutines, shared by every glyph.</summary>
    public CffIndex GlobalSubrs { get; }

    /// <summary>
    /// CFF2: the number of variation regions in each ItemVariationData, indexed by <c>vsindex</c>
    /// — how many deltas each <c>blend</c> operand carries. Empty when there is no variation store.
    /// </summary>
    public IReadOnlyList<int> RegionCounts { get; }

    /// <summary>True for a CFF2 table with a VariationStore — one whose charstrings may <c>blend</c>.</summary>
    public bool HasVariationStore => _variationStoreOffset is not null;

    /// <summary>Number of Font DICTs (1 for a name-keyed CFF).</summary>
    public int FontDictCount => _privates.Length;

    /// <summary>The Private DICT data that applies to <paramref name="glyphIndex"/>.</summary>
    /// <exception cref="FontFormatException">If FDSelect maps the glyph to no Font DICT.</exception>
    public CffPrivate PrivateFor(int glyphIndex)
    {
        if (_fdSelect is null)
            return _privates[0];

        if ((uint)glyphIndex >= (uint)_fdSelect.Length || _fdSelect[glyphIndex] == Unmapped)
            throw new FontFormatException($"CFF FDSelect assigns glyph {glyphIndex} no Font DICT.");

        return _privates[_fdSelect[glyphIndex]];
    }

    /// <summary>
    /// The glyph a <c>seac</c> accent/base character code names: a StandardEncoding code, mapped
    /// to a SID, then to the glyph whose charset entry is that SID. Name-keyed CFF only.
    /// </summary>
    /// <returns>The glyph index, or -1 when the font has no such glyph.</returns>
    /// <exception cref="FontFormatException">If the font is CID-keyed or CFF2, or the charset is malformed.</exception>
    public int GlyphForStandardCode(int code)
    {
        if (IsCff2 || IsCidKeyed)
            throw new FontFormatException("seac accent composition needs a name-keyed CFF font.");

        int sid = StandardEncoding.Sid(code);
        if (sid == 0)
            return -1;

        _charset ??= ParseCharset(_table, _charsetOffset, CharStrings.Count);
        return Array.IndexOf(_charset, (ushort)sid);
    }

    /// <summary>Parses a <c>CFF </c> table.</summary>
    /// <exception cref="FontFormatException">If the table is malformed.</exception>
    public static CffFont ParseCff(ReadOnlyMemory<byte> table)
    {
        var reader = new FontReader(table.Span);
        byte major = reader.ReadUInt8();
        reader.ReadUInt8(); // minor
        byte headerSize = reader.ReadUInt8();
        if (major != 1)
            throw new FontFormatException("CFF ", 0, $"unsupported major version {major}.");

        CffIndex.Parse(table, headerSize, cff2: false, out int afterNames);
        var topDicts = CffIndex.Parse(table, afterNames, cff2: false, out int afterTopDicts);
        CffIndex.Parse(table, afterTopDicts, cff2: false, out int afterStrings);
        var globalSubrs = CffIndex.Parse(table, afterStrings, cff2: false, out _);

        if (topDicts.Count == 0)
            throw new FontFormatException("CFF ", 0, "the Top DICT INDEX is empty.");

        var top = CffDict.Parse(topDicts[0].Span, cff2: false);

        if (top.GetNumber(CffDict.CharstringType, 2) != 2)
            throw new FontFormatException("CFF ", 0, "only Type 2 charstrings are supported.");

        var charStrings = CffIndex.Parse(table, RequireOffset(top, CffDict.CharStrings, "CharStrings"), cff2: false, out _);
        var topMatrix = top.Get(CffDict.FontMatrix);

        CffPrivate[] privates;
        ushort[]? fdSelect = null;
        bool isCid = top.Has(CffDict.Ros);

        if (isCid)
        {
            var fdArray = CffIndex.Parse(table, RequireOffset(top, CffDict.FdArray, "FDArray"), cff2: false, out _);
            privates = ParseFontDicts(table, fdArray, cff2: false, regionCount: null, topMatrix);
            fdSelect = ParseFdSelect(table, RequireOffset(top, CffDict.FdSelect, "FDSelect"), charStrings.Count, privates.Length, cff2: false);
        }
        else
        {
            privates = [ParsePrivate(table, top, cff2: false, regionCount: null, CffMatrix.From(topMatrix, null))];
        }

        return new CffFont(
            isCff2: false, charStrings, globalSubrs, privates, fdSelect, regionCounts: [],
            table, top.GetInt(CffDict.Charset) ?? 0, isCid);
    }

    /// <summary>Parses a <c>CFF2</c> table.</summary>
    /// <exception cref="FontFormatException">If the table is malformed.</exception>
    public static CffFont ParseCff2(ReadOnlyMemory<byte> table)
    {
        var reader = new FontReader(table.Span);
        byte major = reader.ReadUInt8();
        reader.ReadUInt8(); // minor
        byte headerSize = reader.ReadUInt8();
        ushort topDictSize = reader.ReadUInt16();
        if (major != 2)
            throw new FontFormatException("CFF2", 0, $"unsupported major version {major}.");

        if (headerSize + topDictSize > table.Length)
            throw new FontFormatException("CFF2", 0, "the Top DICT runs past the end of the table.");

        var top = CffDict.Parse(table.Span.Slice(headerSize, topDictSize), cff2: true);
        var globalSubrs = CffIndex.Parse(table, headerSize + topDictSize, cff2: true, out _);
        var charStrings = CffIndex.Parse(table, RequireOffset(top, CffDict.CharStrings, "CharStrings"), cff2: true, out _);

        int? vstore = top.GetInt(CffDict.VariationStore);
        int[] regionCounts = vstore is int storeOffset ? ParseRegionCounts(table, storeOffset) : [];
        Func<int, int>? regionCount = regionCounts.Length == 0 ? null : vsIndex => RegionCount(regionCounts, vsIndex);

        var fdArray = CffIndex.Parse(table, RequireOffset(top, CffDict.FdArray, "FDArray"), cff2: true, out _);
        var privates = ParseFontDicts(table, fdArray, cff2: true, regionCount, top.Get(CffDict.FontMatrix));

        ushort[]? fdSelect = top.GetInt(CffDict.FdSelect) is int fdSelectOffset
            ? ParseFdSelect(table, fdSelectOffset, charStrings.Count, privates.Length, cff2: true)
            : null;

        return new CffFont(
            isCff2: true, charStrings, globalSubrs, privates, fdSelect, regionCounts,
            table, charsetOffset: 0, isCidKeyed: false, vstore);
    }

    /// <summary>
    /// Parses the CFF2 VariationStore — a uint16 length, then an ItemVariationStore of that
    /// length — in full. Only a variable instance needs its regions, so this is not done at load:
    /// a damaged store leaves the default instance renderable.
    /// </summary>
    /// <returns>The store, or null when the table has none.</returns>
    /// <exception cref="FontFormatException">If the store is malformed or disagrees with the region counts read at load.</exception>
    public ItemVariationStore? ReadVariationStore()
    {
        if (_variationStoreOffset is not int offset)
            return null;

        var span = _table.Span;
        if ((long)offset + 2 > span.Length)
            throw new FontFormatException("CFF2", offset, "the VariationStore is outside the table.");

        int length = (span[offset] << 8) | span[offset + 1];
        if ((long)offset + 2 + length > span.Length)
            throw new FontFormatException("CFF2", offset, "the VariationStore runs past the end of the table.");

        ItemVariationStore store;
        try
        {
            store = ItemVariationStore.Parse(span.Slice(offset + 2, length));
        }
        catch (OverflowException ex)
        {
            throw new FontFormatException("CFF2", offset, $"the VariationStore is malformed: {ex.Message}");
        }

        if (store.SubtableCount != RegionCounts.Count)
            throw new FontFormatException("CFF2", offset, "the VariationStore's ItemVariationData count changed.");

        return store;
    }

    /// <summary>The region count for <paramref name="vsIndex"/>.</summary>
    /// <exception cref="FontFormatException">If there is no such ItemVariationData.</exception>
    public static int RegionCount(IReadOnlyList<int> regionCounts, int vsIndex)
    {
        if ((uint)vsIndex >= (uint)regionCounts.Count)
            throw new FontFormatException($"CFF2 vsindex {vsIndex} names no ItemVariationData (count {regionCounts.Count}).");

        return regionCounts[vsIndex];
    }

    private static int RequireOffset(CffDict dict, int op, string name) =>
        dict.GetInt(op) ?? throw new FontFormatException($"CFF Top DICT has no {name} entry.");

    private static CffPrivate[] ParseFontDicts(
        ReadOnlyMemory<byte> table, CffIndex fdArray, bool cff2, Func<int, int>? regionCount, double[]? topMatrix)
    {
        if (fdArray.Count == 0)
            throw new FontFormatException("CFF FDArray is empty.");

        var privates = new CffPrivate[fdArray.Count];
        for (int i = 0; i < privates.Length; i++)
        {
            var fontDict = CffDict.Parse(fdArray[i].Span, cff2, regionCount);
            privates[i] = ParsePrivate(table, fontDict, cff2, regionCount, CffMatrix.From(topMatrix, fontDict.Get(CffDict.FontMatrix)));
        }

        return privates;
    }

    private static CffPrivate ParsePrivate(
        ReadOnlyMemory<byte> table, CffDict parent, bool cff2, Func<int, int>? regionCount, CffMatrix? matrix)
    {
        if (!parent.Has(CffDict.Private))
            return new CffPrivate(CffIndex.Empty, 0, 0, 0, matrix);

        int size = parent.GetInt(CffDict.Private, 0)!.Value;
        int offset = parent.GetInt(CffDict.Private, 1)!.Value;
        if ((long)offset + size > table.Length)
            throw new FontFormatException($"CFF Private DICT ({offset}+{size}) runs past the end of the table.");

        var privateDict = CffDict.Parse(table.Span.Slice(offset, size), cff2, regionCount);

        var localSubrs = CffIndex.Empty;
        if (privateDict.GetInt(CffDict.Subrs) is int subrs)
        {
            long subrsOffset = (long)offset + subrs;
            if (subrsOffset > table.Length)
                throw new FontFormatException($"CFF local Subrs offset {subrsOffset} is outside the table.");

            localSubrs = CffIndex.Parse(table, (int)subrsOffset, cff2, out _);
        }

        int vsIndex = cff2 ? privateDict.GetInt(CffDict.VsIndex) ?? 0 : 0;

        return new CffPrivate(
            localSubrs,
            privateDict.GetNumber(CffDict.DefaultWidthX, 0),
            privateDict.GetNumber(CffDict.NominalWidthX, 0),
            vsIndex,
            matrix);
    }

    /// <summary>Parses FDSelect formats 0 and 3 (CFF and CFF2) and 4 (CFF2) into a per-glyph table.</summary>
    private static ushort[] ParseFdSelect(ReadOnlyMemory<byte> table, int offset, int glyphCount, int fdCount, bool cff2)
    {
        var reader = new FontReader(table.Span);
        reader.Seek(offset);
        byte format = reader.ReadUInt8();

        var map = new ushort[glyphCount];

        if (format == 0)
        {
            var ids = reader.ReadBytes(glyphCount);
            for (int g = 0; g < glyphCount; g++)
                map[g] = CheckFd(ids[g], fdCount);

            return map;
        }

        bool wide = format == 4;
        if (format != 3 && !(wide && cff2))
            throw new FontFormatException($"CFF FDSelect format {format} is not supported.");

        Array.Fill(map, Unmapped);

        long rangeCount = wide ? reader.ReadUInt32() : reader.ReadUInt16();
        if (rangeCount * (wide ? 6 : 3) > reader.Remaining)
            throw new FontFormatException($"CFF FDSelect declares {rangeCount} ranges but the table is shorter.");

        long first = wide ? reader.ReadUInt32() : reader.ReadUInt16();
        if (rangeCount > 0 && first != 0)
            throw new FontFormatException("CFF FDSelect's first range does not start at glyph 0.");

        for (long r = 0; r < rangeCount; r++)
        {
            ushort fd = CheckFd(wide ? reader.ReadUInt16() : reader.ReadUInt8(), fdCount);
            long next = wide ? reader.ReadUInt32() : reader.ReadUInt16(); // next range's first, or the sentinel
            if (next < first)
                throw new FontFormatException("CFF FDSelect ranges are not in increasing order.");

            long stop = Math.Min(next, glyphCount);
            for (long g = first; g < stop; g++)
                map[g] = fd;

            first = next;
        }

        return map;
    }

    private static ushort CheckFd(int fd, int fdCount) =>
        fd < fdCount
            ? (ushort)fd
            : throw new FontFormatException($"CFF FDSelect names Font DICT {fd}, but FDArray has {fdCount}.");

    /// <summary>
    /// Reads the region count of each ItemVariationData in the CFF2 VariationStore — a uint16
    /// length followed by an ItemVariationStore. CFF2 keeps its deltas inline as <c>blend</c>
    /// operands, so the region counts are all a default-instance decoder needs.
    /// </summary>
    private static int[] ParseRegionCounts(ReadOnlyMemory<byte> table, int offset)
    {
        var reader = new FontReader(table.Span);
        reader.Seek(offset);
        reader.ReadUInt16(); // length
        int storeStart = reader.Position;

        reader.ReadUInt16(); // format
        reader.ReadUInt32(); // variationRegionListOffset
        int dataCount = reader.ReadUInt16();

        var counts = new int[dataCount];
        for (int i = 0; i < dataCount; i++)
        {
            uint dataOffset = reader.ReadUInt32();
            int resume = reader.Position;

            long at = storeStart + (long)dataOffset;
            if (at > table.Length)
                throw new FontFormatException("CFF2", offset, "an ItemVariationData offset is outside the table.");

            reader.Seek((int)at);
            reader.Skip(4); // itemCount, wordDeltaCount
            counts[i] = reader.ReadUInt16();
            reader.Seek(resume);
        }

        return counts;
    }

    /// <summary>Parses the charset (GID → SID), including the predefined ISOAdobe/Expert ids 0–2.</summary>
    private static ushort[] ParseCharset(ReadOnlyMemory<byte> table, int offset, int glyphCount)
    {
        var sids = new ushort[glyphCount];

        if (offset == 0)
        {
            // ISOAdobe: glyph i is SID i, for the 229 SIDs the predefined charset covers.
            for (int g = 0; g < Math.Min(glyphCount, 229); g++)
                sids[g] = (ushort)g;

            return sids;
        }

        if (offset <= 2)
            throw new FontFormatException("seac in a font with a predefined Expert charset is not supported.");

        var reader = new FontReader(table.Span);
        reader.Seek(offset);
        byte format = reader.ReadUInt8();

        int glyph = 1; // .notdef (SID 0) is implicit
        if (format == 0)
        {
            while (glyph < glyphCount)
                sids[glyph++] = reader.ReadUInt16();

            return sids;
        }

        if (format is not (1 or 2))
            throw new FontFormatException($"CFF charset format {format} is not supported.");

        while (glyph < glyphCount)
        {
            int sid = reader.ReadUInt16();
            int left = format == 1 ? reader.ReadUInt8() : reader.ReadUInt16();
            for (int i = 0; i <= left && glyph < glyphCount; i++)
                sids[glyph++] = (ushort)(sid + i);
        }

        return sids;
    }
}

/// <summary>The Private DICT data a charstring runs against.</summary>
/// <param name="LocalSubrs">The local subroutines (<c>Subrs</c>).</param>
/// <param name="DefaultWidthX">Width of a charstring that encodes none.</param>
/// <param name="NominalWidthX">Base a charstring's encoded width is added to.</param>
/// <param name="VsIndex">CFF2: the ItemVariationData the Private DICT selects (default 0).</param>
/// <param name="Matrix">
/// Maps charstring units to font units, or null when the FontMatrix is the usual
/// 1/unitsPerEm scale (charstring units already are font units).
/// </param>
internal sealed record CffPrivate(
    CffIndex LocalSubrs,
    double DefaultWidthX,
    double NominalWidthX,
    int VsIndex,
    CffMatrix? Matrix);
