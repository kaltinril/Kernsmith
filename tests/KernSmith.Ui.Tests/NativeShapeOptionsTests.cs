using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The LCD filter and subpixel order, outline mode, synthetic width, italic angle and SDF scale
/// must travel from the ViewModels through <see cref="GenerationRequest"/> into generation and
/// through project save/load, and the options only the Native backend supports
/// (<see cref="OutlineMode.Vector"/>, <c>WidthPercent</c>, <c>ItalicAngle</c>) must never reach
/// a backend that rejects them.
/// </summary>
public sealed class NativeShapeOptionsTests : IDisposable
{
    private static readonly string RobotoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-shape-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static (FontConfigViewModel, AtlasConfigViewModel, EffectsViewModel, CharacterGridViewModel) NewViewModels(
        RasterizerBackend backend)
        => (new FontConfigViewModel { SelectedBackend = backend }, new AtlasConfigViewModel(),
            new EffectsViewModel(), new CharacterGridViewModel());

    private static void SetNonDefaults(EffectsViewModel effects)
    {
        effects.AntiAlias = AntiAliasMode.Lcd;
        effects.LcdFilter = LcdFilter.Smooth;
        effects.LcdSubpixelOrder = LcdSubpixelOrder.VBgr;
        effects.OutlineEnabled = true;
        effects.OutlineWidth = 2;
        effects.OutlineMode = OutlineMode.Vector;
        effects.WidthPercent = 80;
        effects.ItalicAngle = 20f;
        effects.SdfScale = 3;
    }

    private string WriteConfig(string name, string extraKeys)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "fileVersion=1\nfontName=Arial\nfontSize=32\nchars=32-126\n" + extraKeys);
        return path;
    }

    private FontGeneratorOptions LoadThenSave(string extraKeys,
        Action<FontConfigViewModel, EffectsViewModel>? afterLoad = null)
    {
        var service = new ProjectService();
        var fontConfig = new FontConfigViewModel();
        var atlasConfig = new AtlasConfigViewModel();
        var effects = new EffectsViewModel();
        var characterGrid = new CharacterGridViewModel();
        service.LoadProject(WriteConfig("source.bmfc", extraKeys), fontConfig, atlasConfig, effects, characterGrid);
        afterLoad?.Invoke(fontConfig, effects);

        var resaved = Path.Combine(_dir, "resaved.bmfc");
        service.SaveProject(resaved, fontConfig, atlasConfig, effects, characterGrid);
        return BmfcConfigReader.Read(resaved).Options;
    }

    // -- Defaults and capabilities --

    [Fact]
    public void EffectsViewModel_DefaultsMatchTheLibraryDefaults()
    {
        var effects = new EffectsViewModel();
        var defaults = new FontGeneratorOptions();

        effects.LcdFilter.ShouldBe(defaults.LcdFilter);
        effects.LcdSubpixelOrder.ShouldBe(defaults.LcdSubpixelOrder);
        effects.OutlineMode.ShouldBe(defaults.OutlineMode);
        effects.WidthPercent.ShouldBe(defaults.WidthPercent);
        effects.ItalicAngle.ShouldBe(defaults.ItalicAngle);
        effects.SdfScale.ShouldBe(defaults.SdfScale);
    }

    [Theory]
    [InlineData(RasterizerBackend.Native, true)]
    [InlineData(RasterizerBackend.FreeType, false)]
    [InlineData(RasterizerBackend.StbTrueType, false)]
    public void FontConfig_ReportsStrokeWidthAndAngleSupport(RasterizerBackend backend, bool expected)
    {
        var fontConfig = new FontConfigViewModel { SelectedBackend = backend };

        fontConfig.BackendSupportsOutlineStroke.ShouldBe(expected);
        fontConfig.BackendSupportsWidthScale.ShouldBe(expected);
        fontConfig.BackendSupportsItalicAngle.ShouldBe(expected);
    }

    [Fact]
    public void MainViewModel_SyncsStrokeWidthAndAngleSupportToEffects()
    {
        // The constructor only stores the game; nothing here touches it.
        var vm = new MainViewModel(new FontDiscoveryService(), new GenerationService(),
            new ProjectService(), new SessionService(), game: null!);

        vm.FontConfig.SelectedBackend = RasterizerBackend.Native;
        vm.Effects.BackendSupportsOutlineStroke.ShouldBeTrue();
        vm.Effects.BackendSupportsWidthScale.ShouldBeTrue();
        vm.Effects.BackendSupportsItalicAngle.ShouldBeTrue();

        vm.FontConfig.SelectedBackend = RasterizerBackend.FreeType;
        vm.Effects.BackendSupportsOutlineStroke.ShouldBeFalse();
        vm.Effects.BackendSupportsWidthScale.ShouldBeFalse();
        vm.Effects.BackendSupportsItalicAngle.ShouldBeFalse();
    }

    // -- ViewModels -> GenerationRequest (with capability gating) --

    [Fact]
    public void BuildGenerationRequest_Native_CarriesEveryOption()
    {
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels(RasterizerBackend.Native);
        SetNonDefaults(effects);

        var request = MainViewModel.BuildGenerationRequest(fontConfig, atlasConfig, effects, characterGrid);

        request.LcdFilter.ShouldBe(LcdFilter.Smooth);
        request.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.VBgr);
        request.OutlineMode.ShouldBe(OutlineMode.Vector);
        request.WidthPercent.ShouldBe(80);
        request.ItalicAngle.ShouldBe(20f);
        request.SdfScale.ShouldBe(3);
    }

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
    public void BuildGenerationRequest_UnsupportingBackend_SendsDefaultsForNativeOnlyOptions(RasterizerBackend backend)
    {
        // Values chosen while Native was selected stay in the ViewModel (switching back restores
        // them), but another backend throws NotSupportedException for a non-default width or
        // angle, so the request must carry the defaults.
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels(backend);
        SetNonDefaults(effects);

        var request = MainViewModel.BuildGenerationRequest(fontConfig, atlasConfig, effects, characterGrid);

        request.OutlineMode.ShouldBe(OutlineMode.Effect);
        request.WidthPercent.ShouldBe(100);
        request.ItalicAngle.ShouldBe(12f);
        effects.WidthPercent.ShouldBe(80);
        effects.ItalicAngle.ShouldBe(20f);
        // SDF scale and the LCD settings are honored or ignored by every backend.
        request.SdfScale.ShouldBe(3);
        request.LcdFilter.ShouldBe(LcdFilter.Smooth);
    }

    [Theory]
    [InlineData(SdfMode.Sdf)]
    [InlineData(SdfMode.Msdf)]
    public void BuildGenerationRequest_DistanceField_SendsEffectOutline(SdfMode mode)
    {
        // Core rejects a vector outline with any distance field.
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels(RasterizerBackend.Native);
        SetNonDefaults(effects);
        effects.SdfMode = mode;

        MainViewModel.BuildGenerationRequest(fontConfig, atlasConfig, effects, characterGrid)
            .OutlineMode.ShouldBe(OutlineMode.Effect);
    }

    // -- GenerationRequest -> FontGeneratorOptions --

    [Fact]
    public void BuildOptions_PassesEveryOption()
    {
        var options = GenerationService.BuildOptions(new GenerationRequest
        {
            Backend = RasterizerBackend.Native,
            AntiAlias = AntiAliasMode.Lcd,
            LcdFilter = LcdFilter.None,
            LcdSubpixelOrder = LcdSubpixelOrder.Bgr,
            OutlineEnabled = true,
            OutlineWidth = 2,
            OutlineMode = OutlineMode.Vector,
            WidthPercent = 150,
            ItalicAngle = -10f,
            SdfMode = SdfMode.Sdf,
            SdfScale = 2,
        });

        options.LcdFilter.ShouldBe(LcdFilter.None);
        options.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.Bgr);
        options.OutlineMode.ShouldBe(OutlineMode.Vector);
        options.WidthPercent.ShouldBe(150);
        options.ItalicAngle.ShouldBe(-10f);
        options.SdfScale.ShouldBe(2);
    }

    // -- End to end through GenerationService --

    private static GenerationRequest RobotoRequest(RasterizerBackend backend, int widthPercent = 100) => new()
    {
        FontData = File.ReadAllBytes(RobotoPath),
        SourceKind = FontSourceKind.File,
        FontSize = 32,
        Characters = CharacterSet.FromChars("MW"),
        Backend = backend,
        WidthPercent = widthPercent,
    };

    [Fact]
    public async Task GenerateAsync_NativeWidthPercent_NarrowsTheAdvances()
    {
        var normal = await new GenerationService().GenerateAsync(RobotoRequest(RasterizerBackend.Native));
        var condensed = await new GenerationService().GenerateAsync(RobotoRequest(RasterizerBackend.Native, 50));

        var normalM = normal.Model.Characters.Single(c => c.Id == 'M').XAdvance;
        var condensedM = condensed.Model.Characters.Single(c => c.Id == 'M').XAdvance;
        condensedM.ShouldBeInRange(normalM / 2 - 1, normalM / 2 + 1);
    }

    [Fact]
    public async Task GenerateAsync_FreeTypeAfterNativeWidth_DoesNotThrow()
    {
        // The user picks a width on Native, then switches to FreeType: generation must still work.
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels(RasterizerBackend.FreeType);
        fontConfig.LoadFromFile(RobotoPath);
        characterGrid.Clear();
        characterGrid.SelectAll("MW".Select(c => (int)c));
        effects.WidthPercent = 50;
        effects.Italic = true;
        effects.ForceSyntheticItalic = true;
        effects.ItalicAngle = 30f;

        var request = MainViewModel.BuildGenerationRequest(fontConfig, atlasConfig, effects, characterGrid);
        var result = await new GenerationService().GenerateAsync(request);

        result.Model.Characters.Select(c => c.Id).ShouldContain('M');
    }

    // -- Project save/load (.bmfc) --

    [Fact]
    public void LoadThenSave_Native_PreservesEveryOption()
    {
        EffectsViewModel? loaded = null;
        var reloaded = LoadThenSave(
            "rasterizer=native\nantiAlias=lcd\nlcdFilter=smooth\nlcdOrder=vbgr\noutlineThickness=2\n" +
            "outlineMode=vector\nwidthPercent=80\nitalicAngle=20\n",
            (_, effects) => loaded = effects);

        loaded!.LcdFilter.ShouldBe(LcdFilter.Smooth);
        loaded.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.VBgr);
        loaded.OutlineMode.ShouldBe(OutlineMode.Vector);
        loaded.WidthPercent.ShouldBe(80);
        loaded.ItalicAngle.ShouldBe(20f);

        reloaded.LcdFilter.ShouldBe(LcdFilter.Smooth);
        reloaded.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.VBgr);
        reloaded.OutlineMode.ShouldBe(OutlineMode.Vector);
        reloaded.WidthPercent.ShouldBe(80);
        reloaded.ItalicAngle.ShouldBe(20f);
    }

    [Fact]
    public void LoadThenSave_PreservesTheSdfScale()
    {
        int? loaded = null;
        var reloaded = LoadThenSave("useSdf=1\nsdfScale=3\n", (_, effects) => loaded = effects.SdfScale);

        loaded.ShouldBe(3);
        reloaded.SdfScale.ShouldBe(3);
    }

    [Fact]
    public void Save_UnsupportingBackend_WritesDefaultsForNativeOnlyOptions()
    {
        // A saved project must not hold a width/angle its rasterizer throws on.
        var reloaded = LoadThenSave(
            "rasterizer=native\noutlineThickness=2\noutlineMode=vector\nwidthPercent=80\nitalicAngle=20\n",
            (fontConfig, _) => fontConfig.SelectedBackend = RasterizerBackend.FreeType);

        reloaded.Backend.ShouldBe(RasterizerBackend.FreeType);
        reloaded.OutlineMode.ShouldBe(OutlineMode.Effect);
        reloaded.WidthPercent.ShouldBe(100);
        reloaded.ItalicAngle.ShouldBe(12f);
    }

    // -- .hiero lossy-save warning --

    [Fact]
    public void LossyHieroSettings_ListsTheNativeOnlyOptions()
    {
        var vm = new MainViewModel(new FontDiscoveryService(), new GenerationService(),
            new ProjectService(), new SessionService(), game: null!);
        vm.FontConfig.SelectedBackend = RasterizerBackend.Native;
        SetNonDefaults(vm.Effects);
        vm.Effects.Italic = true;

        var lost = vm.GetLossyHieroSettings();

        lost.ShouldContain("LCD filter/subpixel order");
        lost.ShouldContain("Vector outline mode");
        lost.ShouldContain("Synthetic width");
        lost.ShouldContain("Italic angle");
    }

    [Fact]
    public void LossyHieroSettings_DefaultsListNoneOfTheNativeOnlyOptions()
    {
        var vm = new MainViewModel(new FontDiscoveryService(), new GenerationService(),
            new ProjectService(), new SessionService(), game: null!);
        vm.FontConfig.SelectedBackend = RasterizerBackend.Native;

        var lost = vm.GetLossyHieroSettings();

        lost.ShouldNotContain("LCD filter/subpixel order");
        lost.ShouldNotContain("Vector outline mode");
        lost.ShouldNotContain("Synthetic width");
        lost.ShouldNotContain("Italic angle");
    }
}
