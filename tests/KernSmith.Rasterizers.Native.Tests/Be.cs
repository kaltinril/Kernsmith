using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Big-endian byte builders for hand-assembled font tables.</summary>
internal static class Be
{
    public static byte[] U8(byte value) => [value];

    public static byte[] U16(ushort value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        return buffer;
    }

    public static byte[] I16(short value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        return buffer;
    }

    public static byte[] U32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }

    public static byte[] I32(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return buffer;
    }

    public static byte[] Tag(string tag) => [(byte)tag[0], (byte)tag[1], (byte)tag[2], (byte)tag[3]];

    /// <summary>A 2.14 fixed-point value, rounded to the nearest representable step.</summary>
    public static byte[] F2Dot14(double value) => I16((short)Math.Round(value * 16384));

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
