using KernSmith.Ui.Services;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// <c>--ui-rasterizer &lt;backend&gt;</c> picks the backend the app's own Gum text is rendered
/// with (through the Gum integration's KernSmithFontCreator), so a Gum + MonoGame consumer of
/// the Native backend can be tried from this repository.
/// </summary>
public sealed class UiFontRasterizerTests
{
    [Fact]
    public void NoArgument_KeepsTheThemeDefault()
    {
        UiFontRasterizer.Parse([]).ShouldBeNull();
    }

    [Theory]
    [InlineData("native", RasterizerBackend.Native)]
    [InlineData("Native", RasterizerBackend.Native)]
    [InlineData("stbtruetype", RasterizerBackend.StbTrueType)]
    public void Argument_SelectsTheBackend(string value, RasterizerBackend expected)
    {
        UiFontRasterizer.Parse(["--ui-rasterizer", value]).ShouldBe(expected);
    }

    [Fact]
    public void UnknownBackend_IsRejected()
    {
        var ex = Should.Throw<ArgumentException>(() => UiFontRasterizer.Parse(["--ui-rasterizer", "bogus"]));
        ex.Message.ShouldContain("bogus", Case.Sensitive);
    }

    [Fact]
    public void MissingValue_IsRejected()
    {
        Should.Throw<ArgumentException>(() => UiFontRasterizer.Parse(["--ui-rasterizer"]));
    }
}
