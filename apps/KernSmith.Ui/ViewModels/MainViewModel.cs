using System.Diagnostics;
using Gum.Mvvm;
using KernSmith.Output;
using KernSmith.Ui.Layout;
using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using Microsoft.Xna.Framework;

namespace KernSmith.Ui.ViewModels;

public class MainViewModel : ViewModel
{
    private const string AppVersion = "1.0.0";
    private static readonly string BaseTitle = $"KernSmith v{AppVersion}";

    private readonly FileDialogService _fileDialogService;
    private readonly FontDiscoveryService _fontDiscoveryService;
    private readonly GenerationService _generationService;
    private readonly ProjectService _projectService;
    private readonly SessionService _sessionService;
    private readonly KernSmithGame _game;
    private BmFontResult? _lastResult;
    private CancellationTokenSource? _autoRegenCts;

    public FontConfigViewModel FontConfig { get => Get<FontConfigViewModel>(); set => Set(value); }
    public PreviewViewModel Preview { get => Get<PreviewViewModel>(); set => Set(value); }
    public StatusBarViewModel StatusBar { get => Get<StatusBarViewModel>(); set => Set(value); }
    public CharacterGridViewModel CharacterGrid { get => Get<CharacterGridViewModel>(); set => Set(value); }
    public AtlasConfigViewModel AtlasConfig { get => Get<AtlasConfigViewModel>(); set => Set(value); }
    public EffectsViewModel Effects { get => Get<EffectsViewModel>(); set => Set(value); }
    public bool AutoRegenerate { get => Get<bool>(); set => Set(value); }
    public string WindowTitle { get => Get<string>(); set => Set(value); }
    public bool IsDirty { get => Get<bool>(); set => Set(value); }

    public ProjectService ProjectService => _projectService;
    public SessionService SessionService => _sessionService;

    public MainViewModel(
        FileDialogService fileDialogService,
        FontDiscoveryService fontDiscoveryService,
        GenerationService generationService,
        ProjectService projectService,
        SessionService sessionService,
        KernSmithGame game)
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
        WindowTitle = BaseTitle;

        // Wire auto-regeneration and dirty tracking from sub-viewmodels
        FontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FontConfigViewModel.FontSize)
                or nameof(FontConfigViewModel.SelectedPreset)
                or nameof(FontConfigViewModel.CustomCharacters))
            {
                MarkDirty();
                RequestAutoRegenerate();
            }
        };

        CharacterGrid.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CharacterGridViewModel.SelectedCount))
            {
                MarkDirty();
                RequestAutoRegenerate();
            }
        };

        AtlasConfig.PropertyChanged += (_, _) => MarkDirty();
        Effects.PropertyChanged += (_, _) => { MarkDirty(); RequestAutoRegenerate(); };
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
            SyncVariationAxesToEffects();
            StatusBar.StatusText = $"Loaded {FontConfig.FamilyName} {FontConfig.StyleName} ({FontConfig.NumGlyphs:N0} glyphs)";
            _sessionService.AddRecentFont(path);
            UpdateWindowTitle();
        }
        catch (FontParsingException ex)
        {
            StatusBar.SetError(ex.Message);
            ErrorDialog.Show("Font Parsing Error", ex.Message);
        }
        catch (Exception ex)
        {
            StatusBar.SetError(ex.Message);
            ErrorDialog.Show("Font Load Error", ex.Message);
        }
    }

    private void SyncVariationAxesToEffects()
    {
        Effects.VariationAxisValues.Clear();
        Effects.VariationAxesList = FontConfig.LoadedVariationAxes;
        Effects.HasVariationAxes = FontConfig.HasVariationAxes;

        if (FontConfig.LoadedVariationAxes is { Count: > 0 })
        {
            foreach (var axis in FontConfig.LoadedVariationAxes)
                Effects.VariationAxisValues[axis.Tag] = axis.DefaultValue;
        }
    }

    public string? ValidateBeforeGenerate()
    {
        if (!FontConfig.IsFontLoaded)
            return "No font loaded. Open a font file first.";
        if (CharacterGrid.SelectedCount < 1)
            return "No characters selected. Choose a character set or add custom characters.";
        if (FontConfig.FontSize <= 0)
            return "Font size must be greater than 0.";
        return null;
    }

    public async Task GenerateAsync()
    {
        if (StatusBar.IsGenerating)
            return;

        var validationError = ValidateBeforeGenerate();
        if (validationError != null)
        {
            StatusBar.SetError(validationError);
            return;
        }

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
                OutlineColorR = Effects.OutlineColorR,
                OutlineColorG = Effects.OutlineColorG,
                OutlineColorB = Effects.OutlineColorB,
                ShadowEnabled = Effects.ShadowEnabled,
                ShadowOffsetX = Effects.ShadowOffsetX,
                ShadowOffsetY = Effects.ShadowOffsetY,
                ShadowBlur = Effects.ShadowBlur,
                ShadowColorR = Effects.ShadowColorR,
                ShadowColorG = Effects.ShadowColorG,
                ShadowColorB = Effects.ShadowColorB,
                ShadowOpacity = Effects.ShadowOpacity,
                GradientEnabled = Effects.GradientEnabled,
                GradientStartR = Effects.GradientStartR,
                GradientStartG = Effects.GradientStartG,
                GradientStartB = Effects.GradientStartB,
                GradientEndR = Effects.GradientEndR,
                GradientEndG = Effects.GradientEndG,
                GradientEndB = Effects.GradientEndB,
                GradientAngle = Effects.GradientAngle,
                ChannelPackingEnabled = Effects.ChannelPackingEnabled,
                SdfEnabled = Effects.SdfEnabled,
                ColorFontEnabled = Effects.ColorFontEnabled,
                PackingAlgorithmIndex = AtlasConfig.PackingAlgorithmIndex,
                FaceIndex = FontConfig.FaceIndex,
                VariationAxisValues = Effects.VariationAxisValues.Count > 0
                    ? new Dictionary<string, float>(Effects.VariationAxisValues)
                    : null,
                FallbackCharacter = Effects.FallbackCharacter
            };

            _lastResult = await _generationService.GenerateAsync(request);

            sw.Stop();

            // Marshal preview update to main thread — Texture2D creation requires the GPU thread
            var result = _lastResult;
            var elapsed = sw.Elapsed;
            _game.RunOnMainThread(() =>
            {
                Preview.LoadResult(result);

                var common = result.Model.Common;
                StatusBar.SetComplete(
                    pageCount: common.Pages,
                    scaleW: common.ScaleW,
                    scaleH: common.ScaleH,
                    glyphCount: result.Model.Characters.Count,
                    elapsed: elapsed);

                StatusBar.GlyphInfoText = Preview.GlyphInfoText;
            });
        }
        catch (AtlasPackingException)
        {
            sw.Stop();
            const string msg = "Glyphs too large for max texture size. Try reducing font size or increasing max atlas dimensions.";
            _game.RunOnMainThread(() =>
            {
                StatusBar.SetError(msg);
                ErrorDialog.Show("Atlas Packing Error", msg);
            });
        }
        catch (Exception ex) when (ex is BmFontException or InvalidOperationException)
        {
            sw.Stop();
            var msg = ex.Message;
            _game.RunOnMainThread(() => StatusBar.SetError(msg));
        }
        catch (Exception ex)
        {
            sw.Stop();
            var msg = ex.Message;
            _game.RunOnMainThread(() => StatusBar.SetError($"Unexpected error: {msg}"));
        }
    }

    public void SaveAs()
    {
        if (_lastResult == null) return;

        var path = _fileDialogService.SaveFile("myfont", "fnt");
        if (path == null) return;

        try
        {
            _lastResult.ToFile(path);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                _sessionService.State.LastOutputDir = dir;

            StatusBar.StatusText = $"Saved to {path}";
        }
        catch (Exception ex)
        {
            StatusBar.SetError($"Export failed: {ex.Message}");
        }
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
            IsDirty = false;
            UpdateWindowTitle();
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

    private void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            UpdateWindowTitle();
        }
    }

    private void UpdateWindowTitle()
    {
        if (!FontConfig.IsFontLoaded)
        {
            WindowTitle = BaseTitle;
            return;
        }

        var name = FontConfig.FamilyName;
        WindowTitle = IsDirty
            ? $"{BaseTitle} \u2014 {name} *"
            : $"{BaseTitle} \u2014 {name}";
    }
}
