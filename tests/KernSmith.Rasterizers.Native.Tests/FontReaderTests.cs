using KernSmith.Rasterizers.Native.Internal;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

public class FontReaderTests
{
    [Fact]
    public void ReadUInt8_ReturnsByteAndAdvances()
    {
        ReadOnlySpan<byte> data = [0x12, 0x34];
        var reader = new FontReader(data);

        reader.ReadUInt8().ShouldBe((byte)0x12);
        reader.Position.ShouldBe(1);
        reader.ReadUInt8().ShouldBe((byte)0x34);
        reader.Position.ShouldBe(2);
    }

    [Fact]
    public void ReadInt8_HandlesNegative()
    {
        ReadOnlySpan<byte> data = [0xFF];
        var reader = new FontReader(data);
        reader.ReadInt8().ShouldBe((sbyte)-1);
    }

    [Fact]
    public void ReadUInt16_IsBigEndian()
    {
        ReadOnlySpan<byte> data = [0x12, 0x34];
        var reader = new FontReader(data);
        reader.ReadUInt16().ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void ReadInt16_HandlesNegative()
    {
        ReadOnlySpan<byte> data = [0xFF, 0xFE];
        var reader = new FontReader(data);
        reader.ReadInt16().ShouldBe((short)-2);
    }

    [Fact]
    public void ReadUInt32_IsBigEndian()
    {
        ReadOnlySpan<byte> data = [0x12, 0x34, 0x56, 0x78];
        var reader = new FontReader(data);
        reader.ReadUInt32().ShouldBe(0x12345678u);
    }

    [Fact]
    public void ReadInt32_HandlesNegative()
    {
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF];
        var reader = new FontReader(data);
        reader.ReadInt32().ShouldBe(-1);
    }

    [Fact]
    public void ReadFixed_DecodesSixteenDotSixteen()
    {
        // 1.5 in 16.16 fixed point = 0x00018000
        ReadOnlySpan<byte> data = [0x00, 0x01, 0x80, 0x00];
        var reader = new FontReader(data);
        reader.ReadFixed().ShouldBe(1.5f);
    }

    [Fact]
    public void ReadF2Dot14_DecodesTwoDotFourteen()
    {
        // 1.0 in 2.14 = 0x4000; -1.0 = 0xC000 (-16384/16384)
        ReadOnlySpan<byte> data = [0x40, 0x00, 0xC0, 0x00];
        var reader = new FontReader(data);
        reader.ReadF2Dot14().ShouldBe(1.0f);
        reader.ReadF2Dot14().ShouldBe(-1.0f);
    }

    [Fact]
    public void ReadFWord_And_ReadUFWord_RoundTrip()
    {
        ReadOnlySpan<byte> data = [0xFF, 0x9C, 0x03, 0xE8];
        var reader = new FontReader(data);
        reader.ReadFWord().ShouldBe((short)-100);
        reader.ReadUFWord().ShouldBe((ushort)1000);
    }

    [Fact]
    public void ReadTag_ReturnsAsciiString()
    {
        ReadOnlySpan<byte> data = [(byte)'h', (byte)'e', (byte)'a', (byte)'d'];
        var reader = new FontReader(data);
        reader.ReadTag().ShouldBe("head");
    }

    [Fact]
    public void ReadBytes_ReturnsSliceAndAdvances()
    {
        ReadOnlySpan<byte> data = [0x01, 0x02, 0x03, 0x04];
        var reader = new FontReader(data);
        reader.Skip(1);
        var slice = reader.ReadBytes(2);
        slice.Length.ShouldBe(2);
        slice[0].ShouldBe((byte)0x02);
        slice[1].ShouldBe((byte)0x03);
        reader.Position.ShouldBe(3);
    }

    [Fact]
    public void Seek_MovesCursorToAbsoluteOffset()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0xAB];
        var reader = new FontReader(data);
        reader.Seek(2);
        reader.ReadUInt8().ShouldBe((byte)0xAB);
    }

    [Fact]
    public void RemainingAndLength_TrackState()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00];
        var reader = new FontReader(data);
        reader.Length.ShouldBe(4);
        reader.Remaining.ShouldBe(4);
        reader.Skip(3);
        reader.Remaining.ShouldBe(1);
    }

    [Fact]
    public void ReadPastEnd_ThrowsFontFormatException()
    {
        Should.Throw<FontFormatException>(() =>
        {
            ReadOnlySpan<byte> data = [0x01];
            var reader = new FontReader(data);
            reader.ReadUInt16();
        });
    }

    [Fact]
    public void SeekOutOfRange_ThrowsFontFormatException()
    {
        Should.Throw<FontFormatException>(() =>
        {
            ReadOnlySpan<byte> data = [0x01, 0x02];
            var reader = new FontReader(data);
            reader.Seek(5);
        });
    }
}
