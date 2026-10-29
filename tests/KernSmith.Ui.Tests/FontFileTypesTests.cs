using KernSmith.Ui.Services;
using Shouldly;

namespace KernSmith.Ui.Tests;

public sealed class FontFileTypesTests
{
    [Theory]
    [InlineData("font.ttf")]
    [InlineData("font.otf")]
    [InlineData("font.ttc")]
    [InlineData("font.woff")]
    [InlineData("font.woff2")]
    [InlineData(@"C:\fonts\Font.WOFF2")]
    public void IsFontFile_AcceptsEveryFormatCoreReads(string path)
    {
        FontFileTypes.IsFontFile(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData("project.bmfc")]
    [InlineData("font.png")]
    [InlineData("font.woff3")]
    [InlineData("noextension")]
    public void IsFontFile_RejectsOtherFiles(string path)
    {
        FontFileTypes.IsFontFile(path).ShouldBeFalse();
    }

    [Fact]
    public void DialogFilter_ListsEveryFontExtension()
    {
        FontFileTypes.DialogFilter.Split(',').ShouldBe(
            ["ttf", "otf", "ttc", "woff", "woff2"], ignoreOrder: true);
    }
}
