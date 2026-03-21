using System.Globalization;
using System.Text;

namespace Bmfontier.Cli.Config;

/// <summary>
/// Serializes <see cref="CliOptions"/> to standard BMFont .bmfc flat key=value format.
/// </summary>
internal static class BmfcWriter
{
    public static void Write(CliOptions options, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AngelCode Bitmap Font Generator configuration file");
        sb.AppendLine("fileVersion=1");
        sb.AppendLine();

        // Font settings
        sb.AppendLine("# font settings");
        sb.AppendLine($"fontName={options.SystemFontName ?? ""}");
        sb.AppendLine($"fontFile={FormatFontFilePath(options, filePath)}");
        var fontSize = options.Size ?? 32;
        if (options.MatchCharHeight)
            fontSize = -fontSize;
        sb.AppendLine($"fontSize={fontSize}");
        sb.AppendLine($"isBold={(options.Bold ? 1 : 0)}");
        sb.AppendLine($"isItalic={(options.Italic ? 1 : 0)}");
        sb.AppendLine($"useSmoothing={(options.AntiAlias == AntiAliasMode.None ? 0 : 1)}");
        sb.AppendLine($"aa={(options.AntiAlias == AntiAliasMode.Light ? 2 : 1)}");
        sb.AppendLine($"useHinting={((options.EnableHinting ?? true) ? 1 : 0)}");
        sb.AppendLine($"scaleH={options.HeightPercent}");
        sb.AppendLine($"dontIncludeKerningPairs={((options.Kerning ?? true) ? 0 : 1)}");
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
        sb.AppendLine($"outWidth={options.MaxTextureWidth ?? options.MaxTextureSize}");
        sb.AppendLine($"outHeight={options.MaxTextureHeight ?? options.MaxTextureSize}");
        sb.AppendLine($"fontDescFormat={FormatOutputFormat(options.OutputFormat)}");
        sb.AppendLine($"textureFormat={options.TextureFormat ?? "png"}");
        sb.AppendLine($"fourChnlPacked={(options.ChannelPacking ? 1 : 0)}");
        sb.AppendLine();

        // Outline
        sb.AppendLine("# outline");
        sb.AppendLine($"outlineThickness={options.Outline}");
        sb.AppendLine();

        // Selected chars
        sb.AppendLine("# selected chars");
        sb.AppendLine($"chars={FormatChars(options)}");
        sb.AppendLine();

        // Output path (if set)
        if (!string.IsNullOrEmpty(options.OutputPath))
        {
            sb.AppendLine("# output path");
            sb.AppendLine($"outputPath={FormatRelativePath(options.OutputPath, filePath)}");
            sb.AppendLine();
        }

        // bmfontier extensions — only include non-default values
        var extensions = new StringBuilder();
        AppendIfNonDefault(extensions, "gradientTop", options.GradientTop);
        AppendIfNonDefault(extensions, "gradientBottom", options.GradientBottom);
        if (options.GradientAngle != 90f)
            extensions.AppendLine($"gradientAngle={options.GradientAngle.ToString(CultureInfo.InvariantCulture)}");
        if (options.GradientMidpoint != 0.5f)
            extensions.AppendLine($"gradientMidpoint={options.GradientMidpoint.ToString(CultureInfo.InvariantCulture)}");
        if (options.ShadowOffsetX != 0)
            extensions.AppendLine($"shadowOffsetX={options.ShadowOffsetX}");
        if (options.ShadowOffsetY != 0)
            extensions.AppendLine($"shadowOffsetY={options.ShadowOffsetY}");
        AppendIfNonDefault(extensions, "shadowColor", options.ShadowColor);
        if (options.ShadowBlur != 0)
            extensions.AppendLine($"shadowBlur={options.ShadowBlur}");
        AppendIfNonDefault(extensions, "outlineColor", options.OutlineColor);
        if (options.Sdf)
            extensions.AppendLine($"useSdf=1");
        if (options.SuperSampleLevel != 1)
            extensions.AppendLine($"superSample={options.SuperSampleLevel}");
        if (options.PackingAlgorithm != PackingAlgorithm.MaxRects)
            extensions.AppendLine($"packer=skyline");
        if (options.FallbackCharacter.HasValue)
            extensions.AppendLine($"fallbackChar={((int)options.FallbackCharacter.Value)}");
        if (options.ColorFont)
            extensions.AppendLine($"colorFont=1");
        if (options.ColorPaletteIndex != 0)
            extensions.AppendLine($"colorPalette={options.ColorPaletteIndex}");
        if (options.FaceIndex != 0)
            extensions.AppendLine($"faceIndex={options.FaceIndex}");
        if (options.Dpi != 72)
            extensions.AppendLine($"dpi={options.Dpi}");
        if (options.PowerOfTwo is false)
            extensions.AppendLine($"powerOfTwo=0");
        if (options.AutofitTexture)
            extensions.AppendLine($"autofit=1");

        if (extensions.Length > 0)
        {
            sb.AppendLine("# bmfontier extensions");
            sb.Append(extensions);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, sb.ToString());
    }

    private static int FormatOutputFormat(OutputFormat format) => format switch
    {
        OutputFormat.Text => 0,
        OutputFormat.Xml => 1,
        OutputFormat.Binary => 2,
        _ => 0
    };

    private static string FormatChars(CliOptions options)
    {
        if (options.UnicodeRanges.Count > 0)
        {
            var parts = options.UnicodeRanges.Select(r =>
                r.Start == r.End ? r.Start.ToString() : $"{r.Start}-{r.End}");
            return string.Join(",", parts);
        }

        // Default to ASCII printable range
        return "32-126";
    }

    private static string FormatFontFilePath(CliOptions options, string configFilePath)
    {
        if (string.IsNullOrEmpty(options.FontPath))
            return "";
        return FormatRelativePath(options.FontPath, configFilePath);
    }

    private static string FormatRelativePath(string targetPath, string configFilePath)
    {
        try
        {
            var configDir = Path.GetDirectoryName(Path.GetFullPath(configFilePath));
            if (string.IsNullOrEmpty(configDir))
                return targetPath;
            var relative = Path.GetRelativePath(configDir, targetPath);
            return relative;
        }
        catch
        {
            return targetPath;
        }
    }

    private static void AppendIfNonDefault(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            sb.AppendLine($"{key}={value}");
    }
}
