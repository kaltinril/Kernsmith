using KernSmith.Output;
using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests.Output;

public sealed class TextFormatterTests
{
    private readonly TextFormatter _formatter = new();

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
    public void FormatText_StartsWithInfoLine()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldStartWith("info face=");
    }

    [Fact]
    public void FormatText_ContainsFaceName()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("face=\"TestFont\"");
    }

    [Fact]
    public void FormatText_BoolsAreZeroOrOne()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("bold=0");
        output.ShouldNotContain("bold=false");
        output.ShouldContain("unicode=1");
        output.ShouldNotContain("unicode=true");
    }

    [Fact]
    public void FormatText_PaddingFormat()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("padding=0,0,0,0");
    }

    [Fact]
    public void FormatText_SpacingFormat()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("spacing=1,1");
    }

    [Fact]
    public void FormatText_ContainsCharsCount()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("chars count=1");
    }

    [Fact]
    public void FormatText_ContainsKerningsCount()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("kernings count=1");
    }

    [Fact]
    public void FormatText_CharLineFormat()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("id=65");
        output.ShouldContain("x=10");
        output.ShouldContain("xadvance=18");
    }

    [Fact]
    public void FormatText_KerningLineFormat()
    {
        // Arrange
        var model = CreateTestModel();

        // Act
        var output = _formatter.FormatText(model);

        // Assert
        output.ShouldContain("first=65 second=86 amount=-2");
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
        output.ShouldNotContain("kernings");
    }
}
