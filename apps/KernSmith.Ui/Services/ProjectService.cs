using KernSmith.Rasterizer;
using KernSmith.Ui.ViewModels;

namespace KernSmith.Ui.Services;

/// <summary>
/// Handles saving and loading project files (<c>.bmfc</c> and <c>.hiero</c>) using the
/// KernSmith library's <see cref="BmfcConfig"/> and <see cref="ConfigFormatFactory"/>.
/// Loading auto-detects the format by inspecting the file content (the extension is used only
/// as a fallback when the content is inconclusive); saving selects the format from the file extension.
/// </summary>
public class ProjectService
{
    public string? CurrentProjectPath { get; private set; }

    /// <summary>
    /// The font file the last loaded project names but that does not exist, or null.
    /// </summary>
    public string? MissingFontFile { get; private set; }

    /// <summary>
    /// Saves the current UI state to a .bmfc or .hiero file at the specified path.
    /// The output format is auto-detected from the file extension.
    /// </summary>
    public void SaveProject(
        string path,
        FontConfigViewModel fontConfig,
        AtlasConfigViewModel atlasConfig,
        EffectsViewModel effects,
        CharacterGridViewModel characterGrid)
    {
        var options = BuildOptions(fontConfig, atlasConfig, effects, characterGrid);

        var config = BmfcConfig.FromOptions(
            options,
            fontFile: fontConfig.FontFilePath,
            fontName: fontConfig.FontSourceKind == Models.FontSourceKind.System
                ? fontConfig.FontSourceDescription?.Replace(" (System)", "")
                : null,
            outputFormat: atlasConfig.DescriptorFormat);

        ConfigFormatFactory.WriteConfig(config, path);
        CurrentProjectPath = path;
    }

    /// <summary>
    /// Loads a .bmfc or .hiero file and populates the provided ViewModels.
    /// The format is auto-detected by inspecting the file content; the extension is used only
    /// as a fallback when the content is inconclusive.
    /// </summary>
    public void LoadProject(
        string path,
        FontConfigViewModel fontConfig,
        AtlasConfigViewModel atlasConfig,
        EffectsViewModel effects,
        CharacterGridViewModel characterGrid)
    {
        var config = ConfigFormatFactory.ReadConfig(path);
        var options = config.Options;

        // Load font source. A font file that is gone unloads the current font: keeping it would
        // generate (and re-save) the project with a font it does not name.
        MissingFontFile = null;
        if (!string.IsNullOrEmpty(config.FontFile))
        {
            if (File.Exists(config.FontFile))
            {
                fontConfig.LoadFromFile(config.FontFile);
            }
            else
            {
                MissingFontFile = config.FontFile;
                fontConfig.Unload();
            }
        }

        // Font settings
        fontConfig.FontSize = (int)Math.Round(options.Size);
        if (options.FaceIndex > 0 && fontConfig.IsFontLoaded)
            fontConfig.ReloadWithFaceIndex(options.FaceIndex);

        // A backend this build lacks keeps the current selection rather than failing the load.
        if (RasterizerFactory.IsRegistered(options.Backend))
            fontConfig.SelectedBackend = options.Backend;

        // Atlas config
        atlasConfig.MaxWidth = options.MaxTextureWidth;
        atlasConfig.MaxHeight = options.MaxTextureHeight;
        atlasConfig.PowerOfTwo = options.PowerOfTwo;
        atlasConfig.AutofitTexture = options.AutofitTexture;
        atlasConfig.PaddingUp = options.Padding.Up;
        atlasConfig.PaddingRight = options.Padding.Right;
        atlasConfig.PaddingDown = options.Padding.Down;
        atlasConfig.PaddingLeft = options.Padding.Left;
        atlasConfig.SpacingH = options.Spacing.Horizontal;
        atlasConfig.SpacingV = options.Spacing.Vertical;
        atlasConfig.IncludeKerning = options.Kerning;
        atlasConfig.PackingAlgorithmIndex = (int)options.PackingAlgorithm;
        atlasConfig.DescriptorFormat = config.OutputFormat;

        // Effects
        effects.FillColor = EffectsViewModel.ToHex(options.FillColorR, options.FillColorG, options.FillColorB);
        effects.AdvanceAdjustX = options.AdvanceAdjustX;
        // The UI falls back to '?' when a project names no fallback (SaveProject omits it then).
        effects.FallbackCharacter = options.FallbackCharacter?.ToString() ?? "?";
        effects.Gamma = options.Gamma;
        effects.Bold = options.Bold;
        effects.Italic = options.Italic;
        effects.ForceSyntheticBold = options.ForceSyntheticBold;
        effects.ForceSyntheticItalic = options.ForceSyntheticItalic;
        effects.AntiAlias = options.AntiAlias;
        effects.LcdFilter = options.LcdFilter;
        effects.LcdSubpixelOrder = options.LcdSubpixelOrder;
        effects.WidthPercent = options.WidthPercent;
        effects.ItalicAngle = options.ItalicAngle;
        effects.Hinting = options.EnableHinting;
        effects.SuperSampleLevel = options.SuperSampleLevel;
        effects.OutlineEnabled = options.Outline > 0;
        effects.OutlineWidth = options.Outline > 0 ? options.Outline : 1;
        effects.OutlineColor = EffectsViewModel.ToHex(options.OutlineR, options.OutlineG, options.OutlineB);
        effects.OutlineShape = options.OutlineShape;
        effects.OutlineMode = options.OutlineMode;
        effects.ShadowEnabled = options.ShadowOffsetX != 0 || options.ShadowOffsetY != 0 || options.ShadowBlur != 0;
        effects.ShadowOffsetX = options.ShadowOffsetX;
        effects.ShadowOffsetY = options.ShadowOffsetY;
        effects.ShadowBlur = options.ShadowBlur;
        effects.ShadowBlurKernelSize = options.ShadowBlurKernelSize;
        effects.ShadowBlurPasses = options.ShadowBlurPasses;
        effects.ShadowColor = EffectsViewModel.ToHex(options.ShadowR, options.ShadowG, options.ShadowB);
        effects.ShadowOpacity = (int)Math.Round(options.ShadowOpacity * 100);
        effects.HardShadow = options.HardShadow;
        effects.GradientEnabled = options.GradientStartR.HasValue && options.GradientEndR.HasValue;
        effects.GradientStartColor = EffectsViewModel.ToHex(
            options.GradientStartR ?? 255, options.GradientStartG ?? 255, options.GradientStartB ?? 255);
        effects.GradientEndColor = EffectsViewModel.ToHex(
            options.GradientEndR ?? 0, options.GradientEndG ?? 0, options.GradientEndB ?? 0);
        effects.GradientAngle = (int)options.GradientAngle;
        effects.GradientOffset = options.GradientOffset;
        effects.GradientScale = options.GradientScale;
        effects.GradientCyclic = options.GradientCyclic;
        // Channel packing and per-channel content are separate features: the checkbox tracks
        // ChannelPacking, while Channels is preserved for round-tripping (no editor for it yet).
        effects.ChannelPackingEnabled = options.ChannelPacking;
        effects.Channels = options.Channels;
        effects.SdfMode = options.EffectiveSdfMode;
        effects.SdfSpread = options.SdfSpread;
        effects.SdfScale = options.SdfScale;
        effects.ColorFontEnabled = options.ColorFont;
        effects.ColorPaletteIndex = options.ColorPaletteIndex;
        effects.HasColorGlyphs = fontConfig.HasColorGlyphs;
        effects.LoadVariationAxes(fontConfig.LoadedVariationAxes, options.VariationAxes);

        // Character set
        var codepoints = options.Characters.GetCodepoints().ToList();
        if (codepoints.Count > 0)
        {
            characterGrid.Clear();
            characterGrid.SelectAll(codepoints);
        }

        CurrentProjectPath = path;
    }

    /// <summary>
    /// Clears the current project path (for "new project" scenarios).
    /// </summary>
    public void ClearCurrentProject()
    {
        CurrentProjectPath = null;
    }

    private static FontGeneratorOptions BuildOptions(
        FontConfigViewModel fontConfig,
        AtlasConfigViewModel atlasConfig,
        EffectsViewModel effects,
        CharacterGridViewModel characterGrid)
    {
        var outlineRgb = EffectsViewModel.ParseHex(effects.OutlineColor);
        var shadowRgb = EffectsViewModel.ParseHex(effects.ShadowColor);
        var gradStartRgb = EffectsViewModel.ParseHex(effects.GradientStartColor);
        var gradEndRgb = EffectsViewModel.ParseHex(effects.GradientEndColor);
        var fillRgb = EffectsViewModel.ParseHex(effects.FillColor);

        var options = new FontGeneratorOptions
        {
            Size = fontConfig.FontSize,
            Characters = characterGrid.ToCharacterSet(),
            MaxTextureWidth = atlasConfig.MaxWidth,
            MaxTextureHeight = atlasConfig.MaxHeight,
            PowerOfTwo = atlasConfig.PowerOfTwo,
            AutofitTexture = atlasConfig.AutofitTexture,
            Padding = new Padding(atlasConfig.PaddingUp, atlasConfig.PaddingRight, atlasConfig.PaddingDown, atlasConfig.PaddingLeft),
            Spacing = new Spacing(atlasConfig.SpacingH, atlasConfig.SpacingV),
            Kerning = atlasConfig.IncludeKerning,
            PackingAlgorithm = (PackingAlgorithm)atlasConfig.PackingAlgorithmIndex,
            FillColorR = fillRgb.R,
            FillColorG = fillRgb.G,
            FillColorB = fillRgb.B,
            FillColorA = 255,
            AdvanceAdjustX = effects.AdvanceAdjustX,
            FallbackCharacter = !string.IsNullOrEmpty(effects.FallbackCharacter) && effects.FallbackCharacter != "?"
                ? effects.FallbackCharacter[0]
                : null,
            Gamma = effects.Gamma,
            Bold = effects.Bold,
            Italic = effects.Italic,
            ForceSyntheticBold = effects.ForceSyntheticBold,
            ForceSyntheticItalic = effects.ForceSyntheticItalic,
            AntiAlias = effects.AntiAlias,
            LcdFilter = effects.LcdFilter,
            LcdSubpixelOrder = effects.LcdSubpixelOrder,
            // Same gating as generation, so a saved project never holds a width, angle or
            // vector outline its rasterizer would reject.
            WidthPercent = MainViewModel.EffectiveWidthPercent(fontConfig, effects),
            ItalicAngle = MainViewModel.EffectiveItalicAngle(fontConfig, effects),
            EnableHinting = effects.Hinting,
            SuperSampleLevel = effects.SuperSampleLevel,
            Outline = effects.OutlineEnabled ? effects.OutlineWidth : 0,
            OutlineShape = effects.OutlineShape,
            OutlineMode = MainViewModel.EffectiveOutlineMode(fontConfig, effects),
            OutlineR = outlineRgb.R,
            OutlineG = outlineRgb.G,
            OutlineB = outlineRgb.B,
            ShadowOffsetX = effects.ShadowEnabled ? effects.ShadowOffsetX : 0,
            ShadowOffsetY = effects.ShadowEnabled ? effects.ShadowOffsetY : 0,
            ShadowBlur = effects.ShadowEnabled ? effects.ShadowBlur : 0,
            ShadowBlurKernelSize = effects.ShadowEnabled ? effects.ShadowBlurKernelSize : 0,
            ShadowBlurPasses = effects.ShadowEnabled ? effects.ShadowBlurPasses : 1,
            ShadowR = shadowRgb.R,
            ShadowG = shadowRgb.G,
            ShadowB = shadowRgb.B,
            ShadowOpacity = effects.ShadowOpacity / 100f,
            HardShadow = effects.HardShadow,
            GradientStartR = effects.GradientEnabled ? gradStartRgb.R : null,
            GradientStartG = effects.GradientEnabled ? gradStartRgb.G : null,
            GradientStartB = effects.GradientEnabled ? gradStartRgb.B : null,
            GradientEndR = effects.GradientEnabled ? gradEndRgb.R : null,
            GradientEndG = effects.GradientEnabled ? gradEndRgb.G : null,
            GradientEndB = effects.GradientEnabled ? gradEndRgb.B : null,
            GradientAngle = effects.GradientAngle,
            GradientOffset = effects.GradientEnabled ? effects.GradientOffset : 0f,
            GradientScale = effects.GradientEnabled ? effects.GradientScale : 1f,
            GradientCyclic = effects.GradientEnabled && effects.GradientCyclic,
            FaceIndex = fontConfig.FaceIndex,
            // Single-channel SDF stays on the BMFont-style useSdf key; only MSDF/MTSDF need sdfMode.
            Sdf = effects.SdfMode == SdfMode.Sdf,
            SdfMode = effects.SdfMode is SdfMode.Msdf or SdfMode.Mtsdf ? effects.SdfMode : SdfMode.None,
            SdfSpread = effects.SdfSpread,
            SdfScale = effects.SdfScale,
            ColorFont = effects.ColorFontEnabled,
            ColorPaletteIndex = effects.ColorPaletteIndex,
            VariationAxes = effects.VariationAxisValues.Count > 0
                ? new Dictionary<string, float>(effects.VariationAxisValues)
                : null,
            Backend = fontConfig.SelectedBackend,
            ChannelPacking = effects.ChannelPackingEnabled,
            Channels = effects.Channels
        };

        return options;
    }
}
