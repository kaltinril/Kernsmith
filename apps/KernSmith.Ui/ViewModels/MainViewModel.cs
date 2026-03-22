using System.Diagnostics;
using Gum.Mvvm;
using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using KernSmith.Output;
using Microsoft.Xna.Framework;

namespace KernSmith.Ui.ViewModels;

public class MainViewModel : ViewModel
{
    private readonly FileDialogService _fileDialogService;
    private readonly FontDiscoveryService _fontDiscoveryService;
    private readonly GenerationService _generationService;
    private readonly Game _game;
    private BmFontResult? _lastResult;

    public FontConfigViewModel FontConfig { get => Get<FontConfigViewModel>(); set => Set(value); }
    public PreviewViewModel Preview { get => Get<PreviewViewModel>(); set => Set(value); }
    public StatusBarViewModel StatusBar { get => Get<StatusBarViewModel>(); set => Set(value); }
    public CharacterGridViewModel CharacterGrid { get => Get<CharacterGridViewModel>(); set => Set(value); }
    public AtlasConfigViewModel AtlasConfig { get => Get<AtlasConfigViewModel>(); set => Set(value); }
    public EffectsViewModel Effects { get => Get<EffectsViewModel>(); set => Set(value); }

    public MainViewModel(
        FileDialogService fileDialogService,
        FontDiscoveryService fontDiscoveryService,
        GenerationService generationService,
        Game game)
    {
        _fileDialogService = fileDialogService;
        _fontDiscoveryService = fontDiscoveryService;
        _generationService = generationService;
        _game = game;

        FontConfig = new FontConfigViewModel();
        Preview = new PreviewViewModel();
        StatusBar = new StatusBarViewModel();
        CharacterGrid = new CharacterGridViewModel();
        AtlasConfig = new AtlasConfigViewModel();
        Effects = new EffectsViewModel();
    }

    public void OpenFont()
    {
        var path = _fileDialogService.OpenFontFile();
        if (path == null) return;
        LoadFontFromPath(path);
    }

    public void LoadFontFromPath(string path)
    {
        try
        {
            FontConfig.LoadFromFile(path);
            StatusBar.StatusText = $"Loaded {FontConfig.FamilyName} {FontConfig.StyleName} ({FontConfig.NumGlyphs:N0} glyphs)";
        }
        catch (FontParsingException ex)
        {
            StatusBar.SetError(ex.Message);
        }
        catch (Exception ex)
        {
            StatusBar.SetError(ex.Message);
        }
    }

    public async Task GenerateAsync()
    {
        if (!FontConfig.IsFontLoaded || StatusBar.IsGenerating)
            return;

        StatusBar.SetGenerating();

        var sw = Stopwatch.StartNew();

        try
        {
            var request = new GenerationRequest
            {
                FontData = FontConfig.FontData,
                FontFilePath = FontConfig.FontFilePath,
                SystemFontFamily = FontConfig.FontSourceKind == FontSourceKind.System
                    ? FontConfig.FontSourceDescription?.Replace(" (System)", "")
                    : null,
                SourceKind = FontConfig.FontSourceKind,
                FontSize = FontConfig.FontSize,
                Characters = CharacterGrid.ToCharacterSet(),
                MaxWidth = AtlasConfig.MaxWidth,
                MaxHeight = AtlasConfig.MaxHeight,
                PowerOfTwo = AtlasConfig.PowerOfTwo,
                AutofitTexture = AtlasConfig.AutofitTexture,
                PaddingUp = AtlasConfig.PaddingUp,
                PaddingRight = AtlasConfig.PaddingRight,
                PaddingDown = AtlasConfig.PaddingDown,
                PaddingLeft = AtlasConfig.PaddingLeft,
                SpacingH = AtlasConfig.SpacingH,
                SpacingV = AtlasConfig.SpacingV,
                IncludeKerning = AtlasConfig.IncludeKerning,
                Bold = Effects.Bold,
                Italic = Effects.Italic,
                AntiAlias = Effects.AntiAlias,
                Hinting = Effects.Hinting,
                SuperSampleLevel = Effects.SuperSampleLevel,
                OutlineEnabled = Effects.OutlineEnabled,
                OutlineWidth = Effects.OutlineWidth,
                ShadowEnabled = Effects.ShadowEnabled,
                ShadowOffsetX = Effects.ShadowOffsetX,
                ShadowOffsetY = Effects.ShadowOffsetY,
                ShadowBlur = Effects.ShadowBlur,
                SdfEnabled = Effects.SdfEnabled,
                ColorFontEnabled = Effects.ColorFontEnabled
            };

            _lastResult = await _generationService.GenerateAsync(request);

            sw.Stop();

            Preview.LoadResult(_lastResult);

            var common = _lastResult.Model.Common;
            StatusBar.SetComplete(
                pageCount: common.Pages,
                scaleW: common.ScaleW,
                scaleH: common.ScaleH,
                glyphCount: _lastResult.Model.Characters.Count,
                elapsed: sw.Elapsed);
        }
        catch (Exception ex) when (ex is BmFontException or InvalidOperationException)
        {
            sw.Stop();
            StatusBar.SetError(ex.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            StatusBar.SetError($"Unexpected error: {ex.Message}");
        }
    }

    public void SaveAs()
    {
        if (_lastResult == null) return;

        var path = _fileDialogService.SaveFile("myfont", "fnt");
        if (path == null) return;

        _lastResult.ToFile(path);
        StatusBar.StatusText = $"Saved to {path}";
    }

    public void Exit()
    {
        _game.Exit();
    }

    public void ResetLayout()
    {
        // Placeholder — layout classes handle actual splitter reset
    }
}
