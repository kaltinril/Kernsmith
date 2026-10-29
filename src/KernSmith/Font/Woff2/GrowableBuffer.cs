using System.Buffers.Binary;

namespace KernSmith.Font.Woff2;

/// <summary>Append-only big-endian byte buffer used while rebuilding WOFF2 tables.</summary>
internal sealed class GrowableBuffer
{
    private byte[] _buffer;

    public GrowableBuffer(int initialCapacity)
    {
        _buffer = new byte[Math.Max(16, initialCapacity)];
    }

    public int Length { get; private set; }

    public void WriteByte(byte value)
    {
        Ensure(1);
        _buffer[Length++] = value;
    }

    public void WriteUInt16(ushort value)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(Length), value);
        Length += 2;
    }

    public void WriteInt16(short value) => WriteUInt16((ushort)value);

    public void Write(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buffer.AsSpan(Length));
        Length += data.Length;
    }

    /// <summary>Overwrites the most recently written byte.</summary>
    public void SetLastByte(byte value) => _buffer[Length - 1] = value;

    /// <summary>Zero-pads to the next 4-byte boundary.</summary>
    public void PadTo4()
    {
        var padding = (4 - (Length & 3)) & 3;
        Ensure(padding);
        _buffer.AsSpan(Length, padding).Clear();
        Length += padding;
    }

    public byte[] ToArray() => _buffer.AsSpan(0, Length).ToArray();

    private void Ensure(int count)
    {
        if (count <= _buffer.Length - Length)
            return;

        var size = Math.Max(Length + (long)count, _buffer.Length * 2L);
        if (size > Array.MaxLength)
            throw Woff2Decoder.Invalid("reconstructed table is too large");
        Array.Resize(ref _buffer, (int)size);
    }
}
