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

        // Load font source
        if (!string.IsNullOrEmpty(config.FontFile) && File.Exists(config.FontFile))
        {
            fontConfig.LoadFromFile(config.FontFile);
        }

        // Font settings
        fontConfig.FontSize = (int)Math.Round(options.Size);
        if (options.FaceIndex > 0 && fontConfig.IsFontLoaded)
            fontConfig.ReloadWithFaceIndex(options.FaceIndex);

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
        atlasConfig.DescriptorFormat = config.OutputFormat;

        // Effects
        effects.Bold = options.Bold;
        effects.Italic = options.Italic;
        effects.ForceSyntheticBold = options.ForceSyntheticBold;
        effects.ForceSyntheticItalic = options.ForceSyntheticItalic;
        effects.AntiAlias = options.AntiAlias != AntiAliasMode.None;
        effects.Hinting = options.EnableHinting;
        effects.SuperSampleLevel = options.SuperSampleLevel;
        effects.OutlineEnabled = options.Outline > 0;
        effects.OutlineWidth = options.Outline > 0 ? options.Outline : 1;
        effects.OutlineColor = EffectsViewModel.ToHex(options.OutlineR, options.OutlineG, options.OutlineB);
        effects.ShadowEnabled = options.ShadowOffsetX != 0 || options.ShadowOffsetY != 0 || options.ShadowBlur != 0;
        effects.ShadowOffsetX = options.ShadowOffsetX;
        effects.ShadowOffsetY = options.ShadowOffsetY;
        effects.ShadowBlur = options.ShadowBlur;
        effects.ShadowColor = EffectsViewModel.ToHex(options.ShadowR, options.ShadowG, options.ShadowB);
        effects.ShadowOpacity = (int)(options.ShadowOpacity * 100);
        effects.HardShadow = options.HardShadow;
        effects.GradientEnabled = options.GradientStartR.HasValue && options.GradientEndR.HasValue;
        effects.GradientStartColor = EffectsViewModel.ToHex(
            options.GradientStartR ?? 255, options.GradientStartG ?? 255, options.GradientStartB ?? 255);
        effects.GradientEndColor = EffectsViewModel.ToHex(
            options.GradientEndR ?? 0, options.GradientEndG ?? 0, options.GradientEndB ?? 0);
        effects.GradientAngle = (int)options.GradientAngle;
        effects.ChannelPackingEnabled = options.Channels != null;
        effects.SdfEnabled = options.Sdf;
        effects.ColorFontEnabled = options.ColorFont;

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
            Bold = effects.Bold,
            Italic = effects.Italic,
            ForceSyntheticBold = effects.ForceSyntheticBold,
            ForceSyntheticItalic = effects.ForceSyntheticItalic,
            AntiAlias = effects.AntiAlias ? AntiAliasMode.Grayscale : AntiAliasMode.None,
            EnableHinting = effects.Hinting,
            SuperSampleLevel = effects.SuperSampleLevel,
            Outline = effects.OutlineEnabled ? effects.OutlineWidth : 0,
            OutlineR = outlineRgb.R,
            OutlineG = outlineRgb.G,
            OutlineB = outlineRgb.B,
            ShadowOffsetX = effects.ShadowEnabled ? effects.ShadowOffsetX : 0,
            ShadowOffsetY = effects.ShadowEnabled ? effects.ShadowOffsetY : 0,
            ShadowBlur = effects.ShadowEnabled ? effects.ShadowBlur : 0,
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
            FaceIndex = fontConfig.FaceIndex,
            Sdf = effects.SdfEnabled,
            ColorFont = effects.ColorFontEnabled
        };

        return options;
    }
}
