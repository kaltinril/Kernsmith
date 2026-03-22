using KernSmith.Cli.Utilities;

namespace KernSmith.Cli.Config;

/// <summary>
/// Reads .bmfc configuration files into <see cref="CliOptions"/>.
/// Delegates to the library-level <see cref="BmfcConfigReader"/> and maps the result.
/// </summary>
internal static class BmfcParser
{
    /// <summary>
    /// Reads a .bmfc configuration file and returns the equivalent CLI options.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the .bmfc file.</param>
    /// <returns>A <see cref="CliOptions"/> populated from the configuration file.</returns>
    public static CliOptions Parse(string filePath)
    {
        var config = BmfcConfigReader.Read(filePath);
        return MapToCliOptions(config);
    }

    /// <summary>
    /// Maps a library-level <see cref="BmfcConfig"/> to CLI-level <see cref="CliOptions"/>.
    /// </summary>
    /// <param name="config">The parsed .bmfc configuration.</param>
    /// <returns>A <see cref="CliOptions"/> with all fields mapped from the config.</returns>
    private static CliOptions MapToCliOptions(BmfcConfig config)
    {
        var gen = config.Options;
        var options = new CliOptions
        {
            FontPath = config.FontFile,
            SystemFontName = config.FontName,
            OutputPath = config.OutputPath,
            OutputFormat = config.OutputFormat,

            // Rendering
            Size = gen.Size,
            Dpi = gen.Dpi,
            AntiAlias = gen.AntiAlias,
            Sdf = gen.Sdf,
            Bold = gen.Bold,
            Italic = gen.Italic,

            // Atlas
            Padding = gen.Padding,
            Spacing = gen.Spacing,
            PackingAlgorithm = gen.PackingAlgorithm,
            ChannelPacking = gen.ChannelPacking,
            AutofitTexture = gen.AutofitTexture,
            MaxTextureWidth = gen.MaxTextureWidth,
            MaxTextureHeight = gen.MaxTextureHeight,

            // Effects
            Outline = gen.Outline,
            GradientAngle = gen.GradientAngle,
            GradientMidpoint = gen.GradientMidpoint,
            ShadowOffsetX = gen.ShadowOffsetX,
            ShadowOffsetY = gen.ShadowOffsetY,
            ShadowBlur = gen.ShadowBlur,

            // Kerning
            Kerning = gen.Kerning,

            // Rendering extras
            SuperSampleLevel = gen.SuperSampleLevel,
            FallbackCharacter = gen.FallbackCharacter,
            EnableHinting = gen.EnableHinting,
            EqualizeCellHeights = gen.EqualizeCellHeights,
            ForceOffsetsToZero = gen.ForceOffsetsToZero,
            HeightPercent = gen.HeightPercent,
            MatchCharHeight = gen.MatchCharHeight,
            ColorFont = gen.ColorFont,
            ColorPaletteIndex = gen.ColorPaletteIndex,
            FaceIndex = gen.FaceIndex,
            PowerOfTwo = gen.PowerOfTwo,
        };

        // Map TextureFormat enum back to string
        options.TextureFormat = gen.TextureFormat switch
        {
            KernSmith.TextureFormat.Tga => "tga",
            KernSmith.TextureFormat.Dds => "dds",
            _ => null // null means default (png) -- CLI treats null as "not explicitly set"
        };

        // Map color bytes back to hex strings for CLI
        if (gen.GradientStartR.HasValue && gen.GradientEndR.HasValue)
        {
            options.GradientTop = FormatColor(gen.GradientStartR.Value, gen.GradientStartG.GetValueOrDefault(), gen.GradientStartB.GetValueOrDefault());
            options.GradientBottom = FormatColor(gen.GradientEndR.Value, gen.GradientEndG.GetValueOrDefault(), gen.GradientEndB.GetValueOrDefault());
        }

        if (gen.Outline > 0 && (gen.OutlineR != 0 || gen.OutlineG != 0 || gen.OutlineB != 0))
            options.OutlineColor = FormatColor(gen.OutlineR, gen.OutlineG, gen.OutlineB);

        if (gen.ShadowR != 0 || gen.ShadowG != 0 || gen.ShadowB != 0)
            options.ShadowColor = FormatColor(gen.ShadowR, gen.ShadowG, gen.ShadowB);

        // Map character set: extract ranges from the CharacterSet for CLI compatibility
        var codepoints = gen.Characters.GetCodepoints().OrderBy(c => c).ToArray();
        if (codepoints.Length > 0)
        {
            options.CharsetPreset = null; // Explicit chars override preset
            options.UnicodeRanges = ExtractRanges(codepoints);
        }

        // Map variation axes
        if (gen.VariationAxes is { Count: > 0 })
            options.VariationAxes = new Dictionary<string, float>(gen.VariationAxes);

        return options;
    }

    /// <summary>
    /// Groups sorted codepoints into contiguous Unicode ranges.
    /// </summary>
    /// <param name="sortedCodepoints">Codepoints in ascending order.</param>
    /// <returns>A list of (Start, End) inclusive ranges.</returns>
    private static List<(int Start, int End)> ExtractRanges(int[] sortedCodepoints)
    {
        var ranges = new List<(int Start, int End)>();
        if (sortedCodepoints.Length == 0)
            return ranges;

        int start = sortedCodepoints[0], end = sortedCodepoints[0];
        for (int i = 1; i < sortedCodepoints.Length; i++)
        {
            if (sortedCodepoints[i] == end + 1)
            {
                end = sortedCodepoints[i];
            }
            else
            {
                ranges.Add((start, end));
                start = end = sortedCodepoints[i];
            }
        }
        ranges.Add((start, end));
        return ranges;
    }

    /// <summary>
    /// Formats RGB bytes as an uppercase six-character hex string (e.g., "FF0000").
    /// </summary>
    /// <param name="r">Red channel value.</param>
    /// <param name="g">Green channel value.</param>
    /// <param name="b">Blue channel value.</param>
    /// <returns>A six-character hex color string without a leading hash.</returns>
    private static string FormatColor(byte r, byte g, byte b) => $"{r:X2}{g:X2}{b:X2}";
}
