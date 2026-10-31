using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// Settings that have a control in the UI must survive Save Project then Open Project.
/// </summary>
public sealed class ProjectSaveLoadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static (FontConfigViewModel, AtlasConfigViewModel, EffectsViewModel, CharacterGridViewModel) NewViewModels()
        => (new FontConfigViewModel(), new AtlasConfigViewModel(), new EffectsViewModel(), new CharacterGridViewModel());

    private (AtlasConfigViewModel Atlas, EffectsViewModel Effects) SaveAndReload(
        Action<AtlasConfigViewModel, EffectsViewModel> configure)
    {
        var service = new ProjectService();
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        characterGrid.SelectAll(new[] { 'A', 'B' }.Select(c => (int)c));
        configure(atlasConfig, effects);

        var path = Path.Combine(_dir, "project.bmfc");
        service.SaveProject(path, fontConfig, atlasConfig, effects, characterGrid);

        var (fontConfig2, atlasConfig2, effects2, characterGrid2) = NewViewModels();
        new ProjectService().LoadProject(path, fontConfig2, atlasConfig2, effects2, characterGrid2);
        return (atlasConfig2, effects2);
    }

    [Fact]
    public void SaveThenLoad_KeepsThePackingAlgorithm()
    {
        var (atlas, _) = SaveAndReload((a, _) => a.PackingAlgorithmIndex = (int)PackingAlgorithm.Skyline);

        atlas.PackingAlgorithmIndex.ShouldBe((int)PackingAlgorithm.Skyline);
    }

    [Fact]
    public void SaveThenLoad_KeepsTheFallbackCharacter()
    {
        var (_, effects) = SaveAndReload((_, e) => e.FallbackCharacter = "#");

        effects.FallbackCharacter.ShouldBe("#");
    }

    [Theory]
    [InlineData(53)]
    [InlineData(59)]
    [InlineData(29)]
    public void SaveThenLoad_KeepsTheShadowOpacityPercent(int percent)
    {
        // 0.53f * 100 is 52.999996 in float, so truncating loses a point.
        var (_, effects) = SaveAndReload((_, e) =>
        {
            e.ShadowEnabled = true;
            e.ShadowOffsetX = 2;
            e.ShadowOffsetY = 2;
            e.ShadowOpacity = percent;
        });

        effects.ShadowOpacity.ShouldBe(percent);
    }
}
