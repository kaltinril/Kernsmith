using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Internal.Bitmaps;

/// <summary>A PNG glyph image located in an <c>sbix</c> strike.</summary>
/// <param name="Png">The PNG bytes.</param>
/// <param name="OriginX">The bitmap's left edge relative to the glyph's design-space origin, in strike pixels.</param>
/// <param name="OriginY">The bitmap's bottom edge relative to that origin, in strike pixels (y up).</param>
internal readonly record struct SbixBitmap(ReadOnlyMemory<byte> Png, int OriginX, int OriginY);

/// <summary>
/// The <c>sbix</c> standard bitmap graphics table: strikes of per-glyph graphics with origin
/// offsets.
/// </summary>
/// <remarks>
/// Only <c>'png '</c> graphics are drawn. <c>'dupe'</c> records are followed (up to
/// <see cref="MaxDupeHops"/> hops, so a cycle ends); <c>'jpg '</c>, <c>'tiff'</c>, Apple's
/// <c>'pdf '</c>/<c>'mask'</c> and unknown types, zero-length records and records that are
/// malformed or out of range all mean "no bitmap for this glyph in this strike". A strike's
/// <c>ppi</c> is not used: strikes are chosen by <c>ppem</c> alone. The header's "draw outlines"
/// flag (bit 1) is optional in the spec and is not honored.
/// </remarks>
internal sealed class SbixTable
{
    /// <summary>The longest chain of <c>'dupe'</c> records followed before giving up.</summary>
    public const int MaxDupeHops = 4;

    private const uint TagPng = 0x706E6720;  // 'png '
    private const uint TagDupe = 0x64757065; // 'dupe'

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _glyphCount;
    private readonly Strike[] _strikes;

    private SbixTable(ReadOnlyMemory<byte> data, int glyphCount, Strike[] strikes)
    {
        _data = data;
        _glyphCount = glyphCount;
        _strikes = strikes;
    }

    /// <summary>The usable strikes (non-zero ppem), in table order.</summary>
    public int StrikeCount => _strikes.Length;

    /// <summary>A strike's pixels per em.</summary>
    public int Ppem(int strike) => _strikes[strike].Ppem;

    /// <summary>Reads the header and strike headers.</summary>
    /// <param name="data">The table.</param>
    /// <param name="glyphCount">The face's <c>maxp.numGlyphs</c>, which sizes every strike's offset array.</param>
    /// <exception cref="FontFormatException">If the header or a strike's offset array lies outside the table.</exception>
    public static SbixTable Parse(ReadOnlyMemory<byte> data, int glyphCount)
    {
        var span = data.Span;
        if (span.Length < 8)
            throw new FontFormatException("The sbix table is too short for its header.");

        uint numStrikes = BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
        if (numStrikes > (uint)((span.Length - 8) / 4))
            throw new FontFormatException($"sbix declares {numStrikes} strikes, more than the table holds.");

        long offsetsLength = 4L * (glyphCount + 1);
        var strikes = new List<Strike>((int)numStrikes);
        for (int i = 0; i < (int)numStrikes; i++)
        {
            long offset = BinaryPrimitives.ReadUInt32BigEndian(span[(8 + (i * 4))..]);
            if (offset + 4 + offsetsLength > span.Length)
                throw new FontFormatException($"sbix strike {i} lies outside the table.");

            int ppem = BinaryPrimitives.ReadUInt16BigEndian(span[(int)offset..]);
            if (ppem != 0)
                strikes.Add(new Strike((int)offset, ppem));
        }

        return new SbixTable(data, glyphCount, [.. strikes]);
    }

    /// <summary>Finds a glyph's PNG in one strike, following <c>'dupe'</c> records.</summary>
    /// <returns>False when the strike has no usable PNG for the glyph.</returns>
    public bool TryLocate(int strike, int glyphIndex, out SbixBitmap bitmap)
    {
        bitmap = default;
        var span = _data.Span;
        int strikeStart = _strikes[strike].Offset;
        int glyph = glyphIndex;

        for (int hop = 0; hop <= MaxDupeHops; hop++)
        {
            if ((uint)glyph >= (uint)_glyphCount)
                return false;

            int entry = strikeStart + 4 + (glyph * 4);
            long start = strikeStart + (long)BinaryPrimitives.ReadUInt32BigEndian(span[entry..]);
            long end = strikeStart + (long)BinaryPrimitives.ReadUInt32BigEndian(span[(entry + 4)..]);
            if (end - start < 8 || end > span.Length)
                return false;

            int at = (int)start;
            int length = (int)(end - start);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(span[(at + 4)..]);
            if (type == TagDupe)
            {
                if (length < 10)
                    return false;
                glyph = BinaryPrimitives.ReadUInt16BigEndian(span[(at + 8)..]);
                continue;
            }

            if (type != TagPng)
                return false;

            bitmap = new SbixBitmap(
                _data.Slice(at + 8, length - 8),
                OriginX: BinaryPrimitives.ReadInt16BigEndian(span[at..]),
                OriginY: BinaryPrimitives.ReadInt16BigEndian(span[(at + 2)..]));
            return bitmap.Png.Length > 0;
        }

        return false;
    }

    private readonly record struct Strike(int Offset, int Ppem);
}
