using System.Text;
using Bmfontier.Output;
using Bmfontier.Output.Model;
using FluentAssertions;

namespace Bmfontier.Tests.Output;

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
        output[0].Should().Be(66, "first byte should be 'B'");
        output[1].Should().Be(77, "second byte should be 'M'");
        output[2].Should().Be(70, "third byte should be 'F'");
        output[3].Should().Be(3, "fourth byte should be version 3");
    }

    [Fact]
    public void FormatBinary_HasBlock1()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatBinary(model);

        // Assert
        output[4].Should().Be(1, "byte at index 4 should be block type 1 (info)");
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
        outputString.Should().Contain("TestFont", "binary output should contain the font name as ASCII bytes");
    }

    [Fact]
    public void FormatBinary_NonEmpty()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatBinary(model);

        // Assert
        output.Length.Should().BeGreaterThan(20, "binary output should contain header, info, common, pages, and chars blocks");
    }
}
