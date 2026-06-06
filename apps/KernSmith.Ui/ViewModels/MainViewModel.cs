using System.Diagnostics;
using Gum.Mvvm;
using KernSmith;
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

            if (e.PropertyName is nameof(FontConfigViewModel.SelectedBackend)
                or nameof(FontConfigViewModel.BackendSupportsColorFonts)
                or nameof(FontConfigViewModel.BackendSupportsVariableFonts)
                or nameof(FontConfigViewModel.BackendSupportsSdf))
            {
                Effects.BackendSupportsColorFonts = FontConfig.BackendSupportsColorFonts;
                Effects.BackendSupportsVariableFonts = FontConfig.BackendSupportsVariableFonts;
                Effects.BackendSupportsSdf = FontConfig.BackendSupportsSdf;
                Effects.BackendIsGdi = FontConfig.SelectedBackend == RasterizerBackend.Gdi;
            }

            if (e.PropertyName is nameof(FontConfigViewModel.CurrentFontGroup))
            {
                var group = FontConfig.CurrentFontGroup;
                Effects.FontHasBoldVariant = group?.Styles.Any(s =>
                    s.StyleName.Contains("Bold", StringComparison.OrdinalIgnoreCase)) == true;
                Effects.FontHasItalicVariant = group?.Styles.Any(s =>
                    s.StyleName.Contains("Italic", StringComparison.OrdinalIgnoreCase)) == true;
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

            // When Bold/Italic/ForceSynthetic changes, try to load the appropriate font variant.
            // ForceSynthetic overrides: load the regular face so the rasterizer applies synthetic styling.
            if (e.PropertyName is nameof(EffectsViewModel.Bold) or nameof(EffectsViewModel.Italic)
                or nameof(EffectsViewModel.ForceSyntheticBold) or nameof(EffectsViewModel.ForceSyntheticItalic))
            {
                if (FontConfig.FontSourceKind == Models.FontSourceKind.System && FontConfig.CurrentFontGroup != null)
                {
                    var wantBold = Effects.Bold && !Effects.ForceSyntheticBold;
                    var wantItalic = Effects.Italic && !Effects.ForceSyntheticItalic;
                    var loaded = FontConfig.TryLoadStyleVariant(wantBold, wantItalic);
                    if (loaded)
                    {
                        StatusBar.StatusText = $"Loaded {FontConfig.FamilyName} {FontConfig.StyleName}";
                    }
                }
            }

            // Regenerate immediately for all effects changes. Generation is fast (8-30ms)
            // so debouncing just adds perceived lag. DO NOT add a debounce here — it was
            // originally 300ms to batch color picker R/G/B changes, but colors are now single
            // hex strings (one event per pick). If slider drag ever causes excessive regeneration,
            // consider a small debounce (10-50ms) on slider properties only.
            RequestAutoRegenerate();
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
            var fillRgb = EffectsViewModel.ParseHex(Effects.FillColor);

            var request = new GenerationRequest
            {
                FontData = FontConfig.FontData,
                FontFilePath = FontConfig.FontFilePath,
                // For system fonts, pass the family name so the core library handles
                // bold/italic variant resolution (including ForceSynthetic logic).
                // For file-based fonts, SystemFontFamily stays null.
                SystemFontFamily = FontConfig.FontSourceKind == FontSourceKind.System
                    ? FontConfig.FamilyName
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
                FillColorR = fillRgb.R,
                FillColorG = fillRgb.G,
                FillColorB = fillRgb.B,
                FillColorA = 255,
                AdvanceAdjustX = Effects.AdvanceAdjustX,
                Gamma = Effects.Gamma,
                Bold = Effects.Bold,
                Italic = Effects.Italic,
                ForceSyntheticBold = Effects.ForceSyntheticBold,
                ForceSyntheticItalic = Effects.ForceSyntheticItalic,
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
                ShadowBlurKernelSize = Effects.ShadowBlurKernelSize,
                ShadowBlurPasses = Effects.ShadowBlurPasses,
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
                GradientOffset = Effects.GradientOffset,
                GradientScale = Effects.GradientScale,
                GradientCyclic = Effects.GradientCyclic,
                ChannelPackingEnabled = Effects.ChannelPackingEnabled,
                SdfEnabled = Effects.SdfEnabled,
                SdfSpread = Effects.SdfSpread,
                ColorFontEnabled = Effects.ColorFontEnabled,
                PackingAlgorithmIndex = AtlasConfig.PackingAlgorithmIndex,
                FaceIndex = FontConfig.FaceIndex,
                VariationAxisValues = Effects.VariationAxisValues.Count > 0
                    ? new Dictionary<string, float>(Effects.VariationAxisValues)
                    : null,
                FallbackCharacter = Effects.FallbackCharacter,
                Backend = FontConfig.SelectedBackend
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

        // Derive the default extension from the current project path so re-saving a
        // .hiero project defaults to .hiero. Falls back to .bmfc when there is no
        // current path or its extension is not a recognized config format.
        var defaultExt = GetDefaultProjectExtension();
        var initialDir = _sessionService.State.LastOutputDir;
        using var dialog = new NativeFileDialog()
            .SaveFile()
            .AddFilter("KernSmith Projects", "bmfc,hiero");
        var result = dialog.Open(out string? savePath, initialDir, $"myproject.{defaultExt}");
        if (result == DialogResult.Okay && savePath != null)
        {
            // Normalize a typed path with no extension so the lossy-.hiero check and the
            // writer both receive the intended (default) extension.
            if (!Path.HasExtension(savePath))
                savePath += "." + GetDefaultProjectExtension();
            DoSaveProject(savePath);
        }
    }

    /// <summary>
    /// Returns the default project file extension (without a dot) for new saves, based on
    /// the current project path. Falls back to <c>bmfc</c> when the path is null or its
    /// extension is neither <c>.bmfc</c> nor <c>.hiero</c>.
    /// </summary>
    private string GetDefaultProjectExtension()
    {
        var currentPath = _projectService.CurrentProjectPath;
        if (currentPath == null)
            return "bmfc";

        var ext = Path.GetExtension(currentPath).TrimStart('.').ToLowerInvariant();
        return ext is "bmfc" or "hiero" ? ext : "bmfc";
    }

    private void DoSaveProject(string path)
    {
        // The Hiero format cannot store some KernSmith-specific settings. Warn and confirm
        // before writing so the user does not silently lose configuration.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".hiero")
        {
            var lost = GetLossyHieroSettings();
            if (lost.Count > 0)
            {
                var message =
                    "These settings are not supported by the Hiero format and will not be saved:\n- "
                    + string.Join("\n- ", lost)
                    + "\nContinue saving?";
                ErrorDialog.Confirm(
                    "Lossy Save Warning",
                    message,
                    "Save Anyway",
                    () => WriteProject(path),
                    () => StatusBar.StatusText = "Save canceled");
                return;
            }
        }

        WriteProject(path);
    }

    /// <summary>
    /// Returns the KernSmith-specific settings that would be lost when saving to the Hiero
    /// format, given the current UI state.
    /// </summary>
    private List<string> GetLossyHieroSettings()
    {
        var lost = new List<string>();
        if (Effects.ChannelPackingEnabled)
            lost.Add("Channel packing");
        // Hiero cannot represent variable-font axes at all. The UI (SyncVariationAxesToEffects)
        // pre-populates VariationAxisValues with each axis's DefaultValue on font load, so only
        // flag when at least one value deviates from its axis DefaultValue.
        if (Effects.HasVariationAxes && HasNonDefaultVariationAxes())
            lost.Add("Variable font axes");
        if (Effects.SuperSampleLevel > 1)
            lost.Add("Super sampling level");
        if (Effects.ColorFontEnabled)
            lost.Add("Color font glyphs");
        // HieroConfigWriter never serializes glyph spacing; the reader restores the
        // default (1,1) on reload, so only a non-default spacing is actually lost.
        if (AtlasConfig.SpacingH != 1 || AtlasConfig.SpacingV != 1)
            lost.Add("Glyph spacing");
        // The writer emits a fixed gradient offset/scale and has no angle field, so a
        // non-default angle (default 90) on an enabled gradient is dropped.
        if (Effects.GradientEnabled && Effects.GradientAngle != 90)
            lost.Add("Gradient angle");
        // Hinting is never serialized; only a deviation when disabled (default on).
        if (!Effects.Hinting)
            lost.Add("Hinting");
        // HardShadow has no Hiero equivalent (writer forces blur kernel size 0).
        if (Effects.HardShadow)
            lost.Add("Hard shadow");
        // HieroConfigWriter writes font.bold/font.italic but has no notion of forcing
        // synthetic styling, so the forced-synthetic intent is dropped on reload.
        if (Effects.ForceSyntheticBold || Effects.ForceSyntheticItalic)
            lost.Add("Forced synthetic bold/italic");
        // The .hiero format is always flat text; XML/Binary descriptor output is lost.
        if (AtlasConfig.DescriptorFormat != OutputFormat.Text)
            lost.Add("Descriptor format (XML/Binary)");
        // HieroConfigWriter never emits autofit; the reader restores the default (false),
        // so the UI default (true) is lost even on a default project.
        if (AtlasConfig.AutofitTexture)
            lost.Add("Autofit texture");
        // Hiero has no kerning toggle; the default (enabled) round-trips, so only flag
        // when kerning has been explicitly disabled.
        if (!AtlasConfig.IncludeKerning)
            lost.Add("Kerning (disabled)");
        return lost;
    }

    /// <summary>
    /// Returns true when any loaded variation axis value deviates from its axis
    /// <see cref="Font.Tables.VariationAxis.DefaultValue"/>. Axes whose default cannot be
    /// resolved are treated conservatively as non-default deviations.
    /// </summary>
    private bool HasNonDefaultVariationAxes()
    {
        if (Effects.VariationAxisValues.Count == 0)
            return false;

        var axes = FontConfig.LoadedVariationAxes;
        foreach (var (tag, value) in Effects.VariationAxisValues)
        {
            var axis = axes?.FirstOrDefault(a => a.Tag == tag);
            // Unknown axis default: treat as a deviation to stay conservative.
            if (axis == null || value != axis.DefaultValue)
                return true;
        }
        return false;
    }

    private void WriteProject(string path)
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
    /// Opens a file browser dialog to select and load a .bmfc or .hiero project file.
    /// </summary>
    public void LoadProject()
    {
        using var dialog = new NativeFileDialog()
            .SelectFile()
            .AddFilter("KernSmith Projects", "bmfc,hiero")
            .AddFilter("All Files", "*");
        var result = dialog.Open(out string? path);
        if (result == DialogResult.Okay && path != null)
            LoadProjectFromPath(path);
    }

    /// <summary>
    /// Loads a .bmfc or .hiero project file from the given path, populating all ViewModels.
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
    /// Debounced auto-regeneration with 10ms delay. Originally 300ms to batch color picker
    /// R/G/B changes, but colors are now single hex strings. Kept as a minimal coalescing
    /// window for FontSize/AtlasConfig changes that may fire multiple events in sequence.
    /// Consider removing entirely if generation stays fast (8-30ms).
    /// </summary>
    public void RequestAutoRegenerateDebounced()
    {
        if (!AutoRegenerate || !FontConfig.IsFontLoaded) return;

        _autoRegenCts?.Cancel();
        _autoRegenCts = new CancellationTokenSource();
        var token = _autoRegenCts.Token;

        Task.Delay(10, token).ContinueWith(async _ =>
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
