using System.Globalization;
using KernSmith.Cli.Utilities;

namespace KernSmith.Cli.Config;

/// <summary>
/// Reads .bmfc configuration files in BMFont flat key=value format into <see cref="CliOptions"/>.
/// </summary>
internal static class BmfcParser
{
    public static CliOptions Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Config file not found: {filePath}", filePath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var lines = File.ReadAllLines(filePath);
        var options = new CliOptions();

        // Accumulate padding/spacing values with defaults
        int paddingUp = 0, paddingDown = 0, paddingRight = 0, paddingLeft = 0;
        int spacingHoriz = 1, spacingVert = 1;
        bool hasPadding = false, hasSpacing = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (line.Length == 0 || line[0] == '#')
                continue;

            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
                continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();

            try
            {
                switch (key)
                {
                    // Font settings
                    case "fontName":
                        options.SystemFontName = value;
                        break;
                    case "fontFile":
                        if (!string.IsNullOrEmpty(value))
                            options.FontPath = Path.IsPathRooted(value)
                                ? value
                                : Path.GetFullPath(Path.Combine(dir, value));
                        break;
                    case "fontSize":
                        var sizeVal = int.Parse(value);
                        if (sizeVal < 0)
                        {
                            options.Size = Math.Abs(sizeVal);
                            options.MatchCharHeight = true;
                        }
                        else
                        {
                            options.Size = sizeVal;
                        }
                        break;
                    case "isBold":
                        options.Bold = value == "1";
                        break;
                    case "isItalic":
                        options.Italic = value == "1";
                        break;
                    case "useSmoothing":
                        if (value == "0")
                            options.AntiAlias = AntiAliasMode.None;
                        break;
                    case "aa":
                        // 1=grayscale (default), 2=ClearType (map to Light as closest)
                        if (value == "2")
                            options.AntiAlias = AntiAliasMode.Light;
                        break;
                    case "useHinting":
                        options.EnableHinting = value == "1";
                        break;
                    case "scaleH":
                        options.HeightPercent = int.Parse(value);
                        break;
                    case "dontIncludeKerningPairs":
                        options.Kerning = value != "1";
                        break;
                    case "autoFitNumPages":
                        if (int.Parse(value) > 0)
                            options.AutofitTexture = true;
                        break;

                    // Character alignment
                    case "paddingUp":
                        paddingUp = int.Parse(value);
                        hasPadding = true;
                        break;
                    case "paddingDown":
                        paddingDown = int.Parse(value);
                        hasPadding = true;
                        break;
                    case "paddingRight":
                        paddingRight = int.Parse(value);
                        hasPadding = true;
                        break;
                    case "paddingLeft":
                        paddingLeft = int.Parse(value);
                        hasPadding = true;
                        break;
                    case "spacingHoriz":
                        spacingHoriz = int.Parse(value);
                        hasSpacing = true;
                        break;
                    case "spacingVert":
                        spacingVert = int.Parse(value);
                        hasSpacing = true;
                        break;
                    case "useFixedHeight":
                        options.EqualizeCellHeights = value == "1";
                        break;
                    case "forceZero":
                        options.ForceOffsetsToZero = value == "1";
                        break;

                    // Output file
                    case "outWidth":
                        options.MaxTextureWidth = int.Parse(value);
                        break;
                    case "outHeight":
                        options.MaxTextureHeight = int.Parse(value);
                        break;
                    case "fontDescFormat":
                        options.OutputFormat = int.Parse(value) switch
                        {
                            0 => OutputFormat.Text,
                            1 => OutputFormat.Xml,
                            2 => OutputFormat.Binary,
                            _ => OutputFormat.Text
                        };
                        break;
                    case "textureFormat":
                        options.TextureFormat = value.ToLowerInvariant();
                        break;
                    case "fourChnlPacked":
                        options.ChannelPacking = value == "1";
                        break;

                    // Outline
                    case "outlineThickness":
                        options.Outline = int.Parse(value);
                        break;

                    // Characters
                    case "chars":
                        ParseBmFontChars(options, value);
                        break;

                    // kernsmith extension keys
                    case "gradientTop":
                        if (!string.IsNullOrEmpty(value))
                            options.GradientTop = value;
                        break;
                    case "gradientBottom":
                        if (!string.IsNullOrEmpty(value))
                            options.GradientBottom = value;
                        break;
                    case "gradientAngle":
                        options.GradientAngle = float.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "gradientMidpoint":
                        options.GradientMidpoint = float.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "shadowOffsetX":
                        options.ShadowOffsetX = int.Parse(value);
                        break;
                    case "shadowOffsetY":
                        options.ShadowOffsetY = int.Parse(value);
                        break;
                    case "shadowColor":
                        if (!string.IsNullOrEmpty(value))
                            options.ShadowColor = value;
                        break;
                    case "shadowBlur":
                        options.ShadowBlur = int.Parse(value);
                        break;
                    case "outlineColor":
                        if (!string.IsNullOrEmpty(value))
                            options.OutlineColor = value;
                        break;
                    case "useSdf":
                        options.Sdf = value == "1";
                        break;
                    case "superSample":
                        options.SuperSampleLevel = int.Parse(value);
                        break;
                    case "packer":
                        options.PackingAlgorithm = value.ToLowerInvariant() switch
                        {
                            "maxrects" => PackingAlgorithm.MaxRects,
                            "skyline" => PackingAlgorithm.Skyline,
                            _ => PackingAlgorithm.MaxRects
                        };
                        break;
                    case "outputPath":
                        if (!string.IsNullOrEmpty(value))
                            options.OutputPath = Path.IsPathRooted(value)
                                ? value
                                : Path.GetFullPath(Path.Combine(dir, value));
                        break;
                    case "fallbackChar":
                        if (!string.IsNullOrEmpty(value))
                            options.FallbackCharacter = value.Length == 1 ? value[0] : (char)int.Parse(value);
                        break;
                    case "colorFont":
                        options.ColorFont = value == "1";
                        break;
                    case "colorPalette":
                        options.ColorPaletteIndex = int.Parse(value);
                        break;
                    case "faceIndex":
                        options.FaceIndex = int.Parse(value);
                        break;
                    case "dpi":
                        options.Dpi = int.Parse(value);
                        break;
                    case "powerOfTwo":
                        options.PowerOfTwo = value == "1";
                        break;
                    case "autofit":
                        options.AutofitTexture = value == "1";
                        break;

                    // Ignored BMFont keys we don't support
                    case "fileVersion":
                    case "charSet":
                    case "useUnicode":
                    case "disableBoxChars":
                    case "outputInvalidCharGlyph":
                    case "renderFromOutline":
                    case "useClearType":
                    case "autoFitFontSizeMin":
                    case "autoFitFontSizeMax":
                    case "widthPaddingFactor":
                    case "outBitDepth":
                    case "textureCompression":
                    case "alphaChnl":
                    case "redChnl":
                    case "greenChnl":
                    case "blueChnl":
                    case "invA":
                    case "invR":
                    case "invG":
                    case "invB":
                        // Silently ignore known BMFont keys we don't map
                        break;

                    default:
                        // Unknown key — skip silently for forward compatibility
                        break;
                }
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                ConsoleOutput.WriteWarning($"Config: ignoring invalid entry {key}={value}: {ex.Message}");
            }
        }

        if (hasPadding)
            options.Padding = new Padding(paddingUp, paddingRight, paddingDown, paddingLeft);
        if (hasSpacing)
            options.Spacing = new Spacing(spacingHoriz, spacingVert);

        return options;
    }

    /// <summary>
    /// Parses BMFont chars= format: comma-separated decimal codepoints and ranges.
    /// Example: "32-126,160-255,8364"
    /// </summary>
    private static void ParseBmFontChars(CliOptions options, string value)
    {
        // Clear default preset since we have explicit character definitions
        options.CharsetPreset = null;

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var dashIndex = part.IndexOf('-');
            if (dashIndex >= 0)
            {
                // Range: "32-126" (decimal)
                var startStr = part[..dashIndex].Trim();
                var endStr = part[(dashIndex + 1)..].Trim();
                var start = int.Parse(startStr);
                var end = int.Parse(endStr);
                options.UnicodeRanges.Add((start, end));
            }
            else
            {
                // Single codepoint: "8364" (decimal)
                var code = int.Parse(part);
                options.UnicodeRanges.Add((code, code));
            }
        }
    }
}
