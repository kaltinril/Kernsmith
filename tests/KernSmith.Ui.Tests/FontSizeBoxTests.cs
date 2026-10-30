using Gum.Forms;
using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using KernSmith.Ui.Layout;
using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The Size (pt) box must show the size a loaded project applied, not the size typed before it.
/// </summary>
public sealed class FontSizeBoxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-size-tests").FullName;

    static FontSizeBoxTests()
    {
        // No GumService here: give TextBox a visual that needs no graphics device.
        FrameworkElement.DefaultFormsTemplates[typeof(TextBox)] =
            new VisualTemplate(() => new TextBoxVisual(tryCreateFormsObject: false));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Fact]
    public void LoadProject_ShowsTheProjectsSizeInTheSizeBox()
    {
        var fontConfig = new FontConfigViewModel();
        var sizeBox = FontConfigPanel.CreateFontSizeBox(fontConfig);
        sizeBox.Text = "20";

        var path = Path.Combine(_dir, "project.bmfc");
        File.WriteAllText(path, "fileVersion=1\nfontName=Arial\nfontSize=48\nchars=32-126\n");
        new ProjectService().LoadProject(path, fontConfig, new AtlasConfigViewModel(),
            new EffectsViewModel(), new CharacterGridViewModel());

        fontConfig.FontSize.ShouldBe(48);
        sizeBox.Text.ShouldBe("48");
    }

    [Fact]
    public void SizeBox_StartsAtTheViewModelsSize()
    {
        var fontConfig = new FontConfigViewModel { FontSize = 72 };

        FontConfigPanel.CreateFontSizeBox(fontConfig).Text.ShouldBe("72");
    }

    [Fact]
    public void TypingASize_ClampsIntoTheViewModelWithoutRewritingTheBox()
    {
        var fontConfig = new FontConfigViewModel();
        var sizeBox = FontConfigPanel.CreateFontSizeBox(fontConfig);

        sizeBox.Text = "1000";

        fontConfig.FontSize.ShouldBe(500);
        sizeBox.Text.ShouldBe("1000");
    }
}
