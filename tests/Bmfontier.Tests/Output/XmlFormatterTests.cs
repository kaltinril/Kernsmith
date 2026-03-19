using Bmfontier.Output;
using Bmfontier.Output.Model;
using FluentAssertions;

namespace Bmfontier.Tests.Output;

public sealed class XmlFormatterTests
{
    private readonly XmlFormatter _formatter = new();

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
    public void FormatText_ContainsXmlDeclaration()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.Should().StartWith("<?xml");
    }

    [Fact]
    public void FormatText_ContainsFontRoot()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.Should().Contain("<font>");
    }

    [Fact]
    public void FormatText_ContainsFaceName()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.Should().Contain("face=\"TestFont\"");
    }

    [Fact]
    public void FormatText_ContainsCharElement()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.Should().Contain("<char ");
        output.Should().Contain("id=\"65\"");
    }

    [Fact]
    public void FormatText_BoolsAreZeroOne()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.Should().Contain("bold=\"0\"");
        output.Should().NotContain("bold=\"false\"", "booleans should be formatted as 0 or 1");
        output.Should().NotContain("bold=\"False\"", "booleans should be formatted as 0 or 1");
    }

    [Fact]
    public void FormatText_NoKernings_OmitsSection()
    {
        // Arrange
        var model = new BmFontModel
        {
            Info = CreateTestModel().Info,
            Common = CreateTestModel().Common,
            Pages = new[] { new PageEntry(0, "TestFont_0.png") },
            Characters = new[] { new CharEntry(65, 10, 20, 16, 24, 1, -2, 18, 0, 15) },
            KerningPairs = Array.Empty<KerningEntry>()
        };

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.Should().NotContain("<kernings");
    }
}
