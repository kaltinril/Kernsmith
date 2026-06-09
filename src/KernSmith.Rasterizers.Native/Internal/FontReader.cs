using System.Buffers.Binary;
using System.Text;

namespace KernSmith.Rasterizers.Native.Internal;

/// <summary>
/// A zero-allocation, big-endian binary reader over a font's raw bytes.
/// SFNT (TrueType/OpenType) data is stored big-endian; all multi-byte reads use
/// <see cref="BinaryPrimitives"/> big-endian helpers. The reader tracks a cursor
/// position and bounds-checks every read, throwing <see cref="FontFormatException"/>
/// when a read would run past the end of the data.
/// </summary>
internal ref struct FontReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    /// <summary>Creates a reader positioned at the start of the supplied span.</summary>
    public FontReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>The current read cursor, in bytes from the start of the data.</summary>
    public readonly int Position => _position;

    /// <summary>Total length of the underlying data, in bytes.</summary>
    public readonly int Length => _data.Length;

    /// <summary>Number of unread bytes remaining from the current position.</summary>
    public readonly int Remaining => _data.Length - _position;

    /// <summary>Moves the cursor to an absolute byte offset.</summary>
    /// <exception cref="FontFormatException">If the offset is out of range.</exception>
    public void Seek(int offset)
    {
        if ((uint)offset > (uint)_data.Length)
            throw new FontFormatException($"Seek to offset {offset} is outside the font data (length {_data.Length}).");
        _position = offset;
    }

    /// <summary>Advances the cursor by <paramref name="count"/> bytes.</summary>
    /// <exception cref="FontFormatException">If the resulting position is out of range.</exception>
    public void Skip(int count)
    {
        int newPosition = _position + count;
        if (count < 0 || newPosition > _data.Length)
            throw new FontFormatException($"Skip of {count} bytes from offset {_position} is outside the font data (length {_data.Length}).");
        _position = newPosition;
    }

    /// <summary>Reads an unsigned 8-bit integer and advances the cursor.</summary>
    public byte ReadUInt8()
    {
        EnsureAvailable(1);
        byte value = _data[_position];
        _position += 1;
        return value;
    }

    /// <summary>Reads a signed 8-bit integer and advances the cursor.</summary>
    public sbyte ReadInt8() => unchecked((sbyte)ReadUInt8());

    /// <summary>Reads a big-endian unsigned 16-bit integer and advances the cursor.</summary>
    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>Reads a big-endian signed 16-bit integer and advances the cursor.</summary>
    public short ReadInt16()
    {
        EnsureAvailable(2);
        short value = BinaryPrimitives.ReadInt16BigEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>Reads a big-endian unsigned 32-bit integer and advances the cursor.</summary>
    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>Reads a big-endian signed 32-bit integer and advances the cursor.</summary>
    public int ReadInt32()
    {
        EnsureAvailable(4);
        int value = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>
    /// Reads a 16.16 fixed-point value and advances the cursor, returning it as a float.
    /// </summary>
    public float ReadFixed() => ReadInt32() / 65536.0f;

    /// <summary>Reads an FWORD (signed 16-bit value in font design units).</summary>
    public short ReadFWord() => ReadInt16();

    /// <summary>Reads a UFWORD (unsigned 16-bit value in font design units).</summary>
    public ushort ReadUFWord() => ReadUInt16();

    /// <summary>
    /// Reads an F2Dot14 (2.14 signed fixed-point) value and advances the cursor.
    /// Used by composite glyph transform components.
    /// </summary>
    public float ReadF2Dot14() => ReadInt16() / 16384.0f;

    /// <summary>
    /// Reads a 4-byte ASCII tag (e.g., "head", "cmap") and advances the cursor.
    /// Non-printable bytes are preserved verbatim.
    /// </summary>
    public string ReadTag()
    {
        EnsureAvailable(4);
        string tag = Encoding.ASCII.GetString(_data.Slice(_position, 4));
        _position += 4;
        return tag;
    }

    /// <summary>
    /// Returns a view over <paramref name="count"/> bytes and advances the cursor.
    /// The returned span aliases the underlying data; no copy is made.
    /// </summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0)
            throw new FontFormatException($"Cannot read a negative number of bytes ({count}).");
        EnsureAvailable(count);
        ReadOnlySpan<byte> slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    private readonly void EnsureAvailable(int count)
    {
        if (_position + count > _data.Length)
            throw new FontFormatException(
                $"Attempted to read {count} byte(s) at offset {_position}, but only {_data.Length - _position} remain.");
    }
}
