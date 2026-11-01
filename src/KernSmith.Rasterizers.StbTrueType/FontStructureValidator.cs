using System.Buffers.Binary;

namespace KernSmith.Rasterizers.StbTrueType;

/// <summary>
/// Checks a font's structure against the OpenType specification before stb_truetype reads it,
/// in the spirit of the OpenType Sanitizer browsers run on web fonts. stb_truetype trusts every
/// offset, length and count in the file; this check makes a non-conforming font a
/// <see cref="FontParsingException"/> naming the table and the rule instead.
/// </summary>
/// <remarks>
/// <para>
/// The checks follow what stb_truetype reads, so where stb departs from the specification the
/// check follows stb: the first directory record of a tag wins, the last Unicode
/// <c>cmap</c> encoding record wins (platform 0, or platform 3 encoding 1 or 10), CFF offsets are
/// read with stb's own DICT integer decoding, and only the selected <c>cmap</c> subtable is read.
/// </para>
/// <para>
/// <see cref="Validate"/> runs once per font; <see cref="EnsureGlyph"/> once per glyph (cached).
/// Limits shared with the Native backend (component depth and total, charstring operations,
/// path commands, coordinate range and bitmap size) carry Native's values; keep them in step
/// with <c>GlyfTable</c>, <c>MaxpTable</c>, <c>Type2Charstring</c> and <c>GlyphLimits</c> there.
/// </para>
/// </remarks>
internal sealed partial class FontStructureValidator
{
    /// <summary>Largest bitmap side, in pixels, padding included (Native's <c>GlyphLimits.MaxDimension</c>).</summary>
    internal const int MaxBitmapDimension = 16384;

    private const uint TagTtcf = 0x74746366;     // 'ttcf'
    private const uint VersionTrueType = 0x00010000;
    private const uint VersionOtto = 0x4F54544F; // 'OTTO'
    private const uint VersionTrue = 0x74727565; // 'true' (Apple TrueType)

    private readonly byte[] _data;
    private readonly TableRange _cmap;
    private readonly TableRange _loca;
    private readonly TableRange _glyf;
    private readonly bool _longLoca;
    private readonly bool[] _glyphChecked;

    private FontStructureValidator(byte[] data, int faceOffset)
    {
        _data = data;
        FaceOffset = faceOffset;

        int numTables = U16(faceOffset + 4);
        long directoryEnd = faceOffset + 12L + (16L * numTables);
        if (directoryEnd > data.Length)
            throw Fail("sfnt", faceOffset + 4, $"table directory runs past the end of the file ({numTables} records).");

        var head = Require(faceOffset, numTables, "head");
        var hhea = Require(faceOffset, numTables, "hhea");
        var maxp = Require(faceOffset, numTables, "maxp");
        var hmtx = Require(faceOffset, numTables, "hmtx");
        _cmap = Require(faceOffset, numTables, "cmap");

        // stb_truetype draws glyf outlines whenever a glyf table is present, else CFF.
        var glyf = Find(faceOffset, numTables, "glyf");
        IsCff = glyf is null;

        if (head.Length < 54)
            throw Fail("head", head.Offset, $"table is {head.Length} bytes, shorter than 54 bytes.");
        int unitsPerEm = U16(head.Offset + 18);
        if (unitsPerEm is < 16 or > 16384)
            throw Fail("head", head.Offset + 18, $"unitsPerEm {unitsPerEm} is outside the spec range 16-16384.");
        int indexToLocFormat = S16(head.Offset + 50);
        if (indexToLocFormat is not (0 or 1))
            throw Fail("head", head.Offset + 50, $"indexToLocFormat {indexToLocFormat} is neither 0 nor 1.");

        if (hhea.Length < 36)
            throw Fail("hhea", hhea.Offset, $"table is {hhea.Length} bytes, shorter than 36 bytes.");

        if (maxp.Length < 6)
            throw Fail("maxp", maxp.Offset, $"table is {maxp.Length} bytes, shorter than 6 bytes.");
        NumGlyphs = U16(maxp.Offset + 4);
        if (NumGlyphs == 0)
            throw Fail("maxp", maxp.Offset + 4, "numGlyphs is 0; a font needs at least the .notdef glyph.");

        int numberOfHMetrics = U16(hhea.Offset + 34);
        if (numberOfHMetrics < 1 || numberOfHMetrics > NumGlyphs)
            throw Fail("hhea", hhea.Offset + 34, $"numberOfHMetrics {numberOfHMetrics} is outside 1-{NumGlyphs} (numGlyphs).");
        long hmtxNeed = (4L * numberOfHMetrics) + (2L * (NumGlyphs - numberOfHMetrics));
        if (hmtx.Length < hmtxNeed)
            throw Fail("hmtx", hmtx.Offset,
                $"table is {hmtx.Length} bytes; {numberOfHMetrics} long metrics and {NumGlyphs - numberOfHMetrics} "
                + $"left side bearings need {hmtxNeed}.");

        if (!IsCff)
        {
            _glyf = glyf!.Value;
            _loca = Require(faceOffset, numTables, "loca");
            _longLoca = indexToLocFormat == 1;
            ValidateLoca();
        }
        else
        {
            ValidateCff(Find(faceOffset, numTables, "CFF ") ?? throw Fail("CFF ", faceOffset + 12,
                "required table is missing: the font has neither glyf nor CFF outlines (stb_truetype reads no CFF2 or bitmap-only fonts)."));
        }

        ValidateCmap();
        _glyphChecked = new bool[NumGlyphs];
    }

    /// <summary>Where the selected face's table directory starts, as <c>stbtt_InitFont</c> takes it.</summary>
    public int FaceOffset { get; }

    /// <summary><c>maxp.numGlyphs</c>.</summary>
    public int NumGlyphs { get; }

    /// <summary>True when the outlines are <c>CFF </c> charstrings rather than <c>glyf</c>.</summary>
    public bool IsCff { get; }

    /// <summary>
    /// True when the only usable <c>cmap</c> subtable is a Windows symbol one (3,0), whose glyphs
    /// sit at U+F020-U+F0FF; see <see cref="ValidateCmap"/>.
    /// </summary>
    public bool IsSymbolCmap { get; private set; }

    /// <summary>
    /// Checks the file header (or TTC header and face index), the table directory and every
    /// table the backend reads: <c>head</c>, <c>hhea</c>, <c>maxp</c>, <c>hmtx</c>, <c>cmap</c>,
    /// and <c>loca</c>/<c>glyf</c> or <c>CFF </c>.
    /// </summary>
    /// <exception cref="FontParsingException">If the font breaks a structural rule.</exception>
    public static FontStructureValidator Validate(byte[] data, int faceIndex)
    {
        if (faceIndex < 0)
            throw Fail("sfnt", 0, $"face index {faceIndex} is negative.");
        if (data.Length < 12)
            throw Fail("sfnt", 0, $"file is shorter than the 12-byte sfnt header ({data.Length} bytes).");

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(data);
        int faceOffset = 0;
        if (tag == TagTtcf)
        {
            uint version = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            if (version is not (0x00010000 or 0x00020000))
                throw Fail("ttcf", 4, $"unsupported TTC version 0x{version:X8}.");
            uint numFonts = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
            if (12L + (4L * numFonts) > data.Length)
                throw Fail("ttcf", 8, $"offset array runs past the end of the file ({numFonts} fonts).");
            if ((uint)faceIndex >= numFonts)
                throw Fail("ttcf", 8, $"face index {faceIndex} is out of range; the collection holds {numFonts} fonts.");
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + (4 * faceIndex)));
            if (offset > (uint)(data.Length - 12))
                throw Fail("ttcf", 12 + (4 * faceIndex), $"face {faceIndex} starts past the end of the file (offset {offset}).");
            faceOffset = (int)offset;
            tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(faceOffset));
        }
        else if (faceIndex != 0)
        {
            throw Fail("sfnt", 0, $"face index {faceIndex} requested from a single-font file.");
        }

        if (tag is not (VersionTrueType or VersionOtto or VersionTrue))
            throw Fail("sfnt", faceOffset, $"unknown sfnt version 0x{tag:X8}.");

        return new FontStructureValidator(data, faceOffset);
    }

    /// <summary>
    /// Maps a glyph index from <c>stbtt_FindGlyphIndex</c> to itself when it names a glyph, and to
    /// 0 (unmapped) when it is outside 0..numGlyphs-1, as FreeType treats an out-of-range <c>cmap</c> entry.
    /// </summary>
    public int ResolveGlyph(int glyphIndex) => (uint)glyphIndex < (uint)NumGlyphs ? glyphIndex : 0;

    /// <summary>
    /// Checks one glyph's outline data — its <c>glyf</c> record and every component, or its
    /// charstring and every subroutine it calls — once; later calls for the same glyph return at once.
    /// </summary>
    /// <exception cref="FontParsingException">If the glyph's data breaks a structural rule or a work limit.</exception>
    public void EnsureGlyph(int glyphIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(glyphIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(glyphIndex, NumGlyphs);
        if (_glyphChecked[glyphIndex])
            return;

        if (IsCff)
            CheckCharstring(glyphIndex);
        else
            CheckGlyfTree(glyphIndex);

        _glyphChecked[glyphIndex] = true;
    }

    /// <summary>
    /// Rejects a bitmap with a side outside 0..<see cref="MaxBitmapDimension"/> px (or not a number),
    /// so damaged metrics or an extreme size, DPI or SDF spread fail before stb_truetype allocates it.
    /// </summary>
    /// <exception cref="RasterizationException">If either side is out of range.</exception>
    public static void EnsureBitmap(int glyphIndex, double width, double height)
    {
        if (!(width >= 0 && width <= MaxBitmapDimension && height >= 0 && height <= MaxBitmapDimension))
            throw new RasterizationException(
                $"Glyph {glyphIndex} would be a {width}x{height} px bitmap, outside the 0-{MaxBitmapDimension} px limit per side.");
    }

    /// <summary>
    /// Rejects the bitmap stb_truetype would allocate for a font-unit box at <paramref name="scale"/>
    /// (floor of the low edge to ceiling of the high edge, y flipped), grown by
    /// <paramref name="padding"/> on every side. Computed in double precision, so a huge box
    /// cannot wrap the way stb's 32-bit arithmetic would.
    /// </summary>
    public static void EnsureBitmapBox(int glyphIndex, double x0, double y0, double x1, double y1, double scale, int padding)
    {
        double width = Math.Ceiling(x1 * scale) - Math.Floor(x0 * scale) + (2.0 * padding);
        double height = Math.Ceiling(-y0 * scale) - Math.Floor(-y1 * scale) + (2.0 * padding);
        EnsureBitmap(glyphIndex, width, height);
    }

    // ------------------------------------------------------------------ directory

    private readonly record struct TableRange(int Offset, int Length);

    /// <summary>The first directory record with <paramref name="tag"/> (stb_truetype's rule), bounds-checked.</summary>
    private TableRange? Find(int faceOffset, int numTables, string tag)
    {
        for (int i = 0; i < numTables; i++)
        {
            int record = faceOffset + 12 + (16 * i);
            if (_data[record] != tag[0] || _data[record + 1] != tag[1] || _data[record + 2] != tag[2] || _data[record + 3] != tag[3])
                continue;

            uint offset = U32(record + 8);
            uint length = U32(record + 12);
            if (offset == 0)
                throw Fail(tag, record + 8, "table offset 0 overlaps the file header.");
            if ((long)offset + length > _data.Length)
                throw Fail(tag, record + 8,
                    $"table ({length} bytes at offset {offset}) runs past the end of the file ({_data.Length} bytes).");
            return new TableRange((int)offset, (int)length);
        }

        return null;
    }

    private TableRange Require(int faceOffset, int numTables, string tag) =>
        Find(faceOffset, numTables, tag) ?? throw Fail(tag, faceOffset + 12, "required table is missing.");

    // ------------------------------------------------------------------ loca

    private void ValidateLoca()
    {
        int entrySize = _longLoca ? 4 : 2;
        long need = (NumGlyphs + 1L) * entrySize;
        if (_loca.Length < need)
            throw Fail("loca", _loca.Offset,
                $"table is {_loca.Length} bytes; {NumGlyphs + 1} offsets need {need} ({(_longLoca ? "long" : "short")} format).");

        long previous = 0;
        for (int g = 0; g <= NumGlyphs; g++)
        {
            long offset = LocaEntry(g);
            if (offset < previous)
                throw Fail("loca", _loca.Offset + (g * entrySize), $"offsets decrease at glyph {g} ({offset} after {previous}).");
            previous = offset;
        }

        if (previous > _glyf.Length)
            throw Fail("loca", _loca.Offset + (NumGlyphs * entrySize),
                $"last offset {previous} is past the end of glyf ({_glyf.Length} bytes).");
    }

    private long LocaEntry(int glyph) =>
        _longLoca ? U32(_loca.Offset + (glyph * 4)) : U16(_loca.Offset + (glyph * 2)) * 2L;

    // ------------------------------------------------------------------ readers

    private int U16(int pos) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(pos, 2));

    private int S16(int pos) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(pos, 2));

    private uint U32(int pos) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(pos, 4));

    private static FontParsingException Fail(string tag, long offset, string details) =>
        new(tag, (int)Math.Clamp(offset, 0, int.MaxValue), details);
}
