using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Font.Tables;

namespace KernSmith.Font;

internal class TtfParser
{
    private const uint TrueTypeMagic = 0x00010000;
    private const uint OttoMagic = 0x4F54544F; // "OTTO"
    private const uint TtcMagic = 0x74746366; // "ttcf"

    private readonly ReadOnlyMemory<byte> _data;
    private readonly Dictionary<uint, (int offset, int length)> _tables = new();
    private readonly HashSet<int>? _requestedCodepoints;
    private HashSet<int>? _relevantGlyphIndices;

    public HeadTable? Head { get; private set; }
    public HheaTable? Hhea { get; private set; }
    public Os2Metrics? Os2 { get; private set; }
    public NameInfo? Names { get; private set; }
    public IReadOnlyDictionary<int, int> CmapTable { get; private set; } = ReadOnlyDictionary<int, int>.Empty;
    public IReadOnlyList<KerningPair> KernPairs { get; private set; } = Array.Empty<KerningPair>();
    public IReadOnlyList<KerningPair> GposPairs { get; private set; } = Array.Empty<KerningPair>();
    public IReadOnlyList<VariationAxis>? VariationAxes { get; private set; }
    public IReadOnlyList<NamedInstance>? NamedInstances { get; private set; }

    /// <summary>
    /// True if the font's table directory contains COLR, sbix, or CBDT tables.
    /// </summary>
    public bool HasColorGlyphs { get; private set; }

    public TtfParser(ReadOnlySpan<byte> fontData, int faceIndex = 0, HashSet<int>? requestedCodepoints = null)
    {
        _data = fontData.ToArray();
        _requestedCodepoints = requestedCodepoints;

        ParseTableDirectory(fontData, faceIndex);
        ParseHead();
        ParseHhea();
        ParseHmtx();
        ParseOs2();
        ParseName();
        ParseCmap();
        ParseKern();
        ParseGpos();
        ParseFvar();
        DetectColorTables();
    }

    /// <summary>
    /// Creates a parser that shares an existing byte array (avoids an extra copy).
    /// </summary>
    internal TtfParser(byte[] fontBytes, int faceIndex, HashSet<int>? requestedCodepoints)
    {
        _data = fontBytes;
        _requestedCodepoints = requestedCodepoints;

        ParseTableDirectory(fontBytes, faceIndex);
        ParseHead();
        ParseHhea();
        ParseHmtx();
        ParseOs2();
        ParseName();
        ParseCmap();
        ParseKern();
        ParseGpos();
        ParseFvar();
        DetectColorTables();
    }

    private static uint Tag(char a, char b, char c, char d) =>
        (uint)(a << 24 | b << 16 | c << 8 | d);

    /// <summary>
    /// Checks whether the table directory contains any color glyph tables (COLR, sbix, CBDT).
    /// </summary>
    private void DetectColorTables()
    {
        HasColorGlyphs =
            _tables.ContainsKey(Tag('C', 'O', 'L', 'R')) ||
            _tables.ContainsKey(Tag('s', 'b', 'i', 'x')) ||
            _tables.ContainsKey(Tag('C', 'B', 'D', 'T'));
    }

    private ReadOnlySpan<byte> GetTable(uint tag)
    {
        if (_tables.TryGetValue(tag, out var entry))
            return _data.Span.Slice(entry.offset, entry.length);
        return ReadOnlySpan<byte>.Empty;
    }

    // 2A: Table Directory Parser
    private void ParseTableDirectory(ReadOnlySpan<byte> data, int faceIndex)
    {
        if (data.Length < 4)
            throw new FontParsingException("Font data is too small to contain a valid header.");

        var magic = BinaryPrimitives.ReadUInt32BigEndian(data);
        int directoryOffset = 0;

        if (magic == TtcMagic)
        {
            // TTC header: version (uint32), numFonts (uint32), offsets[]
            if (data.Length < 12)
                throw new FontParsingException("TTC header is too small.");
            var numFonts = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8));
            if (faceIndex < 0 || (uint)faceIndex >= numFonts)
                throw new FontParsingException($"Face index {faceIndex} is out of range (font contains {numFonts} faces).");
            directoryOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(12 + faceIndex * 4));
            magic = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(directoryOffset));
        }

        if (magic != TrueTypeMagic && magic != OttoMagic)
            throw new FontParsingException($"Invalid sfnt magic: 0x{magic:X8}. Expected TrueType (0x00010000) or OTF ('OTTO').");

        var numTables = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(directoryOffset + 4));
        var recordStart = directoryOffset + 12;

        for (var i = 0; i < numTables; i++)
        {
            var recOffset = recordStart + i * 16;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(recOffset));
            // skip checksum at recOffset + 4
            var tableOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(recOffset + 8));
            var tableLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(recOffset + 12));
            _tables[tag] = (tableOffset, tableLength);
        }
    }

    // 2B: head Table
    private void ParseHead()
    {
        var table = GetTable(Tag('h', 'e', 'a', 'd'));
        if (table.IsEmpty)
            throw new FontParsingException("Required 'head' table is missing.");

        if (table.Length < 54)
            throw new FontParsingException("head", 0, "Table is too small.");

        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(18));
        var created = BinaryPrimitives.ReadInt64BigEndian(table.Slice(20));
        var modified = BinaryPrimitives.ReadInt64BigEndian(table.Slice(28));
        var xMin = BinaryPrimitives.ReadInt16BigEndian(table.Slice(36));
        var yMin = BinaryPrimitives.ReadInt16BigEndian(table.Slice(38));
        var xMax = BinaryPrimitives.ReadInt16BigEndian(table.Slice(40));
        var yMax = BinaryPrimitives.ReadInt16BigEndian(table.Slice(42));
        ushort macStyle = table.Length >= 46 ? BinaryPrimitives.ReadUInt16BigEndian(table.Slice(44)) : (ushort)0;
        ushort lowestRecPPEM = table.Length >= 48 ? BinaryPrimitives.ReadUInt16BigEndian(table.Slice(46)) : (ushort)0;
        var indexToLocFormat = BinaryPrimitives.ReadInt16BigEndian(table.Slice(50));

        Head = new HeadTable(unitsPerEm, xMin, yMin, xMax, yMax, indexToLocFormat, created, modified,
            macStyle, lowestRecPPEM);
    }

    // 2C: hhea Table
    private void ParseHhea()
    {
        var table = GetTable(Tag('h', 'h', 'e', 'a'));
        if (table.IsEmpty)
            return;

        if (table.Length < 36)
            return;

        var ascender = BinaryPrimitives.ReadInt16BigEndian(table.Slice(4));
        var descender = BinaryPrimitives.ReadInt16BigEndian(table.Slice(6));
        var lineGap = BinaryPrimitives.ReadInt16BigEndian(table.Slice(8));
        var advanceWidthMax = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(10));
        short minLeftSideBearing = table.Length >= 14 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(12)) : (short)0;
        short minRightSideBearing = table.Length >= 16 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(14)) : (short)0;
        short xMaxExtent = table.Length >= 18 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(16)) : (short)0;
        var numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(34));

        Hhea = new HheaTable(ascender, descender, lineGap, advanceWidthMax, numberOfHMetrics,
            minLeftSideBearing, minRightSideBearing, xMaxExtent);
    }

    // 2D: hmtx Table — intentionally skipped.
    // FreeType provides per-glyph advance widths via FT_Load_Glyph,
    // so parsing hmtx here would be redundant.
    private void ParseHmtx() { }

    // 2E: OS/2 Table
    private void ParseOs2()
    {
        var table = GetTable(Tag('O', 'S', '/', '2'));
        if (table.IsEmpty)
            return;

        if (table.Length < 78)
            return;

        var version = BinaryPrimitives.ReadUInt16BigEndian(table);
        short xAvgCharWidth = table.Length >= 4 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(2)) : (short)0;
        var weightClass = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(4));
        var widthClass = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6));
        short subscriptXSize = table.Length >= 12 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(10)) : (short)0;
        short subscriptYSize = table.Length >= 14 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(12)) : (short)0;
        short superscriptXSize = table.Length >= 20 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(18)) : (short)0;
        short superscriptYSize = table.Length >= 22 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(20)) : (short)0;
        short strikeoutSize = table.Length >= 28 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(26)) : (short)0;
        short strikeoutPosition = table.Length >= 30 ? BinaryPrimitives.ReadInt16BigEndian(table.Slice(28)) : (short)0;

        var panose = new byte[10];
        table.Slice(32, 10).CopyTo(panose);

        var firstCharIndex = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(64));
        var lastCharIndex = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(66));
        var typoAscender = BinaryPrimitives.ReadInt16BigEndian(table.Slice(68));
        var typoDescender = BinaryPrimitives.ReadInt16BigEndian(table.Slice(70));
        var typoLineGap = BinaryPrimitives.ReadInt16BigEndian(table.Slice(72));
        var winAscent = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(74));
        var winDescent = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(76));

        int xHeight = 0;
        int capHeight = 0;

        if (version >= 2 && table.Length >= 90)
        {
            xHeight = BinaryPrimitives.ReadInt16BigEndian(table.Slice(86));
            capHeight = BinaryPrimitives.ReadInt16BigEndian(table.Slice(88));
        }

        Os2 = new Os2Metrics(
            weightClass, widthClass, typoAscender, typoDescender, typoLineGap,
            winAscent, winDescent, xHeight, capHeight, panose,
            firstCharIndex, lastCharIndex,
            xAvgCharWidth, subscriptXSize, subscriptYSize,
            superscriptXSize, superscriptYSize, strikeoutSize, strikeoutPosition);
    }

    // 2F: name Table
    private void ParseName()
    {
        var table = GetTable(Tag('n', 'a', 'm', 'e'));
        if (table.IsEmpty)
            return;

        if (table.Length < 6)
            return;

        var count = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(2));
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(4));

        // Target nameIDs: 0=copyright, 1=family, 2=subfamily, 3=uniqueId, 4=fullName,
        // 5=version, 6=postScript, 7=trademark, 8=manufacturer, 9=designer,
        // 10=description, 13=license, 14=licenseUrl
        var targetIds = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14 };
        // Store best match: nameID -> (priority, string)
        // Priority: Windows English=3, Windows other=2, Mac=1
        var results = new Dictionary<int, (int priority, string value)>();

        for (var i = 0; i < count; i++)
        {
            var recOffset = 6 + i * 12;
            if (recOffset + 12 > table.Length)
                break;

            var platformID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset));
            var encodingID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 2));
            var languageID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 4));
            var nameID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 6));
            var length = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 8));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 10));

            if (!targetIds.Contains(nameID))
                continue;

            var strStart = stringOffset + offset;
            if (strStart + length > table.Length)
                continue;

            var strData = table.Slice(strStart, length);
            string value;
            int priority;

            if (platformID == 3 && encodingID == 1)
            {
                // Windows Unicode BMP - UTF-16 big-endian
                value = DecodeUtf16BigEndian(strData);
                priority = languageID == 0x0409 ? 3 : 2;
            }
            else if (platformID == 1 && encodingID == 0)
            {
                // Mac Roman - ASCII/Latin-1
                value = DecodeLatin1(strData);
                priority = 1;
            }
            else
            {
                continue;
            }

            if (!results.TryGetValue(nameID, out var existing) || priority > existing.priority)
            {
                results[nameID] = (priority, value);
            }
        }

        Names = new NameInfo(
            FontFamily: results.TryGetValue(1, out var family) ? family.value : null,
            FontSubfamily: results.TryGetValue(2, out var subfamily) ? subfamily.value : null,
            FullName: results.TryGetValue(4, out var fullName) ? fullName.value : null,
            PostScriptName: results.TryGetValue(6, out var psName) ? psName.value : null,
            Copyright: results.TryGetValue(0, out var copyright) ? copyright.value : null,
            Trademark: results.TryGetValue(7, out var trademark) ? trademark.value : null,
            UniqueId: results.TryGetValue(3, out var uniqueId) ? uniqueId.value : null,
            Version: results.TryGetValue(5, out var version) ? version.value : null,
            Manufacturer: results.TryGetValue(8, out var manufacturer) ? manufacturer.value : null,
            Designer: results.TryGetValue(9, out var designer) ? designer.value : null,
            Description: results.TryGetValue(10, out var description) ? description.value : null,
            License: results.TryGetValue(13, out var license) ? license.value : null,
            LicenseUrl: results.TryGetValue(14, out var licenseUrl) ? licenseUrl.value : null);
    }

    /// <summary>
    /// Resolves a name ID from the name table, returning the best available string.
    /// </summary>
    private string? ResolveNameId(int nameId)
    {
        var table = GetTable(Tag('n', 'a', 'm', 'e'));
        if (table.IsEmpty || table.Length < 6)
            return null;

        var count = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(2));
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(4));

        string? best = null;
        int bestPriority = -1;

        for (var i = 0; i < count; i++)
        {
            var recOffset = 6 + i * 12;
            if (recOffset + 12 > table.Length)
                break;

            var platformID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset));
            var encodingID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 2));
            var languageID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 4));
            var nid = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 6));
            var length = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 8));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 10));

            if (nid != nameId)
                continue;

            var strStart = stringOffset + offset;
            if (strStart + length > table.Length)
                continue;

            var strData = table.Slice(strStart, length);
            string value;
            int priority;

            if (platformID == 3 && encodingID == 1)
            {
                value = DecodeUtf16BigEndian(strData);
                priority = languageID == 0x0409 ? 3 : 2;
            }
            else if (platformID == 1 && encodingID == 0)
            {
                value = DecodeLatin1(strData);
                priority = 1;
            }
            else
            {
                continue;
            }

            if (priority > bestPriority)
            {
                best = value;
                bestPriority = priority;
            }
        }

        return best;
    }

    // 17A: fvar Table — Variable font axes and named instances
    private void ParseFvar()
    {
        var table = GetTable(Tag('f', 'v', 'a', 'r'));
        if (table.IsEmpty)
            return;

        // fvar header: majorVersion(2) + minorVersion(2) + axesArrayOffset(2) + reserved(2)
        //              + axisCount(2) + axisSize(2) + instanceCount(2) + instanceSize(2) = 16 bytes
        if (table.Length < 16)
            return;

        var axesArrayOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(4));
        var axisCount = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(8));
        var axisSize = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(10));
        var instanceCount = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(12));
        var instanceSize = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(14));

        // Parse axis records
        var axes = new List<VariationAxis>(axisCount);
        var axisTags = new List<string>(axisCount); // keep tags in order for instance parsing

        for (var i = 0; i < axisCount; i++)
        {
            var axisOffset = axesArrayOffset + i * axisSize;
            if (axisOffset + 20 > table.Length)
                break;

            var axisRecord = table.Slice(axisOffset);
            var tagBytes = axisRecord.Slice(0, 4);
            var tag = Encoding.ASCII.GetString(tagBytes);
            var minValue = BinaryPrimitives.ReadInt32BigEndian(axisRecord.Slice(4)) / 65536.0f;
            var defaultValue = BinaryPrimitives.ReadInt32BigEndian(axisRecord.Slice(8)) / 65536.0f;
            var maxValue = BinaryPrimitives.ReadInt32BigEndian(axisRecord.Slice(12)) / 65536.0f;
            // flags at offset 16 (uint16) — currently unused
            var axisNameId = BinaryPrimitives.ReadUInt16BigEndian(axisRecord.Slice(18));
            var name = ResolveNameId(axisNameId);

            axes.Add(new VariationAxis(tag, minValue, defaultValue, maxValue, name));
            axisTags.Add(tag);
        }

        VariationAxes = axes;

        // Parse named instances
        if (instanceCount == 0 || instanceSize == 0)
        {
            NamedInstances = Array.Empty<NamedInstance>();
            return;
        }

        var instances = new List<NamedInstance>(instanceCount);
        var instancesStart = axesArrayOffset + axisCount * axisSize;

        for (var i = 0; i < instanceCount; i++)
        {
            var instOffset = instancesStart + i * instanceSize;
            // Each instance: subfamilyNameID(2) + flags(2) + axisCount * coordinate(4)
            var minRequired = 4 + axisCount * 4;
            if (instOffset + minRequired > table.Length)
                break;

            var instRecord = table.Slice(instOffset);
            var subfamilyNameId = BinaryPrimitives.ReadUInt16BigEndian(instRecord);
            // flags at offset 2 (uint16) — currently unused
            var name = ResolveNameId(subfamilyNameId);

            var coordinates = new Dictionary<string, float>(axisCount);
            for (var a = 0; a < axisCount; a++)
            {
                var coordOffset = 4 + a * 4;
                var coordValue = BinaryPrimitives.ReadInt32BigEndian(instRecord.Slice(coordOffset)) / 65536.0f;
                coordinates[axisTags[a]] = coordValue;
            }

            instances.Add(new NamedInstance(name, coordinates));
        }

        NamedInstances = instances;
    }

    private static string DecodeUtf16BigEndian(ReadOnlySpan<byte> data)
    {
        var chars = new char[data.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i * 2));
        }
        return new string(chars);
    }

    private static string DecodeLatin1(ReadOnlySpan<byte> data)
    {
        var chars = new char[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            chars[i] = (char)data[i];
        }
        return new string(chars);
    }

    // 2G: cmap Table
    private void ParseCmap()
    {
        var table = GetTable(Tag('c', 'm', 'a', 'p'));
        if (table.IsEmpty)
            throw new FontParsingException("Required 'cmap' table is missing.");

        if (table.Length < 4)
            throw new FontParsingException("cmap", 0, "Table is too small.");

        var numEncodings = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(2));

        // Find best subtable: prefer platform 3 encoding 10 (format 12), then platform 3 encoding 1 (format 4)
        int bestOffset = -1;
        int bestPriority = -1;

        for (var i = 0; i < numEncodings; i++)
        {
            var recOffset = 4 + i * 8;
            if (recOffset + 8 > table.Length)
                break;

            var platformID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset));
            var encodingID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 2));
            var subtableOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(table.Slice(recOffset + 4));

            int priority;
            if (platformID == 3 && encodingID == 10)
                priority = 2; // best: Windows UCS-4
            else if (platformID == 3 && encodingID == 1)
                priority = 1; // fallback: Windows BMP
            else
                continue;

            if (priority > bestPriority)
            {
                bestPriority = priority;
                bestOffset = subtableOffset;
            }
        }

        if (bestOffset < 0 || bestOffset >= table.Length)
            throw new FontParsingException("cmap", 0, "No supported cmap encoding found (need Windows platform).");

        var subtable = table.Slice(bestOffset);
        var format = BinaryPrimitives.ReadUInt16BigEndian(subtable);

        var cmap = new Dictionary<int, int>();

        if (format == 12)
            ParseCmapFormat12(subtable, cmap);
        else if (format == 4)
            ParseCmapFormat4(subtable, cmap);
        else
            throw new FontParsingException("cmap", bestOffset, $"Unsupported cmap format {format}.");

        CmapTable = new ReadOnlyDictionary<int, int>(cmap);

        // Build relevant glyph index set for kern/GPOS filtering when subsetting is active.
        if (_requestedCodepoints != null)
            _relevantGlyphIndices = new HashSet<int>(cmap.Values);
    }

    private void ParseCmapFormat12(ReadOnlySpan<byte> subtable, Dictionary<int, int> cmap)
    {
        if (subtable.Length < 16)
            return;

        var numGroups = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(12));

        for (var i = 0; i < numGroups; i++)
        {
            var groupOffset = 16 + i * 12;
            if (groupOffset + 12 > subtable.Length)
                break;

            var startCharCode = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(groupOffset));
            var endCharCode = Math.Min((int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(groupOffset + 4)), 0x10FFFF);
            var startGlyphID = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(groupOffset + 8));

            for (var c = startCharCode; c <= endCharCode; c++)
            {
                if (_requestedCodepoints != null && !_requestedCodepoints.Contains(c))
                    continue;
                cmap[c] = startGlyphID + (c - startCharCode);
            }
        }
    }

    private void ParseCmapFormat4(ReadOnlySpan<byte> subtable, Dictionary<int, int> cmap)
    {
        if (subtable.Length < 14)
            return;

        var segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(6));
        var segCount = segCountX2 / 2;

        // Arrays start at offset 14
        var endCodeOffset = 14;
        // reservedPad (uint16) after endCode array
        var startCodeOffset = endCodeOffset + segCountX2 + 2;
        var idDeltaOffset = startCodeOffset + segCountX2;
        var idRangeOffsetOffset = idDeltaOffset + segCountX2;

        if (idRangeOffsetOffset + segCountX2 > subtable.Length)
            return;

        for (var i = 0; i < segCount; i++)
        {
            var endCode = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(endCodeOffset + i * 2));
            var startCode = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(startCodeOffset + i * 2));
            var idDelta = BinaryPrimitives.ReadInt16BigEndian(subtable.Slice(idDeltaOffset + i * 2));
            var idRangeOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(idRangeOffsetOffset + i * 2));

            if (startCode == 0xFFFF)
                break;

            for (int c = startCode; c <= endCode; c++)
            {
                int glyphIndex;
                if (idRangeOffset == 0)
                {
                    glyphIndex = (c + idDelta) & 0xFFFF;
                }
                else
                {
                    // idRangeOffset is relative to the current position in the idRangeOffset array
                    var rangeOffsetLocation = idRangeOffsetOffset + i * 2;
                    var glyphIndexOffset = rangeOffsetLocation + idRangeOffset + (c - startCode) * 2;
                    if (glyphIndexOffset + 2 > subtable.Length)
                        continue;
                    glyphIndex = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(glyphIndexOffset));
                    if (glyphIndex != 0)
                        glyphIndex = (glyphIndex + idDelta) & 0xFFFF;
                }

                if (glyphIndex != 0)
                {
                    if (_requestedCodepoints != null && !_requestedCodepoints.Contains(c))
                        continue;
                    cmap[c] = glyphIndex;
                }
            }
        }
    }

    // 2H: kern Table
    private void ParseKern()
    {
        var table = GetTable(Tag('k', 'e', 'r', 'n'));
        if (table.IsEmpty)
        {
            KernPairs = Array.Empty<KerningPair>();
            return;
        }

        if (table.Length < 4)
        {
            KernPairs = Array.Empty<KerningPair>();
            return;
        }

        var version = BinaryPrimitives.ReadUInt16BigEndian(table);

        // Version 1 (AAT) — skip
        if (version == 1)
        {
            KernPairs = Array.Empty<KerningPair>();
            return;
        }

        // Version 0: Microsoft format
        var nTables = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(2));
        var pairs = new List<KerningPair>();
        var offset = 4;

        for (var t = 0; t < nTables; t++)
        {
            if (offset + 6 > table.Length)
                break;

            // Subtable header: version (uint16), length (uint16), coverage (uint16)
            var subtableLength = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(offset + 2));
            var coverage = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(offset + 4));

            // Format is in high byte of coverage; we only handle format 0
            var format = coverage >> 8;
            if (format == 0)
            {
                // Format 0 header: nPairs (uint16), searchRange, entrySelector, rangeShift
                if (offset + 14 > table.Length)
                    break;

                var nPairs = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(offset + 6));
                var pairOffset = offset + 14;

                for (var p = 0; p < nPairs; p++)
                {
                    var entryOffset = pairOffset + p * 6;
                    if (entryOffset + 6 > table.Length)
                        break;

                    var left = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(entryOffset));
                    var right = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(entryOffset + 2));
                    var value = BinaryPrimitives.ReadInt16BigEndian(table.Slice(entryOffset + 4));

                    if (value != 0)
                    {
                        if (_relevantGlyphIndices != null &&
                            (!_relevantGlyphIndices.Contains(left) || !_relevantGlyphIndices.Contains(right)))
                            continue;
                        pairs.Add(new KerningPair(left, right, value));
                    }
                }
            }

            offset += subtableLength;
        }

        KernPairs = pairs;
    }

    // 4A: GPOS Table — extract kerning pairs from PairPos (Type 2) lookups
    private void ParseGpos()
    {
        var table = GetTable(Tag('G', 'P', 'O', 'S'));
        if (table.IsEmpty)
            return;

        // GPOS header: version (uint16 major + uint16 minor), scriptListOffset (uint16), featureListOffset (uint16), lookupListOffset (uint16)
        if (table.Length < 10)
            return;

        var featureListOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6));
        var lookupListOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(8));

        // Find kern feature lookup indices
        var kernLookupIndices = FindKernFeatureLookups(table, featureListOffset);
        if (kernLookupIndices.Count == 0)
            return;

        // Parse LookupList
        var lookupList = table.Slice(lookupListOffset);
        if (lookupList.Length < 2)
            return;

        var lookupCount = BinaryPrimitives.ReadUInt16BigEndian(lookupList);
        var pairs = new List<KerningPair>();

        foreach (var lookupIndex in kernLookupIndices)
        {
            if (lookupIndex >= lookupCount)
                continue;

            var lookupOffset = BinaryPrimitives.ReadUInt16BigEndian(lookupList.Slice(2 + lookupIndex * 2));
            var lookup = lookupList.Slice(lookupOffset);
            if (lookup.Length < 6)
                continue;

            var lookupType = BinaryPrimitives.ReadUInt16BigEndian(lookup);
            // skip flag at offset 2
            var subTableCount = BinaryPrimitives.ReadUInt16BigEndian(lookup.Slice(4));

            for (var s = 0; s < subTableCount; s++)
            {
                if (6 + (s + 1) * 2 > lookup.Length)
                    break;

                var subTableOffset = BinaryPrimitives.ReadUInt16BigEndian(lookup.Slice(6 + s * 2));
                var subtable = lookup.Slice(subTableOffset);
                var effectiveType = lookupType;

                // Handle Extension Lookup (Type 9)
                if (effectiveType == 9)
                {
                    if (subtable.Length < 8)
                        continue;
                    // format (uint16), extensionLookupType (uint16), extensionOffset (uint32)
                    effectiveType = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(2));
                    var extensionOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(4));
                    subtable = lookup.Slice(subTableOffset + extensionOffset);
                }

                // Only handle PairPos (Type 2)
                if (effectiveType == 2)
                    ParsePairPos(subtable, pairs);
            }
        }

        GposPairs = pairs;
    }

    private static List<int> FindKernFeatureLookups(ReadOnlySpan<byte> table, int featureListOffset)
    {
        var result = new List<int>();
        var featureList = table.Slice(featureListOffset);
        if (featureList.Length < 2)
            return result;

        var featureCount = BinaryPrimitives.ReadUInt16BigEndian(featureList);
        uint kernTag = Tag('k', 'e', 'r', 'n');

        for (var i = 0; i < featureCount; i++)
        {
            var recOffset = 2 + i * 6;
            if (recOffset + 6 > featureList.Length)
                break;

            var tag = BinaryPrimitives.ReadUInt32BigEndian(featureList.Slice(recOffset));
            if (tag != kernTag)
                continue;

            var offset = BinaryPrimitives.ReadUInt16BigEndian(featureList.Slice(recOffset + 4));
            var featureTable = featureList.Slice(offset);
            if (featureTable.Length < 4)
                continue;

            // featureParams (uint16, ignore), lookupCount (uint16), lookupListIndices (uint16[])
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

    private void ParsePairPos(ReadOnlySpan<byte> subtable, List<KerningPair> pairs)
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
            ParsePairPosFormat1(subtable, coverageOffset, valueFormat1, valueRecord1Size, valueRecord2Size, pairs);
        else if (posFormat == 2)
            ParsePairPosFormat2(subtable, coverageOffset, valueFormat1, valueRecord1Size, valueRecord2Size, pairs);
    }

    private void ParsePairPosFormat1(
        ReadOnlySpan<byte> subtable, int coverageOffset,
        int valueFormat1, int valueRecord1Size, int valueRecord2Size,
        List<KerningPair> pairs)
    {
        if (subtable.Length < 10)
            return;

        var pairSetCount = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(8));
        var coverage = ParseCoverage(subtable.Slice(coverageOffset));

        for (var i = 0; i < pairSetCount; i++)
        {
            if (10 + (i + 1) * 2 > subtable.Length)
                break;

            var pairSetOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(10 + i * 2));
            var pairSet = subtable.Slice(pairSetOffset);
            if (pairSet.Length < 2)
                continue;

            var pairValueCount = BinaryPrimitives.ReadUInt16BigEndian(pairSet);
            // Each record: secondGlyph (uint16) + value1 + value2
            var recordSize = 2 + valueRecord1Size + valueRecord2Size;

            // Find the first glyph index from coverage that maps to this pair set index
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
                    if (_relevantGlyphIndices != null &&
                        (!_relevantGlyphIndices.Contains(firstGlyph) || !_relevantGlyphIndices.Contains(secondGlyph)))
                        continue;
                    pairs.Add(new KerningPair(firstGlyph, secondGlyph, xAdvance));
                }
            }
        }
    }

    private void ParsePairPosFormat2(
        ReadOnlySpan<byte> subtable, int coverageOffset,
        int valueFormat1, int valueRecord1Size, int valueRecord2Size,
        List<KerningPair> pairs)
    {
        // Format 2 header after the common 8 bytes:
        // classDef1Offset (uint16), classDef2Offset (uint16), class1Count (uint16), class2Count (uint16)
        if (subtable.Length < 16)
            return;

        var classDef1Offset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(8));
        var classDef2Offset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(10));
        var class1Count = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(12));
        var class2Count = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(14));

        var coverage = ParseCoverage(subtable.Slice(coverageOffset));
        var classDef1 = ParseClassDef(subtable.Slice(classDef1Offset));
        var classDef2 = ParseClassDef(subtable.Slice(classDef2Offset));

        // Build reverse map: class1 -> list of glyph indices
        var class1ToGlyphs = new Dictionary<int, List<int>>();
        // Coverage defines which glyphs are the "first" glyphs
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

        // Build reverse map: class2 -> list of glyph indices
        // For class2, we need all glyphs that could be the second glyph.
        // Glyphs not in classDef2 belong to class 0.
        var class2ToGlyphs = new Dictionary<int, List<int>>();
        foreach (var (glyph, cls) in classDef2)
        {
            if (!class2ToGlyphs.TryGetValue(cls, out var list))
            {
                list = new List<int>();
                class2ToGlyphs[cls] = list;
            }
            list.Add(glyph);
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
                    if (_relevantGlyphIndices != null && !_relevantGlyphIndices.Contains(left))
                        continue;
                    foreach (var right in rightGlyphs)
                    {
                        if (_relevantGlyphIndices != null && !_relevantGlyphIndices.Contains(right))
                            continue;
                        pairs.Add(new KerningPair(left, right, xAdvance));
                    }
                }
            }
        }
    }

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
                var glyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4 + i * 2));
                result.Add((glyph, i));
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

                var startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset));
                var endGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 2));
                var startIndex = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 4));

                for (var g = startGlyph; g <= endGlyph; g++)
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
            var startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
            for (var i = 0; i < glyphCount; i++)
            {
                if (6 + (i + 1) * 2 > data.Length)
                    break;
                var classValue = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6 + i * 2));
                result[startGlyph + i] = classValue;
            }
        }
        else if (format == 2)
        {
            var classRangeCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            for (var i = 0; i < classRangeCount; i++)
            {
                var recOffset = 4 + i * 6;
                if (recOffset + 6 > data.Length)
                    break;

                var startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset));
                var endGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 2));
                var classValue = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 4));

                for (var g = startGlyph; g <= endGlyph; g++)
                    result[g] = classValue;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate the byte size of a ValueRecord based on the valueFormat bitmask.
    /// Each set bit contributes 2 bytes (one int16 field).
    /// </summary>
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

    /// <summary>
    /// Extract XAdvance (bit 2, 0x0004) from a ValueRecord.
    /// Count the set bits below bit 2 to find its byte offset.
    /// </summary>
    private static int ReadXAdvanceFromValueRecord(ReadOnlySpan<byte> record, int valueFormat)
    {
        if ((valueFormat & 0x0004) == 0)
            return 0;

        // Count set bits below bit 2 (bits 0 and 1)
        var fieldsBefore = 0;
        if ((valueFormat & 0x0001) != 0) fieldsBefore++;
        if ((valueFormat & 0x0002) != 0) fieldsBefore++;

        var byteOffset = fieldsBefore * 2;
        if (byteOffset + 2 > record.Length)
            return 0;

        return BinaryPrimitives.ReadInt16BigEndian(record.Slice(byteOffset));
    }
}
