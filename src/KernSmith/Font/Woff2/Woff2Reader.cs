using System.Buffers.Binary;

namespace KernSmith.Font.Woff2;

/// <summary>
/// Bounds-checked big-endian cursor over one WOFF2 region (header, directory or a transform
/// substream). Every overrun throws <see cref="FontParsingException"/> naming the region.
/// </summary>
internal ref struct Woff2Reader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly string _region;

    public Woff2Reader(ReadOnlySpan<byte> data, string region)
    {
        _data = data;
        _region = region;
    }

    public int Position { get; private set; }

    public readonly int Remaining => _data.Length - Position;

    public byte ReadByte()
    {
        Require(1);
        return _data[Position++];
    }

    public ushort ReadUInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(Position));
        Position += 2;
        return value;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(Position));
        Position += 4;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        var slice = _data.Slice(Position, count);
        Position += count;
        return slice;
    }

    /// <summary>
    /// UIntBase128: 1-5 bytes, 7 bits each, high bit = continuation. Leading zero bytes and
    /// values that overflow 32 bits are invalid.
    /// </summary>
    public uint ReadUIntBase128()
    {
        uint value = 0;
        for (var i = 0; i < 5; i++)
        {
            var b = ReadByte();
            if (i == 0 && b == 0x80)
                throw Woff2Decoder.Invalid($"UIntBase128 in the {_region} has a leading zero byte");
            if ((value & 0xFE000000) != 0)
                throw Woff2Decoder.Invalid($"UIntBase128 in the {_region} overflows 32 bits");

            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return value;
        }

        throw Woff2Decoder.Invalid($"UIntBase128 in the {_region} is longer than 5 bytes");
    }

    /// <summary>255UInt16: one byte below 253, or a 253/254/255 escape and 1-2 more bytes.</summary>
    public ushort Read255UInt16()
    {
        const byte WordCode = 253;
        const byte OneMoreByteCode2 = 254;
        const byte OneMoreByteCode1 = 255;
        const int LowestUCode = 253;

        var code = ReadByte();
        return code switch
        {
            WordCode => ReadUInt16(),
            OneMoreByteCode1 => (ushort)(ReadByte() + LowestUCode),
            OneMoreByteCode2 => (ushort)(ReadByte() + LowestUCode * 2),
            _ => code,
        };
    }

    private readonly void Require(int count)
    {
        if (count < 0 || count > _data.Length - Position)
            throw Woff2Decoder.Invalid($"the {_region} is truncated");
    }
}
