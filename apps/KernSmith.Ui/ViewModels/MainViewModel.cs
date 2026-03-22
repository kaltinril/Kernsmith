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
    private readonly ProjectService _projectService;
    private readonly SessionService _sessionService;
    private readonly Game _game;
    private BmFontResult? _lastResult;
    private CancellationTokenSource? _autoRegenCts;

    public FontConfigViewModel FontConfig { get => Get<FontConfigViewModel>(); set => Set(value); }
    public PreviewViewModel Preview { get => Get<PreviewViewModel>(); set => Set(value); }
    public StatusBarViewModel StatusBar { get => Get<StatusBarViewModel>(); set => Set(value); }
    public CharacterGridViewModel CharacterGrid { get => Get<CharacterGridViewModel>(); set => Set(value); }
    public AtlasConfigViewModel AtlasConfig { get => Get<AtlasConfigViewModel>(); set => Set(value); }
    public EffectsViewModel Effects { get => Get<EffectsViewModel>(); set => Set(value); }
    public bool AutoRegenerate { get => Get<bool>(); set => Set(value); }

    public ProjectService ProjectService => _projectService;
    public SessionService SessionService => _sessionService;

    public MainViewModel(
        FileDialogService fileDialogService,
        FontDiscoveryService fontDiscoveryService,
        GenerationService generationService,
        ProjectService projectService,
        SessionService sessionService,
        Game game)
    {
        _fileDialogService = fileDialogService;
        _fontDiscoveryService = fontDiscoveryService;
        _generationService = generationService;
        _projectService = projectService;
        _sessionService = sessionService;
        _game = game;

        FontConfig = new FontConfigViewModel();
        Preview = new PreviewViewModel();
        StatusBar = new StatusBarViewModel();
        CharacterGrid = new CharacterGridViewModel();
        AtlasConfig = new AtlasConfigViewModel();
        Effects = new EffectsViewModel();

        // Wire auto-regeneration from sub-viewmodels
        FontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FontConfigViewModel.FontSize)
                or nameof(FontConfigViewModel.SelectedPreset)
                or nameof(FontConfigViewModel.CustomCharacters))
                RequestAutoRegenerate();
        };

        CharacterGrid.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CharacterGridViewModel.SelectedCount))
                RequestAutoRegenerate();
        };

        Effects.PropertyChanged += (_, _) => RequestAutoRegenerate();
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
            _sessionService.AddRecentFont(path);
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

            StatusBar.GlyphInfoText = Preview.GlyphInfoText;
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

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            _sessionService.State.LastOutputDir = dir;

        StatusBar.StatusText = $"Saved to {path}";
    }

    public void SaveProject()
    {
        var path = _projectService.CurrentProjectPath;
        if (path == null)
        {
            path = _fileDialogService.SaveFile("myproject", "bmfc");
            if (path == null) return;
        }

        try
        {
            _projectService.SaveProject(path, FontConfig, AtlasConfig, Effects, CharacterGrid);
            _sessionService.State.LastProjectPath = path;
            StatusBar.StatusText = $"Project saved to {path}";
        }
        catch (Exception ex)
        {
            StatusBar.SetError($"Save failed: {ex.Message}");
        }
    }

    public void LoadProject()
    {
        var path = _fileDialogService.OpenFontFile();
        if (path == null) return;
        LoadProjectFromPath(path);
    }

    public void LoadProjectFromPath(string path)
    {
        try
        {
            _projectService.LoadProject(path, FontConfig, AtlasConfig, Effects, CharacterGrid);
            _sessionService.State.LastProjectPath = path;
            if (FontConfig.IsFontLoaded)
                StatusBar.StatusText = $"Project loaded: {Path.GetFileName(path)} — {FontConfig.FamilyName}";
            else
                StatusBar.StatusText = $"Project loaded: {Path.GetFileName(path)} (no font file found)";
        }
        catch (Exception ex)
        {
            StatusBar.SetError($"Load failed: {ex.Message}");
        }
    }

    public void RequestAutoRegenerate()
    {
        if (!AutoRegenerate || !FontConfig.IsFontLoaded) return;

        _autoRegenCts?.Cancel();
        _autoRegenCts = new CancellationTokenSource();
        var token = _autoRegenCts.Token;

        Task.Delay(500, token).ContinueWith(async _ =>
        {
            if (!token.IsCancellationRequested)
                await GenerateAsync();
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
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
