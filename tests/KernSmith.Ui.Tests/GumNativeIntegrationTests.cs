using KernSmith.Gum;
using RenderingLibrary.Graphics.Fonts;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The published Gum integration (KernSmith.GumCommon, consumed here through the UI's
/// Gum.Themes.Editor.MonoGame dependency) is compiled against a released KernSmith, but at run
/// time it binds to this repository's KernSmith and can drive the unpublished Native backend.
/// This is the path <c>KernSmithFontCreator(graphicsDevice, RasterizerBackend.Native)</c> and
/// the UI's <c>--ui-rasterizer native</c> take.
/// </summary>
public sealed class GumNativeIntegrationTests : IDisposable
{
    private const string Family = "KernSmithUiNativeGumTest";

    public void Dispose() => BmFont.UnregisterFont(Family);

    [Fact]
    public void GumFontGenerator_WithNativeBackend_GeneratesTheFont()
    {
        BmFont.RegisterFont(Family, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf")));

        var result = GumFontGenerator.Generate(new BmfcSave { FontName = Family, FontSize = 20 }, RasterizerBackend.Native);

        result.Pages.Count.ShouldBeGreaterThan(0);
        result.Model.Characters.ShouldContain(c => c.Id == 'A' && c.Width > 0);
    }
}
