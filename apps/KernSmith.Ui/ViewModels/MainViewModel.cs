using System.Diagnostics;
using Gum.Mvvm;
using KernSmith.Output;
using KernSmith.Ui.Layout;
using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using Microsoft.Xna.Framework;
using NativeFileDialogNET;

namespace KernSmith.Ui.ViewModels;

/// <summary>
/// Central orchestrator for the UI. Owns all child ViewModels, coordinates font loading,
/// bitmap font generation, project save/load, auto-regeneration, dirty tracking, and
/// window title updates.
/// </summary>
public class MainViewModel : ViewModel
{
    private static readonly string AppVersion = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly string BaseTitle = $"KernSmith v{AppVersion}";

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
        FontDiscoveryService fontDiscoveryService,
        GenerationService generationService,
        ProjectService projectService,
        SessionService sessionService,
        KernSmithGame game)
    {
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
            if (e.PropertyName is nameof(FontConfigViewModel.FontSize))
            {
                MarkDirty();
                RequestAutoRegenerateDebounced();
            }

            if (e.PropertyName is nameof(FontConfigViewModel.HasColorGlyphs))
            {
                Effects.HasColorGlyphs = FontConfig.HasColorGlyphs;
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

        AtlasConfig.PropertyChanged += (_, _) =>
        {
            MarkDirty();
            RequestAutoRegenerateDebounced();
        };
        Effects.PropertyChanged += (_, e) =>
        {
            MarkDirty();

            // When Bold/Italic changes, try to load the real font variant file
            if (e.PropertyName is nameof(EffectsViewModel.Bold) or nameof(EffectsViewModel.Italic))
            {
                if (FontConfig.FontSourceKind == Models.FontSourceKind.System && FontConfig.CurrentFontGroup != null)
                {
                    var loaded = FontConfig.TryLoadStyleVariant(Effects.Bold, Effects.Italic);
                    if (loaded)
                    {
                        StatusBar.StatusText = $"Loaded {FontConfig.FamilyName} {FontConfig.StyleName}";
                    }
                }
            }

            // Debounce to allow batched updates (e.g. color picker sets R, G, B sequentially)
            RequestAutoRegenerateDebounced();
        };
    }

    /// <summary>
    /// Opens a file browser dialog to select and load a font file.
    /// </summary>
    public void OpenFont()
    {
        using var dialog = new NativeFileDialog()
            .SelectFile()
            .AddFilter("Font Files", "ttf,otf,woff,ttc")
            .AddFilter("All Files", "*");
        var result = dialog.Open(out string? path);
        if (result == DialogResult.Okay && path != null)
            LoadFontFromPath(path);
    }

    /// <summary>
    /// Loads a font file from the given path, updates metadata, and adds it to recent fonts.
    /// </summary>
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
        Effects.HasColorGlyphs = FontConfig.HasColorGlyphs;

        if (FontConfig.LoadedVariationAxes is { Count: > 0 })
        {
            foreach (var axis in FontConfig.LoadedVariationAxes)
                Effects.VariationAxisValues[axis.Tag] = axis.DefaultValue;
        }
    }

    /// <summary>
    /// Returns a validation error message if generation cannot proceed, or null if ready.
    /// </summary>
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

    /// <summary>
    /// Validates settings, builds a <see cref="GenerationRequest"/>, runs generation on a
    /// background thread, and marshals the preview/status update back to the main thread.
    /// </summary>
    public async Task GenerateAsync()
    {
        if (StatusBar.IsGenerating) return;

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
            var outlineRgb = EffectsViewModel.ParseHex(Effects.OutlineColor);
            var shadowRgb = EffectsViewModel.ParseHex(Effects.ShadowColor);
            var gradStartRgb = EffectsViewModel.ParseHex(Effects.GradientStartColor);
            var gradEndRgb = EffectsViewModel.ParseHex(Effects.GradientEndColor);

            var request = new GenerationRequest
            {
                FontData = FontConfig.FontData,
                FontFilePath = FontConfig.FontFilePath,
                // When we've loaded a specific style variant file, use it directly
                // instead of letting the library pick via family name
                SystemFontFamily = null,
                SourceKind = FontConfig.FontFilePath != null
                    ? FontSourceKind.File
                    : FontConfig.FontSourceKind,
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
                // Only apply synthetic bold/italic if the font file doesn't already have it
                Bold = Effects.Bold && !FontConfig.LoadedAsBold,
                Italic = Effects.Italic && !FontConfig.LoadedAsItalic,
                AntiAlias = Effects.AntiAlias,
                Hinting = Effects.Hinting,
                SuperSampleLevel = Effects.SuperSampleLevel,
                OutlineEnabled = Effects.OutlineEnabled,
                OutlineWidth = Effects.OutlineWidth,
                OutlineColorR = outlineRgb.R,
                OutlineColorG = outlineRgb.G,
                OutlineColorB = outlineRgb.B,
                ShadowEnabled = Effects.ShadowEnabled,
                ShadowOffsetX = Effects.ShadowOffsetX,
                ShadowOffsetY = Effects.ShadowOffsetY,
                ShadowBlur = Effects.ShadowBlur,
                ShadowColorR = shadowRgb.R,
                ShadowColorG = shadowRgb.G,
                ShadowColorB = shadowRgb.B,
                ShadowOpacity = Effects.ShadowOpacity,
                HardShadow = Effects.HardShadow,
                GradientEnabled = Effects.GradientEnabled,
                GradientStartR = gradStartRgb.R,
                GradientStartG = gradStartRgb.G,
                GradientStartB = gradStartRgb.B,
                GradientEndR = gradEndRgb.R,
                GradientEndG = gradEndRgb.G,
                GradientEndB = gradEndRgb.B,
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

    /// <summary>
    /// Opens a save dialog and exports the last generated result to the chosen path.
    /// If no result has been generated yet, triggers generation first.
    /// </summary>
    public async void SaveAs()
    {
        if (_lastResult == null)
        {
            var validationError = ValidateBeforeGenerate();
            if (validationError != null)
            {
                StatusBar.SetError(validationError);
                return;
            }

            StatusBar.StatusText = "Generating before export...";
            await GenerateAsync();

            if (_lastResult == null)
            {
                StatusBar.SetError("Generation failed — cannot export.");
                return;
            }
        }

        var initialDir = _sessionService.State.LastOutputDir;
        using var dialog = new NativeFileDialog()
            .SaveFile()
            .AddFilter("BMFont Files", "*.fnt");
        var result = dialog.Open(out string? path, initialDir, "myfont.fnt");
        if (result == DialogResult.Okay && path != null)
        {
            try
            {
                // ToFile expects a base path without extension (e.g., "output/myfont")
                var basePath = path;
                var ext = Path.GetExtension(path);
                if (ext.Equals(".fnt", StringComparison.OrdinalIgnoreCase))
                    basePath = path[..^ext.Length];

                _lastResult!.ToFile(basePath);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    _sessionService.State.LastOutputDir = dir;

                StatusBar.StatusText = $"Exported to {basePath}.fnt";
            }
            catch (Exception ex)
            {
                StatusBar.SetError($"Export failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Saves the current project. Uses the existing path if available, otherwise opens a save dialog.
    /// </summary>
    public void SaveProject()
    {
        var path = _projectService.CurrentProjectPath;
        if (path != null)
        {
            DoSaveProject(path);
            return;
        }

        var initialDir = _sessionService.State.LastOutputDir;
        using var dialog = new NativeFileDialog()
            .SaveFile()
            .AddFilter("KernSmith Projects", "*.bmfc");
        var result = dialog.Open(out string? savePath, initialDir, "myproject.bmfc");
        if (result == DialogResult.Okay && savePath != null)
            DoSaveProject(savePath);
    }

    private void DoSaveProject(string path)
    {
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

    /// <summary>
    /// Opens a file browser dialog to select and load a .bmfc project file.
    /// </summary>
    public void LoadProject()
    {
        using var dialog = new NativeFileDialog()
            .SelectFile()
            .AddFilter("KernSmith Projects", "bmfc")
            .AddFilter("All Files", "*");
        var result = dialog.Open(out string? path);
        if (result == DialogResult.Okay && path != null)
            LoadProjectFromPath(path);
    }

    /// <summary>
    /// Loads a .bmfc project file from the given path, populating all ViewModels.
    /// </summary>
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

    /// <summary>
    /// Triggers immediate auto-regeneration (for checkbox/dropdown changes).
    /// Only triggers when <see cref="AutoRegenerate"/> is enabled and a font is loaded.
    /// </summary>
    public void RequestAutoRegenerate()
    {
        if (!AutoRegenerate || !FontConfig.IsFontLoaded) return;

        _autoRegenCts?.Cancel();
        _ = GenerateAsync();
    }

    /// <summary>
    /// Debounced auto-regeneration with 300ms delay (for text input changes).
    /// </summary>
    public void RequestAutoRegenerateDebounced()
    {
        if (!AutoRegenerate || !FontConfig.IsFontLoaded) return;

        _autoRegenCts?.Cancel();
        _autoRegenCts = new CancellationTokenSource();
        var token = _autoRegenCts.Token;

        Task.Delay(300, token).ContinueWith(async _ =>
        {
            if (!token.IsCancellationRequested)
                await GenerateAsync();
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>
    /// Exits the application by calling <see cref="Game.Exit"/>.
    /// </summary>
    public void Exit()
    {
        _game.Exit();
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
