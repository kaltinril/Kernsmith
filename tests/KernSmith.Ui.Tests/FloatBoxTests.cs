using Gum.Forms.Controls;
using KernSmith.Ui.Services;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The float boxes (Advance X, gradient Offset and Scale) must show the values a loaded project
/// applied, not the values typed before it.
/// </summary>
[Collection(HeadlessGum.Name)]
public sealed class FloatBoxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-float-tests").FullName;

    static FloatBoxTests() => HeadlessGum.EnsureInitialized();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    // The boxes the panels build, bound as FontConfigPanel and EffectsPanel bind them.
    private static TextBox Box(EffectsViewModel effects, string property) => property switch
    {
        nameof(EffectsViewModel.AdvanceAdjustX) => UiFactory.CreateSmallFloatBox(effects, property,
            () => effects.AdvanceAdjustX, v => effects.AdvanceAdjustX = v),
        nameof(EffectsViewModel.GradientOffset) => UiFactory.CreateSmallFloatBox(effects, property,
            () => effects.GradientOffset, v => effects.GradientOffset = v),
        nameof(EffectsViewModel.GradientScale) => UiFactory.CreateSmallFloatBox(effects, property,
            () => effects.GradientScale, v => effects.GradientScale = v),
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    [Theory]
    [InlineData(nameof(EffectsViewModel.AdvanceAdjustX), "advanceAdjustX=2.5", 2.5f)]
    [InlineData(nameof(EffectsViewModel.GradientOffset), "gradientTop=FF0000\ngradientBottom=0000FF\ngradientOffset=0.25", 0.25f)]
    [InlineData(nameof(EffectsViewModel.GradientScale), "gradientTop=FF0000\ngradientBottom=0000FF\ngradientScale=1.5", 1.5f)]
    public void LoadProject_ShowsTheProjectsValueInTheBox(string property, string keys, float expected)
    {
        var effects = new EffectsViewModel();
        var box = Box(effects, property);
        box.Text = "7";

        var path = Path.Combine(_dir, "project.bmfc");
        File.WriteAllText(path, "fileVersion=1\nfontName=Arial\nfontSize=32\nchars=32-126\n" + keys + "\n");
        new ProjectService().LoadProject(path, new FontConfigViewModel(), new AtlasConfigViewModel(),
            effects, new CharacterGridViewModel());

        typeof(EffectsViewModel).GetProperty(property)!.GetValue(effects).ShouldBe(expected);
        box.Text.ShouldBe(expected.ToString("0.0###"));
    }

    [Fact]
    public void Box_StartsAtTheViewModelsValue()
    {
        var effects = new EffectsViewModel { GradientScale = 2f };

        Box(effects, nameof(EffectsViewModel.GradientScale)).Text.ShouldBe(2f.ToString("0.0###"));
    }

    [Theory]
    [InlineData("0.50")]
    [InlineData("1.")]
    public void TypingAValue_SetsTheViewModelWithoutRewritingTheBox(string typed)
    {
        var effects = new EffectsViewModel();
        var box = Box(effects, nameof(EffectsViewModel.AdvanceAdjustX));

        box.Text = typed;

        effects.AdvanceAdjustX.ShouldBe(float.Parse(typed, System.Globalization.CultureInfo.CurrentCulture));
        box.Text.ShouldBe(typed);
    }
}
