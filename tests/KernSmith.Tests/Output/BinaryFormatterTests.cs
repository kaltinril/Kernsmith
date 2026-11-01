using System.Text;
using KernSmith.Output;
using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests.Output;

public sealed class BinaryFormatterTests
{
    private readonly BmFontBinaryFormatter _formatter = new();

    private static BmFontModel CreateTestModel() => new()
    {
        Info = new InfoBlock(
            Face: "TestFont",
            Size: 32,
            Bold: false,
            Italic: false,
            Unicode: true,
            Smooth: true,
            FixedHeight: false,
            StretchH: 100,
            Charset: "",
            Aa: 1,
            Padding: new Padding(0, 0, 0, 0),
            Spacing: new Spacing(1, 1)),
        Common = new CommonBlock(
            LineHeight: 38,
            Base: 30,
            ScaleW: 256,
            ScaleH: 256,
            Pages: 1,
            Packed: false),
        Pages = new[] { new PageEntry(0, "TestFont_0.png") },
        Characters = new[] { new CharEntry(65, 10, 20, 16, 24, 1, -2, 18, 0, 15) },
        KerningPairs = new[] { new KerningEntry(65, 86, -2) }
    };

    [Fact]
    public void FormatBinary_StartsWithBMFHeader()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatBinary(model);

        // Assert
        output[0].ShouldBe((byte)66);
        output[1].ShouldBe((byte)77);
        output[2].ShouldBe((byte)70);
        output[3].ShouldBe((byte)3);
    }

    [Fact]
    public void FormatBinary_HasBlock1()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatBinary(model);

        // Assert
        output[4].ShouldBe((byte)1);
    }

    [Fact]
    public void FormatBinary_ContainsFontName()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatBinary(model);

        // Assert
        var outputString = Encoding.ASCII.GetString(output);
        outputString.ShouldContain("TestFont");
    }

    [Fact]
    public void FormatBinary_NonEmpty()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatBinary(model);

        // Assert
        output.Length.ShouldBeGreaterThan(20);
    }

    /// <summary>The payload of the first block of <paramref name="type"/> in a binary .fnt.</summary>
    private static byte[] Block(byte[] fnt, byte type)
    {
        var pos = 4;
        while (pos < fnt.Length)
        {
            var size = BitConverter.ToInt32(fnt, pos + 1);
            if (fnt[pos] == type)
                return fnt[(pos + 5)..(pos + 5 + size)];
            pos += 5 + size;
        }
        throw new InvalidOperationException($"No block {type}.");
    }

    // BMFont numbers bitField bits from the most significant end: info's bit 0 (smooth) is 0x80,
    // and common's bit 7 (packed) is 0x01. BMFont64 with fourChnlPacked=1 writes common 0x01 and
    // info 0xC0 (smooth + unicode).
    [Theory]
    [InlineData(true, 0x01)]
    [InlineData(false, 0x00)]
    public void FormatBinary_CommonPackedBit_MatchesBmFont(bool packed, byte expected)
    {
        var model = CreateTestModel();
        model = new BmFontModel
        {
            Info = model.Info,
            Common = model.Common with { Packed = packed },
            Pages = model.Pages,
            Characters = model.Characters,
            KerningPairs = model.KerningPairs,
        };

        var bytes = _formatter.FormatBinary(model);

        Block(bytes, 2)[10].ShouldBe(expected);
        Block(bytes, 1)[2].ShouldBe((byte)0xC0);
        BmFontReader.Read(bytes).Common.Packed.ShouldBe(packed);
    }

    [Fact]
    public void Read_CommonPackedHighBit_IsStillPacked()
    {
        // Binary files written before the packed bit was corrected carry it as 0x80.
        var bytes = _formatter.FormatBinary(CreateTestModel());
        var pos = 4;
        while (bytes[pos] != 2)
            pos += 5 + BitConverter.ToInt32(bytes, pos + 1);
        bytes[pos + 5 + 10] = 0x80;

        BmFontReader.Read(bytes).Common.Packed.ShouldBeTrue();
    }
}
