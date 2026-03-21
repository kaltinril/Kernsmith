using KernSmith.Cli.Utilities;

namespace KernSmith.Cli.Config;

/// <summary>
/// Serializes <see cref="CliOptions"/> to .bmfc format.
/// Delegates to the library-level <see cref="BmfcConfigWriter"/>.
/// </summary>
internal static class BmfcWriter
{
    public static void Write(CliOptions options, string filePath)
    {
        var config = MapToBmfcConfig(options);
        BmfcConfigWriter.WriteToFile(config, filePath);
    }

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
    private static FontGeneratorOptions BuildFontGeneratorOptions(CliOptions options)
    {
        var genOptions = new FontGeneratorOptions
        {
            Size = options.Size ?? 32,
            Bold = options.Bold,
            Italic = options.Italic,
            AntiAlias = options.AntiAlias,
            Padding = options.Padding,
            Spacing = options.Spacing,
            PackingAlgorithm = options.PackingAlgorithm,
            Kerning = options.Kerning ?? true,
            Outline = options.Outline,
            Sdf = options.Sdf,
            PowerOfTwo = options.PowerOfTwo ?? true,
            Dpi = options.Dpi,
            FaceIndex = options.FaceIndex,
            ChannelPacking = options.ChannelPacking,
            SuperSampleLevel = options.SuperSampleLevel,
            FallbackCharacter = options.FallbackCharacter,
            EnableHinting = options.EnableHinting ?? true,
            AutofitTexture = options.AutofitTexture,
            EqualizeCellHeights = options.EqualizeCellHeights,
            ForceOffsetsToZero = options.ForceOffsetsToZero,
            HeightPercent = options.HeightPercent,
            MatchCharHeight = options.MatchCharHeight,
            ColorFont = options.ColorFont,
            ColorPaletteIndex = options.ColorPaletteIndex,
            GradientAngle = options.GradientAngle,
            GradientMidpoint = options.GradientMidpoint,
            ShadowOffsetX = options.ShadowOffsetX,
            ShadowOffsetY = options.ShadowOffsetY,
            ShadowBlur = options.ShadowBlur,
        };

        // Set MaxTextureSize (sets both width and height)
        genOptions.MaxTextureSize = options.MaxTextureSize;
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

        // Build character set from CLI options
        if (options.UnicodeRanges.Count > 0)
        {
            genOptions.Characters = CharacterSet.FromRanges(options.UnicodeRanges.ToArray());
        }

        if (options.VariationAxes.Count > 0)
            genOptions.VariationAxes = new Dictionary<string, float>(options.VariationAxes);

        return genOptions;
    }
}
