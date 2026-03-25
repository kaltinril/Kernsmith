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
        var fontNameBytes = Encoding.ASCII.GetBytes("TestFont");
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
}
