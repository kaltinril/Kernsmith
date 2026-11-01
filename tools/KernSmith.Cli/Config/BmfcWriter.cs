using KernSmith.Cli.Utilities;

namespace KernSmith.Cli.Config;

/// <summary>
/// Serializes <see cref="CliOptions"/> to .bmfc or .hiero format.
/// Delegates to the library-level <see cref="ConfigFormatFactory"/>, which selects the
/// serializer based on the output file extension.
/// </summary>
internal static class BmfcWriter
{
    /// <summary>
    /// Writes CLI options to a configuration file. The format (.bmfc or .hiero) is
    /// auto-detected from the file extension.
    /// </summary>
    /// <param name="options">The CLI options to serialize.</param>
    /// <param name="filePath">The output config file path (.bmfc or .hiero).</param>
    public static void Write(CliOptions options, string filePath)
    {
        var config = MapToBmfcConfig(options);
        ConfigFormatFactory.WriteConfig(config, filePath);
    }

    /// <summary>
    /// Maps CLI options to a library-level <see cref="BmfcConfig"/> for serialization.
    /// </summary>
    /// <param name="options">The CLI options to convert.</param>
    /// <returns>A <see cref="BmfcConfig"/> ready for file serialization.</returns>
    private static BmfcConfig MapToBmfcConfig(CliOptions options)
    {
        var genOptions = BuildFontGeneratorOptions(options);

        return new BmfcConfig
        {
            Options = genOptions,
            FontFile = options.FontPath,
            FontName = options.SystemFontName,
            OutputPath = options.OutputPath,
            OutputFormat = options.OutputFormat,
        };
    }

    /// <summary>
    /// Builds a <see cref="FontGeneratorOptions"/> from CLI options for serialization.
    /// This mirrors GenerateCommand.BuildGenOptions but without requiring a validated Size.
    /// </summary>
    /// <param name="options">The CLI options to convert.</param>
    /// <returns>A <see cref="FontGeneratorOptions"/> suitable for config serialization (.bmfc or .hiero).</returns>
    private static FontGeneratorOptions BuildFontGeneratorOptions(CliOptions options)
    {
        var genOptions = new FontGeneratorOptions
        {
            Size = options.Size ?? 32,
            Bold = options.Bold,
            Italic = options.Italic,
            ForceSyntheticBold = options.ForceSyntheticBold,
            ForceSyntheticItalic = options.ForceSyntheticItalic,
            AntiAlias = options.AntiAlias,
            LcdFilter = options.LcdFilter ?? KernSmith.LcdFilter.Default,
            LcdSubpixelOrder = options.LcdSubpixelOrder ?? KernSmith.LcdSubpixelOrder.Rgb,
            Padding = options.Padding ?? new Padding(0),
            Spacing = options.Spacing ?? new Spacing(1),
            PackingAlgorithm = options.PackingAlgorithm,
            Kerning = options.Kerning ?? true,
            Outline = options.Outline,
            OutlineShape = options.OutlineShape ?? KernSmith.OutlineShape.Round,
            OutlineMode = options.OutlineMode ?? KernSmith.OutlineMode.Effect,
            Sdf = options.Sdf,
            SdfMode = options.SdfMode,
            PowerOfTwo = options.PowerOfTwo ?? true,
            Dpi = options.Dpi,
            FaceIndex = options.FaceIndex,
            ChannelPacking = options.ChannelPacking,
            SuperSampleLevel = options.SuperSampleLevel,
            FallbackCharacter = options.FallbackCharacter,
            FallbackCodepoint = options.FallbackCodepoint,
            EnableHinting = options.EnableHinting ?? true,
            AutofitTexture = options.AutofitTexture,
            EqualizeCellHeights = options.EqualizeCellHeights,
            ForceOffsetsToZero = options.ForceOffsetsToZero,
            HeightPercent = options.HeightPercent,
            WidthPercent = options.WidthPercent,
            ItalicAngle = options.ItalicAngle ?? 12f,
            MatchCharHeight = options.MatchCharHeight,
            ColorFont = options.ColorFont,
            ColorPaletteIndex = options.ColorPaletteIndex,
            GradientAngle = options.GradientAngle,
            GradientMidpoint = options.GradientMidpoint,
            GradientOffset = options.GradientOffset,
            GradientScale = options.GradientScale,
            GradientCyclic = options.GradientCyclic,
            ShadowOffsetX = options.ShadowOffsetX,
            ShadowOffsetY = options.ShadowOffsetY,
            ShadowBlur = options.ShadowBlur,
            ShadowBlurKernelSize = options.ShadowBlurKernelSize,
            ShadowBlurPasses = options.ShadowBlurPasses,
            ShadowOpacity = options.ShadowOpacity,
            HardShadow = options.HardShadow,
            SdfScale = options.SdfScale,
            AdvanceAdjustX = options.AdvanceAdjustX,
            AdvanceAdjustY = options.AdvanceAdjustY,
            Backend = options.Backend,
        };

        // Phase 100 rendering options — only override defaults when explicitly set.
        if (options.SdfSpread.HasValue)
            genOptions.SdfSpread = options.SdfSpread.Value;
        if (options.Gamma.HasValue)
            genOptions.Gamma = options.Gamma.Value;

        // Set MaxTextureSize (sets both width and height)
        genOptions.MaxTextureSize = options.MaxTextureSize ?? 1024;
        // Then override individually if explicitly set
        if (options.MaxTextureWidth.HasValue)
            genOptions.MaxTextureWidth = options.MaxTextureWidth.Value;
        if (options.MaxTextureHeight.HasValue)
            genOptions.MaxTextureHeight = options.MaxTextureHeight.Value;

        // Map texture format string to enum
        if (options.TextureFormat != null)
        {
            genOptions.TextureFormat = options.TextureFormat.ToLowerInvariant() switch
            {
                "tga" => KernSmith.TextureFormat.Tga,
                "dds" => KernSmith.TextureFormat.Dds,
                _ => KernSmith.TextureFormat.Png
            };
        }

        // Map color strings to RGB bytes
        if (options.GradientTop != null && options.GradientBottom != null)
        {
            var top = ColorParser.Parse(options.GradientTop);
            var bottom = ColorParser.Parse(options.GradientBottom);
            genOptions.GradientStartR = top.R;
            genOptions.GradientStartG = top.G;
            genOptions.GradientStartB = top.B;
            genOptions.GradientEndR = bottom.R;
            genOptions.GradientEndG = bottom.G;
            genOptions.GradientEndB = bottom.B;
        }
        if (options.Outline > 0 && options.OutlineColor != null)
        {
            var oc = ColorParser.Parse(options.OutlineColor);
            genOptions.OutlineR = oc.R;
            genOptions.OutlineG = oc.G;
            genOptions.OutlineB = oc.B;
        }
        if (options.ShadowColor != null)
        {
            var sc = ColorParser.Parse(options.ShadowColor);
            genOptions.ShadowR = sc.R;
            genOptions.ShadowG = sc.G;
            genOptions.ShadowB = sc.B;
        }
        if (options.FillColor != null)
        {
            var fill = ColorParser.ParseRgba(options.FillColor);
            genOptions.FillColorR = fill.R;
            genOptions.FillColorG = fill.G;
            genOptions.FillColorB = fill.B;
            genOptions.FillColorA = fill.A;
        }

        if (options.ShadowVariant)
            genOptions.Variants = [new("shadow", AtlasVariantKind.ShadowSilhouette, options.ShadowBlur, options.HardShadow)];

        // The same union of -c/--chars/--chars-file/--range that generation renders.
        genOptions.Characters = Commands.GenerateCommand.BuildCharacterSet(options);

        if (options.VariationAxes.Count > 0)
            genOptions.VariationAxes = new Dictionary<string, float>(options.VariationAxes);

        return genOptions;
    }
}
