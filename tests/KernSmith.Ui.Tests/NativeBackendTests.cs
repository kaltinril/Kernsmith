using KernSmith.Atlas;
using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The desktop UI must offer the Native rasterizer and carry every Native-relevant option
/// (rasterizer, distance-field mode, anti-alias mode, color palette, variation axes) from the
/// ViewModels through <see cref="GenerationRequest"/> into generation, and through project
/// save/load.
/// </summary>
public sealed class NativeBackendTests : IDisposable
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string RobotoPath = Path.Combine(FixtureDir, "Roboto-Regular.ttf");
    private static readonly string BungeeColorPath = Path.Combine(FixtureDir, "BungeeColor-Regular.ttf");

    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-native-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static (FontConfigViewModel, AtlasConfigViewModel, EffectsViewModel, CharacterGridViewModel) NewViewModels()
        => (new FontConfigViewModel(), new AtlasConfigViewModel(), new EffectsViewModel(), new CharacterGridViewModel());

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
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        service.LoadProject(WriteConfig("source.bmfc", extraKeys), fontConfig, atlasConfig, effects, characterGrid);
        afterLoad?.Invoke(fontConfig, effects);

        var resaved = Path.Combine(_dir, "resaved.bmfc");
        service.SaveProject(resaved, fontConfig, atlasConfig, effects, characterGrid);
        return BmfcConfigReader.Read(resaved).Options;
    }

    // -- Backend selection and capabilities --

    [Fact]
    public void FontConfig_ListsNativeBackend()
    {
        new FontConfigViewModel().AvailableBackends.ShouldContain(RasterizerBackend.Native);
    }

    [Fact]
    public void SelectingNative_ReportsMsdfAndEveryAntiAliasMode()
    {
        var fontConfig = new FontConfigViewModel { SelectedBackend = RasterizerBackend.Native };

        fontConfig.BackendSupportsMsdf.ShouldBeTrue();
        fontConfig.BackendSupportsSdf.ShouldBeTrue();
        fontConfig.BackendSupportsColorFonts.ShouldBeTrue();
        fontConfig.BackendSupportsVariableFonts.ShouldBeTrue();
        fontConfig.BackendAntiAliasModes.ShouldBe(
            [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Light, AntiAliasMode.Lcd],
            ignoreOrder: true);
    }

    [Fact]
    public void SelectingFreeType_ReportsNoMsdf()
    {
        var fontConfig = new FontConfigViewModel { SelectedBackend = RasterizerBackend.Native };
        fontConfig.SelectedBackend = RasterizerBackend.FreeType;

        fontConfig.BackendSupportsMsdf.ShouldBeFalse();
    }

    [Fact]
    public void MainViewModel_StartsWithTheDefaultBackendsCapabilities()
    {
        // FontConfig computes its backend's capabilities before Effects exists, so without an
        // initial sync the Anti-Alias combo lists only None/Grayscale until the backend changes.
        // The constructor only stores the game; nothing here touches it.
        var vm = new MainViewModel(new FontDiscoveryService(), new GenerationService(),
            new ProjectService(), new SessionService(), game: null!);

        vm.Effects.BackendAntiAliasModes.ShouldBe(vm.FontConfig.BackendAntiAliasModes);
        vm.Effects.BackendAntiAliasModes.ShouldContain(AntiAliasMode.Light);
        vm.Effects.BackendSupportsMsdf.ShouldBe(vm.FontConfig.BackendSupportsMsdf);
    }

    // -- ViewModels -> GenerationRequest --

    [Fact]
    public void BuildGenerationRequest_CarriesNativeOptions()
    {
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        fontConfig.SelectedBackend = RasterizerBackend.Native;
        effects.SdfMode = SdfMode.Mtsdf;
        effects.AntiAlias = AntiAliasMode.Lcd;
        effects.ColorFontEnabled = true;
        effects.ColorPaletteIndex = 3;
        effects.VariationAxisValues["wght"] = 650f;

        var request = MainViewModel.BuildGenerationRequest(fontConfig, atlasConfig, effects, characterGrid);

        request.Backend.ShouldBe(RasterizerBackend.Native);
        request.SdfMode.ShouldBe(SdfMode.Mtsdf);
        request.AntiAlias.ShouldBe(AntiAliasMode.Lcd);
        request.ColorFontEnabled.ShouldBeTrue();
        request.ColorPaletteIndex.ShouldBe(3);
        request.VariationAxisValues.ShouldNotBeNull();
        request.VariationAxisValues!["wght"].ShouldBe(650f);
    }

    // -- GenerationRequest -> FontGeneratorOptions --

    [Theory]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light)]
    [InlineData(AntiAliasMode.Lcd)]
    public void BuildOptions_PassesTheAntiAliasMode(AntiAliasMode mode)
    {
        GenerationService.BuildOptions(new GenerationRequest { AntiAlias = mode }).AntiAlias.ShouldBe(mode);
    }

    [Theory]
    [InlineData(SdfMode.None)]
    [InlineData(SdfMode.Sdf)]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void BuildOptions_PassesTheDistanceFieldMode(SdfMode mode)
    {
        GenerationService.BuildOptions(new GenerationRequest { SdfMode = mode }).EffectiveSdfMode.ShouldBe(mode);
    }

    [Fact]
    public void BuildOptions_PassesBackendPaletteAndHinting()
    {
        var options = GenerationService.BuildOptions(new GenerationRequest
        {
            Backend = RasterizerBackend.Native,
            ColorFontEnabled = true,
            ColorPaletteIndex = 2,
            Hinting = false,
        });

        options.Backend.ShouldBe(RasterizerBackend.Native);
        options.ColorFont.ShouldBeTrue();
        options.ColorPaletteIndex.ShouldBe(2);
        options.EnableHinting.ShouldBeFalse();
    }

    // -- End to end through GenerationService on the Native backend --

    [Fact]
    public async Task GenerateAsync_NativeMsdf_WritesAnRgbaMultiChannelAtlas()
    {
        // Only Native supports MSDF, so a successful MSDF generation proves both the backend
        // and the mode reached the library.
        var result = await new GenerationService().GenerateAsync(new GenerationRequest
        {
            FontData = File.ReadAllBytes(RobotoPath),
            SourceKind = FontSourceKind.File,
            FontSize = 32,
            Characters = CharacterSet.FromChars("AB"),
            Backend = RasterizerBackend.Native,
            SdfMode = SdfMode.Msdf,
        });

        result.Model.Extended.ShouldNotBeNull();
        result.Model.Extended!.SdfMode.ShouldBe(SdfMode.Msdf);
        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public async Task GenerateAsync_NativeLcd_WritesAnRgbaAtlas()
    {
        // Native renders LCD as RGBA (FreeType labels its LCD buffer Grayscale8), so the page
        // format shows both the backend and the mode were honored.
        var result = await new GenerationService().GenerateAsync(new GenerationRequest
        {
            FontData = File.ReadAllBytes(RobotoPath),
            SourceKind = FontSourceKind.File,
            FontSize = 24,
            Characters = CharacterSet.FromChars("AB"),
            Backend = RasterizerBackend.Native,
            AntiAlias = AntiAliasMode.Lcd,
        });

        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public async Task GenerateAsync_NativeColorPalette_ChangesTheColors()
    {
        async Task<byte[]> Render(int palette) => (await new GenerationService().GenerateAsync(new GenerationRequest
        {
            FontData = File.ReadAllBytes(BungeeColorPath),
            SourceKind = FontSourceKind.File,
            FontSize = 32,
            Characters = CharacterSet.FromChars("AB"),
            Backend = RasterizerBackend.Native,
            ColorFontEnabled = true,
            ColorPaletteIndex = palette,
        })).Pages[0].PixelData;

        var palette0 = await Render(0);
        var palette1 = await Render(1);

        palette0.Length.ShouldBe(palette1.Length);
        palette0.SequenceEqual(palette1).ShouldBeFalse("palette 1 must recolor Bungee Color's glyphs");
    }

    // -- Project save/load (.bmfc) --

    [Fact]
    public void LoadThenSave_PreservesNativeRasterizer()
    {
        LoadThenSave("rasterizer=native\n").Backend.ShouldBe(RasterizerBackend.Native);
    }

    [Fact]
    public void LoadProject_SelectsTheRasterizer()
    {
        var service = new ProjectService();
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();

        service.LoadProject(WriteConfig("native.bmfc", "rasterizer=native\n"), fontConfig, atlasConfig, effects, characterGrid);

        fontConfig.SelectedBackend.ShouldBe(RasterizerBackend.Native);
    }

    [Theory]
    [InlineData("sdfMode=msdf\n", SdfMode.Msdf)]
    [InlineData("sdfMode=mtsdf\n", SdfMode.Mtsdf)]
    [InlineData("sdfMode=sdf\n", SdfMode.Sdf)]
    [InlineData("useSdf=1\n", SdfMode.Sdf)]
    public void LoadThenSave_PreservesTheDistanceFieldMode(string keys, SdfMode expected)
    {
        SdfMode? loaded = null;
        var reloaded = LoadThenSave("rasterizer=native\n" + keys, (_, effects) => loaded = effects.SdfMode);

        loaded.ShouldBe(expected);
        reloaded.EffectiveSdfMode.ShouldBe(expected);
    }

    [Fact]
    public void Save_PlainSdf_StillWritesUseSdf()
    {
        // Single-channel SDF keeps BMFont-style useSdf=1 rather than switching to sdfMode=sdf.
        LoadThenSave("useSdf=1\n").Sdf.ShouldBeTrue();
    }

    [Theory]
    [InlineData("useSmoothing=0\n", AntiAliasMode.None)]
    [InlineData("", AntiAliasMode.Grayscale)]
    [InlineData("antiAlias=light\n", AntiAliasMode.Light)]
    [InlineData("antiAlias=lcd\n", AntiAliasMode.Lcd)]
    public void LoadThenSave_PreservesTheAntiAliasMode(string keys, AntiAliasMode expected)
    {
        AntiAliasMode? loaded = null;
        var reloaded = LoadThenSave("rasterizer=native\n" + keys, (_, effects) => loaded = effects.AntiAlias);

        loaded.ShouldBe(expected);
        reloaded.AntiAlias.ShouldBe(expected);
    }

    [Fact]
    public void LoadThenSave_PreservesTheColorPalette()
    {
        int? loaded = null;
        var reloaded = LoadThenSave("rasterizer=native\ncolorFont=1\ncolorPalette=2\n",
            (_, effects) => loaded = effects.ColorPaletteIndex);

        loaded.ShouldBe(2);
        reloaded.ColorFont.ShouldBeTrue();
        reloaded.ColorPaletteIndex.ShouldBe(2);
    }

    [Fact]
    public void LoadThenSave_PreservesVariationAxes()
    {
        float? loaded = null;
        var reloaded = LoadThenSave("rasterizer=native\nvariationAxes=wght:700,wdth:90\n",
            (_, effects) => loaded = effects.VariationAxisValues.GetValueOrDefault("wght"));

        loaded.ShouldBe(700f);
        reloaded.VariationAxes.ShouldNotBeNull();
        reloaded.VariationAxes!["wght"].ShouldBe(700f);
        reloaded.VariationAxes!["wdth"].ShouldBe(90f);
    }
}
