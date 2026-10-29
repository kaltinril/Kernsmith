using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Internal.Bitmaps;

/// <summary>A PNG glyph image located in a <c>CBDT</c> strike, with the metrics stored for it (strike pixels).</summary>
/// <param name="Png">The PNG bytes.</param>
/// <param name="Width">Bitmap columns.</param>
/// <param name="Height">Bitmap rows.</param>
/// <param name="BearingX">Pen to the bitmap's left edge, in pixels.</param>
/// <param name="BearingY">Baseline up to the bitmap's top edge, in pixels.</param>
internal readonly record struct CbdtBitmap(ReadOnlyMemory<byte> Png, int Width, int Height, int BearingX, int BearingY);

/// <summary>
/// The <c>CBLC</c> (locations) and <c>CBDT</c> (data) color bitmap tables: strikes, their index
/// subtables (formats 1-5) and the PNG image formats 17, 18 and 19.
/// </summary>
/// <remarks>
/// The table header and the strike records are validated by <see cref="Parse"/>. Everything a
/// glyph lookup reads — index subtables, offsets, image headers — is bounds-checked as it is
/// read, and anything malformed, out of range or in an image format other than 17-19 (the EBDT
/// formats, uncompressed BGRA included) means "no bitmap for this glyph in this strike": the
/// glyph falls back to another strike, its outline, or nothing. Small glyph metrics are read as
/// horizontal metrics whatever the strike's direction flags say.
/// </remarks>
internal sealed class CbdtTable
{
    private const int BitmapSizeLength = 48;

    private readonly ReadOnlyMemory<byte> _cblc;
    private readonly ReadOnlyMemory<byte> _cbdt;
    private readonly Strike[] _strikes;

    private CbdtTable(ReadOnlyMemory<byte> cblc, ReadOnlyMemory<byte> cbdt, Strike[] strikes)
    {
        _cblc = cblc;
        _cbdt = cbdt;
        _strikes = strikes;
    }

    /// <summary>The usable strikes (those with a non-zero ppem), in table order.</summary>
    public int StrikeCount => _strikes.Length;

    /// <summary>A strike's horizontal pixels per em.</summary>
    public int PpemX(int strike) => _strikes[strike].PpemX;

    /// <summary>A strike's vertical pixels per em.</summary>
    public int PpemY(int strike) => _strikes[strike].PpemY;

    /// <summary>Reads the table headers and strike records.</summary>
    /// <exception cref="FontFormatException">If a header or strike record lies outside its table.</exception>
    public static CbdtTable Parse(ReadOnlyMemory<byte> cblc, ReadOnlyMemory<byte> cbdt)
    {
        var data = cblc.Span;
        if (data.Length < 8 || cbdt.Length < 4)
            throw new FontFormatException("The CBLC/CBDT tables are too short for their headers.");

        int major = BinaryPrimitives.ReadUInt16BigEndian(data);
        int dataMajor = BinaryPrimitives.ReadUInt16BigEndian(cbdt.Span);
        if (major is not (2 or 3) || dataMajor is not (2 or 3))
            throw new FontFormatException($"Unsupported CBLC/CBDT version {major}/{dataMajor}.");

        uint numSizes = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (numSizes > (uint)((data.Length - 8) / BitmapSizeLength))
            throw new FontFormatException($"CBLC declares {numSizes} strikes, more than the table holds.");

        var strikes = new List<Strike>((int)numSizes);
        for (int i = 0; i < (int)numSizes; i++)
        {
            var record = data.Slice(8 + (i * BitmapSizeLength), BitmapSizeLength);
            uint listOffset = BinaryPrimitives.ReadUInt32BigEndian(record);
            uint subtableCount = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            if (listOffset > (uint)data.Length || subtableCount > (uint)((data.Length - (int)listOffset) / 8))
                throw new FontFormatException($"CBLC strike {i}'s index subtable list lies outside the table.");

            int ppemX = record[44];
            int ppemY = record[45];
            if (ppemX == 0 || ppemY == 0)
                continue;

            strikes.Add(new Strike((int)listOffset, (int)subtableCount, ppemX, ppemY));
        }

        return new CbdtTable(cblc, cbdt, [.. strikes]);
    }

    /// <summary>Finds a glyph's PNG in one strike.</summary>
    /// <returns>False when the strike has no usable PNG for the glyph.</returns>
    public bool TryLocate(int strike, int glyphIndex, out CbdtBitmap bitmap)
    {
        bitmap = default;
        var s = _strikes[strike];
        var cblc = _cblc.Span;

        for (int i = 0; i < s.SubtableCount; i++)
        {
            int record = s.ListOffset + (i * 8);
            int first = BinaryPrimitives.ReadUInt16BigEndian(cblc[record..]);
            int last = BinaryPrimitives.ReadUInt16BigEndian(cblc[(record + 2)..]);
            if (glyphIndex < first || glyphIndex > last)
                continue;

            long subtable = (long)s.ListOffset + BinaryPrimitives.ReadUInt32BigEndian(cblc[(record + 4)..]);
            if (TryLocateInSubtable(cblc, subtable, glyphIndex, first, last, out bitmap))
                return true;
        }

        return false;
    }

    private bool TryLocateInSubtable(ReadOnlySpan<byte> cblc, long subtable, int glyph, int first, int last, out CbdtBitmap bitmap)
    {
        bitmap = default;
        if (!Fits(cblc, subtable, 8))
            return false;

        int at = (int)subtable;
        int indexFormat = BinaryPrimitives.ReadUInt16BigEndian(cblc[at..]);
        int imageFormat = BinaryPrimitives.ReadUInt16BigEndian(cblc[(at + 2)..]);
        long imageDataOffset = BinaryPrimitives.ReadUInt32BigEndian(cblc[(at + 4)..]);
        long start, end;
        ReadOnlySpan<byte> sharedMetrics = default; // the BigGlyphMetrics of formats 2 and 5

        switch (indexFormat)
        {
            case 1:
            case 3:
            {
                int size = indexFormat == 1 ? 4 : 2;
                long entry = at + 8L + ((long)(glyph - first) * size);
                if (!Fits(cblc, entry, size * 2))
                    return false;
                int e = (int)entry;
                long o0 = size == 4 ? BinaryPrimitives.ReadUInt32BigEndian(cblc[e..]) : BinaryPrimitives.ReadUInt16BigEndian(cblc[e..]);
                long o1 = size == 4 ? BinaryPrimitives.ReadUInt32BigEndian(cblc[(e + 4)..]) : BinaryPrimitives.ReadUInt16BigEndian(cblc[(e + 2)..]);
                start = imageDataOffset + o0;
                end = imageDataOffset + o1;
                break;
            }

            case 2:
            {
                if (!Fits(cblc, at, 20))
                    return false;
                long imageSize = BinaryPrimitives.ReadUInt32BigEndian(cblc[(at + 8)..]);
                sharedMetrics = cblc.Slice(at + 12, 8);
                start = imageDataOffset + (imageSize * (glyph - first));
                end = start + imageSize;
                break;
            }

            case 4:
            {
                if (!Fits(cblc, at, 12))
                    return false;
                long count = BinaryPrimitives.ReadUInt32BigEndian(cblc[(at + 8)..]);
                if (!Fits(cblc, at + 12L, (count + 1) * 4))
                    return false;
                int pairs = at + 12;
                int found = -1;
                for (int i = 0; i < count; i++)
                {
                    if (BinaryPrimitives.ReadUInt16BigEndian(cblc[(pairs + (i * 4))..]) == glyph)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                    return false;
                start = imageDataOffset + BinaryPrimitives.ReadUInt16BigEndian(cblc[(pairs + (found * 4) + 2)..]);
                end = imageDataOffset + BinaryPrimitives.ReadUInt16BigEndian(cblc[(pairs + (found * 4) + 6)..]);
                break;
            }

            case 5:
            {
                if (!Fits(cblc, at, 24))
                    return false;
                long imageSize = BinaryPrimitives.ReadUInt32BigEndian(cblc[(at + 8)..]);
                sharedMetrics = cblc.Slice(at + 12, 8);
                long count = BinaryPrimitives.ReadUInt32BigEndian(cblc[(at + 20)..]);
                if (!Fits(cblc, at + 24L, count * 2))
                    return false;
                int found = -1;
                for (int i = 0; i < count; i++)
                {
                    if (BinaryPrimitives.ReadUInt16BigEndian(cblc[(at + 24 + (i * 2))..]) == glyph)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                    return false;
                start = imageDataOffset + (imageSize * found);
                end = start + imageSize;
                break;
            }

            default:
                return false;
        }

        var cbdt = _cbdt.Span;
        if (end <= start || start < 0 || end > cbdt.Length)
            return false;

        return TryReadImage(imageFormat, (int)start, (int)(end - start), sharedMetrics, out bitmap);
    }

    /// <summary>Reads a glyph's image record: formats 17 (small metrics), 18 (big) and 19 (metrics in CBLC).</summary>
    private bool TryReadImage(int imageFormat, int start, int length, ReadOnlySpan<byte> sharedMetrics, out CbdtBitmap bitmap)
    {
        bitmap = default;
        var record = _cbdt.Span.Slice(start, length);
        ReadOnlySpan<byte> metrics;
        int header;
        switch (imageFormat)
        {
            case 17:
                metrics = record.Length >= 5 ? record[..5] : default;
                header = 5;
                break;
            case 18:
                metrics = record.Length >= 8 ? record[..8] : default;
                header = 8;
                break;
            case 19:
                metrics = sharedMetrics;
                header = 0;
                break;
            default:
                return false;
        }

        if (metrics.Length < 5 || record.Length < header + 4)
            return false;

        uint dataLength = BinaryPrimitives.ReadUInt32BigEndian(record[header..]);
        if (dataLength == 0 || dataLength > (uint)(record.Length - header - 4))
            return false;

        // Small and big metrics share their first four fields: height, width, (hori)BearingX, (hori)BearingY.
        bitmap = new CbdtBitmap(
            _cbdt.Slice(start + header + 4, (int)dataLength),
            Width: metrics[1],
            Height: metrics[0],
            BearingX: (sbyte)metrics[2],
            BearingY: (sbyte)metrics[3]);
        return true;
    }

    private static bool Fits(ReadOnlySpan<byte> table, long offset, long length) =>
        offset >= 0 && length >= 0 && offset + length <= table.Length;

    private readonly record struct Strike(int ListOffset, int SubtableCount, int PpemX, int PpemY);
}
