using System.Globalization;
using System.Text;

namespace KernSmith;

/// <summary>
/// Serializes <see cref="BmfcConfig"/> to standard BMFont .bmfc flat key=value format.
/// </summary>
public static class BmfcConfigWriter
{
    /// <summary>
    /// Writes a <see cref="BmfcConfig"/> as a .bmfc format string.
    /// </summary>
    /// <param name="config">The configuration to serialize.</param>
    /// <returns>The .bmfc file content as a string.</returns>
    public static string Write(BmfcConfig config)
    {
        return WriteCore(config, relativeBasePath: null);
    }

    /// <summary>
    /// Writes a <see cref="BmfcConfig"/> to a .bmfc file on disk.
    /// Font file and output paths are written as relative paths when possible.
    /// </summary>
    /// <param name="config">The configuration to serialize.</param>
    /// <param name="filePath">The destination file path.</param>
    public static void WriteToFile(BmfcConfig config, string filePath)
    {
        var content = WriteCore(config, relativeBasePath: filePath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, content);
    }

    private static string WriteCore(BmfcConfig config, string? relativeBasePath)
    {
        var options = config.Options;
        var sb = new StringBuilder();
        sb.AppendLine("# AngelCode Bitmap Font Generator configuration file");
        sb.AppendLine("fileVersion=1");
        sb.AppendLine();

        // Font settings
        sb.AppendLine("# font settings");
        sb.AppendLine($"fontName={config.FontName ?? ""}");
        sb.AppendLine($"fontFile={FormatPath(config.FontFile, relativeBasePath)}");
        // .bmfc is an integer-only format; round here since FontGeneratorOptions.Size is float.
        var fontSize = (int)Math.Round(options.Size);
        if (options.MatchCharHeight)
            fontSize = -fontSize;
        sb.AppendLine($"fontSize={fontSize}");
        sb.AppendLine($"isBold={(options.Bold ? 1 : 0)}");
        sb.AppendLine($"isItalic={(options.Italic ? 1 : 0)}");
        sb.AppendLine($"useSmoothing={(options.AntiAlias == AntiAliasMode.None ? 0 : 1)}");
        sb.AppendLine($"aa={(options.AntiAlias == AntiAliasMode.Light ? 2 : 1)}");
        sb.AppendLine($"useHinting={(options.EnableHinting ? 1 : 0)}");
        sb.AppendLine($"scaleH={options.HeightPercent}");
        sb.AppendLine($"dontIncludeKerningPairs={(options.Kerning ? 0 : 1)}");
        sb.AppendLine();

        // Character alignment
        sb.AppendLine("# character alignment");
        var pad = options.Padding;
        sb.AppendLine($"paddingUp={pad.Up}");
        sb.AppendLine($"paddingDown={pad.Down}");
        sb.AppendLine($"paddingRight={pad.Right}");
        sb.AppendLine($"paddingLeft={pad.Left}");
        var spc = options.Spacing;
        sb.AppendLine($"spacingHoriz={spc.Horizontal}");
        sb.AppendLine($"spacingVert={spc.Vertical}");
        sb.AppendLine($"useFixedHeight={(options.EqualizeCellHeights ? 1 : 0)}");
        sb.AppendLine($"forceZero={(options.ForceOffsetsToZero ? 1 : 0)}");
        sb.AppendLine();

        // Output file
        sb.AppendLine("# output file");
        sb.AppendLine($"outWidth={options.MaxTextureWidth}");
        sb.AppendLine($"outHeight={options.MaxTextureHeight}");
        sb.AppendLine($"fontDescFormat={FormatOutputFormat(config.OutputFormat)}");
        sb.AppendLine($"textureFormat={FormatTextureFormat(options.TextureFormat)}");
        sb.AppendLine($"fourChnlPacked={(options.ChannelPacking ? 1 : 0)}");
        sb.AppendLine();

        // Outline
        sb.AppendLine("# outline");
        sb.AppendLine($"outlineThickness={options.Outline}");
        sb.AppendLine();

        // Selected chars
        sb.AppendLine("# selected chars");
        sb.AppendLine($"chars={FormatChars(options.Characters)}");
        sb.AppendLine();

        // Output path (if set)
        if (!string.IsNullOrEmpty(config.OutputPath))
        {
            sb.AppendLine("# output path");
            sb.AppendLine($"outputPath={FormatPath(config.OutputPath, relativeBasePath)}");
            sb.AppendLine();
        }

        // kernsmith extensions -- only include non-default values
        var extensions = new StringBuilder();
        if (options.GradientStartR.HasValue && options.GradientEndR.HasValue)
        {
            extensions.AppendLine($"gradientTop={FormatColor(options.GradientStartR.Value, options.GradientStartG.GetValueOrDefault(), options.GradientStartB.GetValueOrDefault())}");
            extensions.AppendLine($"gradientBottom={FormatColor(options.GradientEndR.Value, options.GradientEndG.GetValueOrDefault(), options.GradientEndB.GetValueOrDefault())}");
        }
        if (options.GradientAngle != 90f)
            extensions.AppendLine($"gradientAngle={options.GradientAngle.ToString(CultureInfo.InvariantCulture)}");
        if (options.GradientMidpoint != 0.5f)
            extensions.AppendLine($"gradientMidpoint={options.GradientMidpoint.ToString(CultureInfo.InvariantCulture)}");
        if (options.ShadowOffsetX != 0)
            extensions.AppendLine($"shadowOffsetX={options.ShadowOffsetX}");
        if (options.ShadowOffsetY != 0)
            extensions.AppendLine($"shadowOffsetY={options.ShadowOffsetY}");
        if (options.ShadowR != 0 || options.ShadowG != 0 || options.ShadowB != 0)
            extensions.AppendLine($"shadowColor={FormatColor(options.ShadowR, options.ShadowG, options.ShadowB)}");
        if (options.ShadowBlur != 0)
            extensions.AppendLine($"shadowBlur={options.ShadowBlur}");
        if (options.Outline > 0 && (options.OutlineR != 0 || options.OutlineG != 0 || options.OutlineB != 0))
            extensions.AppendLine($"outlineColor={FormatColor(options.OutlineR, options.OutlineG, options.OutlineB)}");
        if (options.Sdf)
            extensions.AppendLine("useSdf=1");
        if (options.SuperSampleLevel != 1)
            extensions.AppendLine($"superSample={options.SuperSampleLevel}");
        if (options.PackingAlgorithm != PackingAlgorithm.MaxRects)
            extensions.AppendLine("packer=skyline");
        // FallbackCodepoint takes precedence over FallbackCharacter
        if (options.FallbackCodepoint.HasValue)
            extensions.AppendLine($"fallbackChar={options.FallbackCodepoint.Value}");
        else if (options.FallbackCharacter.HasValue)
            extensions.AppendLine($"fallbackChar={(int)options.FallbackCharacter.Value}");
        if (options.ColorFont)
            extensions.AppendLine("colorFont=1");
        if (options.ColorPaletteIndex != 0)
            extensions.AppendLine($"colorPalette={options.ColorPaletteIndex}");
        if (options.FaceIndex != 0)
            extensions.AppendLine($"faceIndex={options.FaceIndex}");
        if (options.Dpi != 72)
            extensions.AppendLine($"dpi={options.Dpi}");
        if (!options.PowerOfTwo)
            extensions.AppendLine("powerOfTwo=0");
        if (options.AutofitTexture)
            extensions.AppendLine("autofit=1");
        if (options.Backend != RasterizerBackend.FreeType)
            extensions.AppendLine($"rasterizer={options.Backend.ToString().ToLowerInvariant()}");
        if (options.ForceSyntheticBold)
            extensions.AppendLine("forceSyntheticBold=1");
        if (options.ForceSyntheticItalic)
            extensions.AppendLine("forceSyntheticItalic=1");

        if (extensions.Length > 0)
        {
            sb.AppendLine("# kernsmith extensions");
            sb.Append(extensions);
        }

        return sb.ToString();
    }

    private static int FormatOutputFormat(OutputFormat format) => format switch
    {
        OutputFormat.Text => 0,
        OutputFormat.Xml => 1,
        OutputFormat.Binary => 2,
        _ => 0
    };

    private static string FormatTextureFormat(TextureFormat format) => format switch
    {
        TextureFormat.Png => "png",
        TextureFormat.Tga => "tga",
        TextureFormat.Dds => "dds",
        _ => "png"
    };

    private static string FormatChars(CharacterSet characters)
    {
        var codepoints = characters.GetCodepoints().ToArray();
        if (codepoints.Length == 0)
            return "32-126";

        // Sort and merge into ranges
        Array.Sort(codepoints);
        var ranges = new List<(int Start, int End)>();
        int start = codepoints[0], end = codepoints[0];

        for (int i = 1; i < codepoints.Length; i++)
        {
            if (codepoints[i] == end + 1)
            {
                end = codepoints[i];
            }
            else
            {
                ranges.Add((start, end));
                start = end = codepoints[i];
            }
        }
        ranges.Add((start, end));

        var parts = ranges.Select(r =>
            r.Start == r.End ? r.Start.ToString() : $"{r.Start}-{r.End}");
        return string.Join(",", parts);
    }

    private static string FormatPath(string? path, string? relativeBasePath)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        if (relativeBasePath == null)
            return path;

        try
        {
            var configDir = Path.GetDirectoryName(Path.GetFullPath(relativeBasePath));
            if (string.IsNullOrEmpty(configDir))
                return path;
            return Path.GetRelativePath(configDir, path);
        }
        catch
        {
            return path;
        }
    }

    private static string FormatColor(byte r, byte g, byte b)
    {
        return $"{r:X2}{g:X2}{b:X2}";
    }
}
