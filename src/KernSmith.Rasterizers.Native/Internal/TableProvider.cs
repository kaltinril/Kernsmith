namespace KernSmith.Rasterizers.Native.Internal;

/// <summary>
/// Parses an SFNT (TrueType/OpenType) offset table and table directory, then provides
/// lazy, zero-copy access to each table's raw bytes by tag.
/// </summary>
/// <remarks>
/// Supports TrueType (<c>0x00010000</c>), the legacy Apple <c>true</c> tag, and
/// CFF-flavoured OpenType (<c>OTTO</c>). For TrueType Collections (<c>ttcf</c>) the
/// requested face's offset table is resolved first. CFF outline parsing itself is
/// deferred to a later phase; this provider only validates that the required tables
/// are present.
/// </remarks>
internal sealed class TableProvider
{
    /// <summary>sfnt version for TrueType outlines.</summary>
    private const uint SfntVersionTrueType = 0x00010000;

    /// <summary>sfnt version tag "true" used by some Apple fonts.</summary>
    private const uint SfntVersionTrue = 0x74727565; // 'true'

    /// <summary>sfnt version tag "OTTO" for CFF (PostScript) outlines.</summary>
    private const uint SfntVersionOtto = 0x4F54544F; // 'OTTO'

    /// <summary>Magic tag "ttcf" identifying a TrueType Collection.</summary>
    private const uint TtcTag = 0x74746366; // 'ttcf'

    private readonly ReadOnlyMemory<byte> _fontData;
    private readonly Dictionary<string, TableRecord> _tables;

    private TableProvider(ReadOnlyMemory<byte> fontData, Dictionary<string, TableRecord> tables, bool isCff)
    {
        _fontData = fontData;
        _tables = tables;
        IsCff = isCff;
    }

    /// <summary>True when the font uses CFF (PostScript) outlines rather than TrueType <c>glyf</c>.</summary>
    public bool IsCff { get; }

    /// <summary>The table tags present in this font face.</summary>
    public IReadOnlyCollection<string> TableTags => _tables.Keys;

    /// <summary>Returns true when a table with the given tag exists in this font face.</summary>
    public bool HasTable(string tag) => _tables.ContainsKey(tag);

    /// <summary>Returns the table directory record for the given tag, or null if absent.</summary>
    public TableRecord? TryGetRecord(string tag) =>
        _tables.TryGetValue(tag, out var record) ? record : null;

    /// <summary>
    /// Returns the raw bytes of the named table, or null if the table is not present.
    /// The returned memory aliases the font data; no copy is made.
    /// </summary>
    /// <exception cref="FontFormatException">If the table's byte range is out of bounds.</exception>
    public ReadOnlyMemory<byte>? TryGetTable(string tag)
    {
        if (!_tables.TryGetValue(tag, out var record))
            return null;

        long end = (long)record.Offset + record.Length;
        if (end > _fontData.Length)
            throw new FontFormatException(tag, (int)record.Offset,
                $"table extends to {end} bytes but the font is only {_fontData.Length} bytes.");

        return _fontData.Slice((int)record.Offset, (int)record.Length);
    }

    /// <summary>
    /// Returns the raw bytes of the named table.
    /// </summary>
    /// <exception cref="FontFormatException">If the table is missing or out of bounds.</exception>
    public ReadOnlyMemory<byte> GetTable(string tag) =>
        TryGetTable(tag) ?? throw new FontFormatException($"Required table '{tag}' is missing from the font.");

    /// <summary>
    /// Parses the offset table and table directory for the requested face.
    /// </summary>
    /// <param name="fontData">The full font file bytes.</param>
    /// <param name="faceIndex">Which face to load from a TrueType Collection. Usually 0.</param>
    /// <exception cref="FontFormatException">If the data is not a recognized/valid font.</exception>
    public static TableProvider Parse(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        if (faceIndex < 0)
            throw new FontFormatException($"Face index must be non-negative, got {faceIndex}.");

        var reader = new FontReader(fontData.Span);

        if (reader.Remaining < 4)
            throw new FontFormatException("Font data is too small to contain an SFNT header.");

        uint sfntVersion = reader.ReadUInt32();

        if (sfntVersion == TtcTag)
        {
            uint offsetTablePosition = ResolveTtcFaceOffset(ref reader, faceIndex);
            reader.Seek((int)offsetTablePosition);
            sfntVersion = reader.ReadUInt32();
        }

        bool isCff = sfntVersion == SfntVersionOtto;
        if (sfntVersion != SfntVersionTrueType && sfntVersion != SfntVersionTrue && !isCff)
            throw new FontFormatException(
                $"Unsupported sfnt version 0x{sfntVersion:X8}. Expected TrueType (0x00010000) or CFF (OTTO).");

        ushort numTables = reader.ReadUInt16();
        // searchRange, entrySelector, rangeShift — derived fields, not needed for parsing.
        reader.Skip(6);

        var tables = new Dictionary<string, TableRecord>(numTables, StringComparer.Ordinal);
        for (int i = 0; i < numTables; i++)
        {
            string tag = reader.ReadTag();
            uint checksum = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            uint length = reader.ReadUInt32();
            tables[tag] = new TableRecord(tag, checksum, offset, length);
        }

        return new TableProvider(fontData, tables, isCff);
    }

    private static uint ResolveTtcFaceOffset(ref FontReader reader, int faceIndex)
    {
        // ttcf header: tag (already read) + majorVersion(2) + minorVersion(2) + numFonts(4) + offsets[numFonts].
        reader.Skip(4); // major + minor version
        uint numFonts = reader.ReadUInt32();
        if ((uint)faceIndex >= numFonts)
            throw new FontFormatException(
                $"Face index {faceIndex} is out of range for a collection with {numFonts} face(s).");

        reader.Skip(faceIndex * 4);
        return reader.ReadUInt32();
    }
}
