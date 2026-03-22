using KernSmith.Ui.ViewModels;

namespace KernSmith.Ui.Services;

/// <summary>
/// Handles saving and loading .bmfc project files using the KernSmith library's
/// <see cref="BmfcConfig"/>, <see cref="BmfcConfigReader"/>, and <see cref="BmfcConfigWriter"/>.
/// </summary>
public class ProjectService
{
    public string? CurrentProjectPath { get; private set; }

    /// <summary>
    /// Saves the current UI state to a .bmfc file at the specified path.
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

        BmfcConfigWriter.WriteToFile(config, path);
        CurrentProjectPath = path;
    }

    /// <summary>
    /// Loads a .bmfc file and populates the provided ViewModels.
    /// </summary>
    public void LoadProject(
        string path,
        FontConfigViewModel fontConfig,
        AtlasConfigViewModel atlasConfig,
        EffectsViewModel effects,
        CharacterGridViewModel characterGrid)
    {
        var config = BmfcConfigReader.Read(path);
        var options = config.Options;

        // Load font source
        if (!string.IsNullOrEmpty(config.FontFile) && File.Exists(config.FontFile))
        {
            fontConfig.LoadFromFile(config.FontFile);
        }

        // Font settings
        fontConfig.FontSize = options.Size;

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
        effects.AntiAlias = options.AntiAlias != AntiAliasMode.None;
        effects.Hinting = options.EnableHinting;
        effects.SuperSampleLevel = options.SuperSampleLevel;
        effects.OutlineEnabled = options.Outline > 0;
        effects.OutlineWidth = options.Outline > 0 ? options.Outline : 1;
        effects.ShadowEnabled = options.ShadowOffsetX != 0 || options.ShadowOffsetY != 0 || options.ShadowBlur != 0;
        effects.ShadowOffsetX = options.ShadowOffsetX;
        effects.ShadowOffsetY = options.ShadowOffsetY;
        effects.ShadowBlur = options.ShadowBlur;
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
            AntiAlias = effects.AntiAlias ? AntiAliasMode.Grayscale : AntiAliasMode.None,
            EnableHinting = effects.Hinting,
            SuperSampleLevel = effects.SuperSampleLevel,
            Outline = effects.OutlineEnabled ? effects.OutlineWidth : 0,
            ShadowOffsetX = effects.ShadowEnabled ? effects.ShadowOffsetX : 0,
            ShadowOffsetY = effects.ShadowEnabled ? effects.ShadowOffsetY : 0,
            ShadowBlur = effects.ShadowEnabled ? effects.ShadowBlur : 0,
            Sdf = effects.SdfEnabled,
            ColorFont = effects.ColorFontEnabled
        };

        return options;
    }
}
