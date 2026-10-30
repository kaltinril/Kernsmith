using System.Buffers;
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
    public HmtxTable? Hmtx { get; private set; }
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

    /// <summary>
    /// Indicates whether the font was parsed successfully. False when the font
    /// lacks a supported cmap encoding (e.g. no Windows-platform subtable).
    /// </summary>
    public bool IsValid { get; private set; }

    public TtfParser(ReadOnlySpan<byte> fontData, int faceIndex = 0, HashSet<int>? requestedCodepoints = null, bool parseAdvancedTables = true)
    {
        _data = fontData.ToArray();
        _requestedCodepoints = requestedCodepoints;
        IsValid = true;

        ParseTableDirectory(fontData, faceIndex);
        ParseHead();
        ParseHhea();
        ParseHmtx();
        ParseOs2();
        ParseName();
        ParseCmap();
        if (!IsValid) return;
        if (parseAdvancedTables)
        {
            ParseKern();
            ParseGpos();
            ParseFvar();
            DetectColorTables();
        }
    }

    /// <summary>
    /// Creates a parser that shares an existing byte array (avoids an extra copy).
    /// </summary>
    internal TtfParser(byte[] fontBytes, int faceIndex = 0, HashSet<int>? requestedCodepoints = null, bool parseAdvancedTables = true)
    {
        _data = fontBytes;
        _requestedCodepoints = requestedCodepoints;
        IsValid = true;

        ParseTableDirectory(fontBytes, faceIndex);
        ParseHead();
        ParseHhea();
        ParseHmtx();
        ParseOs2();
        ParseName();
        ParseCmap();
        if (!IsValid) return;
        if (parseAdvancedTables)
        {
            ParseKern();
            ParseGpos();
            ParseFvar();
            DetectColorTables();
        }
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
            if (12L + (faceIndex + 1L) * 4 > data.Length)
                throw new FontParsingException($"TTC offset table for face {faceIndex} runs past the end of the data.");
            var faceOffset = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(12 + faceIndex * 4));
            if (faceOffset > (uint)data.Length - 4)
                throw new FontParsingException($"TTC face {faceIndex} starts at {faceOffset}, past the end of the {data.Length}-byte data.");
            directoryOffset = (int)faceOffset;
            magic = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(directoryOffset));
        }

        if (magic != TrueTypeMagic && magic != OttoMagic)
            throw new FontParsingException($"Invalid sfnt magic: 0x{magic:X8}. Expected TrueType (0x00010000) or OTF ('OTTO').");

        if ((long)directoryOffset + 12 > data.Length)
            throw new FontParsingException("The font's offset table is truncated.");
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(directoryOffset + 4));
        var recordStart = directoryOffset + 12;
        if ((long)recordStart + numTables * 16L > data.Length)
            throw new FontParsingException($"The table directory declares {numTables} tables but the data ends inside it.");

        for (var i = 0; i < numTables; i++)
        {
            var recOffset = recordStart + i * 16;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(recOffset));
            // skip checksum at recOffset + 4
            var tableOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(recOffset + 8));
            var tableLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(recOffset + 12));
            if (tableOffset < 0 || tableLength < 0 || (long)tableOffset + tableLength > data.Length)
                continue; // Skip invalid table entry
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

    // 2D: hmtx Table — per-glyph advance width and left side bearing, in font design units.
    // Note: FreeType (used by the rasterizer) also provides per-glyph advance widths via
    // FT_Load_Glyph, but that's only available at rasterization time. Parsing hmtx here lets
    // FontInfo expose design-space metrics for measurement (e.g. word-wrap) without rasterizing.
    private void ParseHmtx()
    {
        if (Hhea == null || Hhea.NumberOfHMetrics <= 0)
            return;

        var table = GetTable(Tag('h', 'm', 't', 'x'));
        if (table.IsEmpty)
            return;

        try
        {
            Hmtx = HmtxTable.Parse(table, Hhea.NumberOfHMetrics);
        }
        catch (FontFormatException)
        {
            // Malformed hmtx table — leave Hmtx null rather than throwing, matching the
            // nullable pattern used for other optional tables like Os2/Names.
        }
    }

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
            // long: axisCount x axisSize can exceed int.MaxValue in a damaged table.
            var axisOffset = axesArrayOffset + (long)i * axisSize;
            if (axisOffset + 20 > table.Length)
                break;

            var axisRecord = table.Slice((int)axisOffset);
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
        var instancesStart = axesArrayOffset + (long)axisCount * axisSize;

        // An instance's coordinates are named by the axes; if any axis record was unreadable,
        // the instances cannot be mapped.
        if (axisTags.Count < axisCount)
        {
            NamedInstances = Array.Empty<NamedInstance>();
            return;
        }

        for (var i = 0; i < instanceCount; i++)
        {
            var instOffset = instancesStart + (long)i * instanceSize;
            // Each instance: subfamilyNameID(2) + flags(2) + axisCount * coordinate(4)
            var minRequired = 4 + axisCount * 4;
            if (instOffset + minRequired > table.Length)
                break;

            var instRecord = table.Slice((int)instOffset);
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

        if (bestOffset < 0 || bestOffset > table.Length - 2)
        {
            IsValid = false;
            return;
        }

        var subtable = table.Slice(bestOffset);
        var format = BinaryPrimitives.ReadUInt16BigEndian(subtable);

        var cmap = new Dictionary<int, int>();

        if (format == 12)
            ParseCmapFormat12(subtable, cmap);
        else if (format == 4)
            ParseCmapFormat4(subtable, cmap);
        else
        {
            IsValid = false;
            return;
        }

        CmapTable = new ReadOnlyDictionary<int, int>(cmap);

        // Build relevant glyph index set for kern/GPOS filtering when subsetting is active.
        if (_requestedCodepoints != null)
            _relevantGlyphIndices = new HashSet<int>(cmap.Values);
    }

    /// <summary>The last Unicode code point.</summary>
    private const uint MaxCodepoint = 0x10FFFF;

    private void ParseCmapFormat12(ReadOnlySpan<byte> subtable, Dictionary<int, int> cmap)
    {
        if (subtable.Length < 16)
            return;

        var numGroups = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(12));

        if (numGroups < 0 || 16L + (long)numGroups * 12 > subtable.Length)
            return;

        for (var i = 0; i < numGroups; i++)
        {
            var groupOffset = 16 + i * 12;
            if (groupOffset + 12 > subtable.Length)
                break;

            // Read unsigned: a code above 0x7FFFFFFF cast straight to int is negative and turned
            // the range loop into billions of iterations. Codes past Unicode are clamped/skipped.
            var rawStart = BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(groupOffset));
            var rawEnd = BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(groupOffset + 4));
            if (rawStart > MaxCodepoint || rawEnd < rawStart)
                continue;
            var startCharCode = (int)rawStart;
            var endCharCode = (int)Math.Min(rawEnd, MaxCodepoint);
            var startGlyphID = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(groupOffset + 8));

            // With a requested subset, visit only the requested codepoints a long range contains:
            // overlapping full-Unicode groups would otherwise cost a million lookups each.
            if (_requestedCodepoints != null && endCharCode - startCharCode >= _requestedCodepoints.Count)
            {
                foreach (var c in _requestedCodepoints)
                {
                    if (c >= startCharCode && c <= endCharCode)
                        cmap[c] = startGlyphID + (c - startCharCode);
                }

                continue;
            }

            for (var c = startCharCode; c <= endCharCode; c++)
            {
                if (_requestedCodepoints != null && !_requestedCodepoints.Contains(c))
                    continue;
                cmap[c] = startGlyphID + (c - startCharCode);

                if (cmap.Count > 200_000)
                    throw new FontParsingException("cmap table exceeds maximum entry limit (200,000).");
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

        // Segments may not overlap, so a valid subtable visits at most 65536 codes in total.
        var visited = 0;
        for (var i = 0; i < segCount; i++)
        {
            var endCode = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(endCodeOffset + i * 2));
            var startCode = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(startCodeOffset + i * 2));
            var idDelta = BinaryPrimitives.ReadInt16BigEndian(subtable.Slice(idDeltaOffset + i * 2));
            var idRangeOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(idRangeOffsetOffset + i * 2));

            if (startCode == 0xFFFF)
                break;

            if (endCode >= startCode)
            {
                visited += endCode - startCode + 1;
                if (visited > 0x10000)
                    throw new FontParsingException("cmap", 0, "format 4 segments overlap (they cover more than 65536 codes).");
            }

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
            if (subtableLength < 6)
                break; // Minimum valid subtable size
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
        var lookupList = From(table, lookupListOffset);
        if (lookupList.Length < 2)
            return;

        var lookupCount = BinaryPrimitives.ReadUInt16BigEndian(lookupList);
        var pairs = new List<KerningPair>();

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
            // skip flag at offset 2
            var subTableCount = BinaryPrimitives.ReadUInt16BigEndian(lookup.Slice(4));

            for (var s = 0; s < subTableCount; s++)
            {
                if (6 + (s + 1) * 2 > lookup.Length)
                    break;

                var subTableOffset = BinaryPrimitives.ReadUInt16BigEndian(lookup.Slice(6 + s * 2));
                var subtable = From(lookup, subTableOffset);
                var effectiveType = lookupType;

                // Handle Extension Lookup (Type 9)
                if (effectiveType == 9)
                {
                    if (subtable.Length < 8)
                        continue;
                    // format (uint16), extensionLookupType (uint16), extensionOffset (uint32)
                    effectiveType = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(2));
                    var extensionOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(subtable.Slice(4));
                    if (extensionOffset < 0 || (long)subTableOffset + extensionOffset >= lookup.Length)
                        continue;
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
        var featureList = From(table, featureListOffset);
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
            var featureTable = From(featureList, offset);
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

        // Pair sets whose offset fits in the subtable; the offset array ends the walk there.
        var pairSetCount = Math.Min(
            (int)BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(8)),
            (subtable.Length - 10) / 2);
        if (pairSetCount == 0)
            return;

        // The first glyph of pair set i is the first coverage entry (in table order) whose
        // coverage index is i; a set no entry points at has none and is skipped.
        var firstGlyphPool = ArrayPool<int>.Shared.Rent(pairSetCount);
        try
        {
            var firstGlyphOf = firstGlyphPool.AsSpan(0, pairSetCount);
            firstGlyphOf.Fill(-1);
            foreach (var (glyph, index) in ParseCoverage(From(subtable, coverageOffset)))
            {
                if ((uint)index < (uint)pairSetCount && firstGlyphOf[index] < 0)
                    firstGlyphOf[index] = glyph;
            }

            // Each record: secondGlyph (uint16) + value1 + value2
            ReadPairSets(subtable, firstGlyphOf, valueFormat1, 2 + valueRecord1Size + valueRecord2Size, pairs);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(firstGlyphPool);
        }
    }

    private void ReadPairSets(
        ReadOnlySpan<byte> subtable, ReadOnlySpan<int> firstGlyphOf, int valueFormat1, int recordSize,
        List<KerningPair> pairs)
    {
        var pairSetCount = firstGlyphOf.Length;
        for (var i = 0; i < pairSetCount; i++)
        {
            var firstGlyph = firstGlyphOf[i];
            if (firstGlyph < 0)
                continue;

            // A first glyph outside the subset filters out every pair in the set.
            if (_relevantGlyphIndices != null && !_relevantGlyphIndices.Contains(firstGlyph))
                continue;

            var pairSetOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable.Slice(10 + i * 2));
            var pairSet = From(subtable, pairSetOffset);
            if (pairSet.Length < 2)
                continue;

            var pairValueCount = BinaryPrimitives.ReadUInt16BigEndian(pairSet);

            for (var p = 0; p < pairValueCount; p++)
            {
                var recOffset = 2 + p * recordSize;
                if (recOffset + recordSize > pairSet.Length)
                    break;

                var xAdvance = ReadXAdvanceFromValueRecord(pairSet.Slice(recOffset + 2), valueFormat1);
                if (xAdvance == 0)
                    continue;

                var secondGlyph = BinaryPrimitives.ReadUInt16BigEndian(pairSet.Slice(recOffset));
                if (_relevantGlyphIndices != null && !_relevantGlyphIndices.Contains(secondGlyph))
                    continue;
                pairs.Add(new KerningPair(firstGlyph, secondGlyph, xAdvance));
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
        if (class1Count == 0 || class2Count == 0)
            return;

        var relevant = _relevantGlyphIndices;

        // First glyphs by class: the coverage glyphs in coverage order (duplicates kept), each
        // in its classDef1 class (unlisted = class 0), filtered to the subset up front rather
        // than per pair.
        var leftByClass = new List<int>?[class1Count];
        var anyLeft = false;
        var classDef1 = From(subtable, classDef1Offset);
        var class1 = RentClassTable(classDef1, out var class1Length);
        try
        {
            foreach (var (glyph, _) in ParseCoverage(From(subtable, coverageOffset)))
            {
                if (relevant != null && !relevant.Contains(glyph))
                    continue;
                var cls = glyph < class1Length && class1[glyph] != UnclassifiedGlyph ? class1[glyph] : 0;
                if (cls < class1Count)
                {
                    (leftByClass[cls] ??= new List<int>()).Add(glyph);
                    anyLeft = true;
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(class1);
        }

        if (!anyLeft)
            return;

        // Second glyphs by class, in the order a glyph -> class dictionary built from classDef2
        // would enumerate them (position of the first assignment, class of the last), filtered
        // to the subset; then class 0 gains every requested glyph classDef2 does not list, in
        // the subset's own enumeration order.
        var rightByClass = new List<int>?[class2Count];
        var classDef2 = From(subtable, classDef2Offset);
        var class2 = RentClassTable(classDef2, out var class2Length);
        try
        {
            VisitClassDef(classDef2, class2, rightByClass, relevant);
            if (relevant != null)
            {
                foreach (var glyph in relevant)
                {
                    if ((uint)glyph >= (uint)class2Length || class2[glyph] == UnclassifiedGlyph)
                        (rightByClass[0] ??= new List<int>()).Add(glyph);
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(class2);
        }

        var recordPairSize = valueRecord1Size + valueRecord2Size;
        const int arrayStart = 16;

        for (var c1 = 0; c1 < class1Count; c1++)
        {
            var leftGlyphs = leftByClass[c1];
            if (leftGlyphs == null)
                continue;

            for (var c2 = 0; c2 < class2Count; c2++)
            {
                // 64-bit: 65535 x 65535 classes overflow an int offset.
                var recordOffset = arrayStart + ((long)c1 * class2Count + c2) * recordPairSize;
                if (recordOffset + recordPairSize > subtable.Length)
                    break;

                var rightGlyphs = rightByClass[c2];
                if (rightGlyphs == null)
                    continue;

                var xAdvance = ReadXAdvanceFromValueRecord(subtable.Slice((int)recordOffset), valueFormat1);
                if (xAdvance == 0)
                    continue;

                foreach (var left in leftGlyphs)
                {
                    foreach (var right in rightGlyphs)
                        pairs.Add(new KerningPair(left, right, xAdvance));
                }
            }
        }
    }

    /// <summary>Marks a glyph a ClassDef does not list, in a <see cref="RentClassTable"/> table.</summary>
    private const int UnclassifiedGlyph = -1;

    /// <summary>Added to a class in a class table once <see cref="VisitClassDef"/> has listed its glyph.</summary>
    private const int ClassListed = 0x10000;

    /// <summary>
    /// A pooled glyph -> class table for a ClassDef: entry <c>g</c>, for <c>g</c> below
    /// <paramref name="length"/>, is the class of the ClassDef's last assignment to <c>g</c>, or
    /// <see cref="UnclassifiedGlyph"/>; glyphs at or past <paramref name="length"/> are
    /// unclassified. Return it to <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    private static int[] RentClassTable(ReadOnlySpan<byte> classDef, out int length)
    {
        length = ClassDefGlyphLimit(classDef);
        var table = ArrayPool<int>.Shared.Rent(Math.Max(length, 1));
        table.AsSpan(0, length).Fill(UnclassifiedGlyph);
        VisitClassDef(classDef, table, null, null);
        return table;
    }

    /// <summary>One past the highest glyph id a ClassDef can assign a class to (0 when none).</summary>
    private static int ClassDefGlyphLimit(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return 0;

        var format = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (format == 1)
        {
            if (data.Length < 6)
                return 0;
            var startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            var glyphCount = Math.Min((int)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4)), (data.Length - 6) / 2);
            return glyphCount > 0 ? startGlyph + glyphCount : 0;
        }

        if (format == 2)
        {
            var limit = 0;
            var classRangeCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            for (var i = 0; i < classRangeCount; i++)
            {
                var recOffset = 4 + i * 6;
                if (recOffset + 6 > data.Length)
                    break;
                int startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset));
                int endGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 2));
                if (endGlyph >= startGlyph)
                    limit = Math.Max(limit, endGlyph + 1);
            }
            return limit;
        }

        return 0;
    }

    /// <summary>
    /// Walks a ClassDef's (glyph, class) assignments in table order, bounded as a ClassDef must
    /// be (format 2 visits at most <see cref="MaxGlyphIds"/> glyphs). With <paramref name="byClass"/>
    /// null it records each assignment in <paramref name="classes"/> (later ones win). Otherwise
    /// <paramref name="classes"/> is that finished table, and each glyph is listed once, at its
    /// first assignment, under its final class — skipping classes past the end of
    /// <paramref name="byClass"/> and glyphs outside <paramref name="relevant"/>; afterwards an
    /// entry is <see cref="UnclassifiedGlyph"/> exactly when it was before.
    /// </summary>
    private static void VisitClassDef(
        ReadOnlySpan<byte> data, int[] classes, List<int>?[]? byClass, HashSet<int>? relevant)
    {
        if (data.Length < 4)
            return;

        var format = BinaryPrimitives.ReadUInt16BigEndian(data);

        if (format == 1)
        {
            if (data.Length < 6)
                return;
            var startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
            var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
            for (var i = 0; i < glyphCount; i++)
            {
                if (6 + (i + 1) * 2 > data.Length)
                    break;
                var classValue = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6 + i * 2));
                Visit(startGlyph + i, classValue, classes, byClass, relevant);
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

                // An int counter, so a range ending at glyph 65535 terminates; overlapping ranges
                // are bounded by the visit budget rather than re-walking every glyph id each time.
                for (var g = startGlyph; g <= endGlyph && visited < MaxGlyphIds; g++, visited++)
                    Visit(g, classValue, classes, byClass, relevant);
            }
        }

        static void Visit(int glyph, int classValue, int[] classes, List<int>?[]? byClass, HashSet<int>? relevant)
        {
            if (byClass == null)
            {
                classes[glyph] = classValue;
                return;
            }

            var cls = classes[glyph];
            if (cls >= ClassListed)
                return;
            classes[glyph] = cls + ClassListed;
            if (cls < byClass.Length && (relevant == null || relevant.Contains(glyph)))
                (byClass[cls] ??= new List<int>()).Add(glyph);
        }
    }

    /// <summary>The number of glyph ids a font can have; a Coverage or ClassDef lists each at most once.</summary>
    private const int MaxGlyphIds = 65536;

    /// <summary>
    /// The bytes of <paramref name="span"/> from a subtable offset on, or empty when the offset
    /// points past the end: a damaged optional subtable is skipped, not fatal.
    /// </summary>
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

                int startGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset));
                int endGlyph = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 2));
                int startIndex = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(recOffset + 4));

                // An int counter, so a range ending at glyph 65535 terminates; and ranges may not
                // overlap, so more entries than there are glyph ids means the table is malformed.
                for (var g = startGlyph; g <= endGlyph && result.Count < MaxGlyphIds; g++)
                    result.Add((g, startIndex + (g - startGlyph)));
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
