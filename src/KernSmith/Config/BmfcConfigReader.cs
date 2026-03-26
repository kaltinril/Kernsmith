using System.Globalization;

namespace KernSmith;

/// <summary>
/// Reads .bmfc configuration files in BMFont flat key=value format into <see cref="BmfcConfig"/>.
/// </summary>
public static class BmfcConfigReader
{
    /// <summary>
    /// Reads a .bmfc file from disk and returns a <see cref="BmfcConfig"/>.
    /// Relative paths in the config (fontFile, outputPath) are resolved against the config file's directory.
    /// </summary>
    /// <param name="filePath">Path to the .bmfc file.</param>
    /// <returns>A <see cref="BmfcConfig"/> containing parsed options and font source info.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="filePath"/> does not exist.</exception>
    public static BmfcConfig Read(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Config file not found: {filePath}", filePath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var content = File.ReadAllText(filePath);
        var config = ParseCore(content);

        // Resolve relative paths against the config file directory
        if (!string.IsNullOrEmpty(config.FontFile) && !Path.IsPathRooted(config.FontFile))
            config.FontFile = Path.GetFullPath(Path.Combine(dir, config.FontFile));

        if (!string.IsNullOrEmpty(config.OutputPath) && !Path.IsPathRooted(config.OutputPath))
            config.OutputPath = Path.GetFullPath(Path.Combine(dir, config.OutputPath));

        return config;
    }

    /// <summary>
    /// Parses .bmfc content from a string and returns a <see cref="BmfcConfig"/>.
    /// Relative paths are not resolved since there is no file context.
    /// </summary>
    /// <param name="content">The .bmfc file content.</param>
    /// <returns>A <see cref="BmfcConfig"/> containing parsed options and font source info.</returns>
    public static BmfcConfig Parse(string content)
    {
        return ParseCore(content);
    }

    private static BmfcConfig ParseCore(string content)
    {
        var config = new BmfcConfig();
        var options = config.Options;

        // Accumulate padding/spacing values with defaults
        int paddingUp = 0, paddingDown = 0, paddingRight = 0, paddingLeft = 0;
        int spacingHoriz = 1, spacingVert = 1;
        bool hasPadding = false, hasSpacing = false;

        // Accumulate character ranges
        var unicodeRanges = new List<(int Start, int End)>();
        bool hasChars = false;

        // Track color strings for deferred parsing
        string? gradientTop = null, gradientBottom = null;
        string? outlineColor = null;
        string? shadowColor = null;

        foreach (var rawLine in content.Split('\n'))
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
                        config.FontName = value;
                        break;
                    case "fontFile":
                        if (!string.IsNullOrEmpty(value))
                            config.FontFile = value;
                        break;
                    case "fontSize":
                        var sizeVal = int.Parse(value, CultureInfo.InvariantCulture);
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
                        // BMFont's aa is the supersampling factor (1, 2, or 4), not the AA mode.
                        options.SuperSampleLevel = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "useHinting":
                        options.EnableHinting = value == "1";
                        break;
                    case "scaleH":
                        options.HeightPercent = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "dontIncludeKerningPairs":
                        options.Kerning = value != "1";
                        break;
                    case "autoFitNumPages":
                        if (int.Parse(value, CultureInfo.InvariantCulture) > 0)
                            options.AutofitTexture = true;
                        break;

                    // Character alignment
                    case "paddingUp":
                        paddingUp = int.Parse(value, CultureInfo.InvariantCulture);
                        hasPadding = true;
                        break;
                    case "paddingDown":
                        paddingDown = int.Parse(value, CultureInfo.InvariantCulture);
                        hasPadding = true;
                        break;
                    case "paddingRight":
                        paddingRight = int.Parse(value, CultureInfo.InvariantCulture);
                        hasPadding = true;
                        break;
                    case "paddingLeft":
                        paddingLeft = int.Parse(value, CultureInfo.InvariantCulture);
                        hasPadding = true;
                        break;
                    case "spacingHoriz":
                        spacingHoriz = int.Parse(value, CultureInfo.InvariantCulture);
                        hasSpacing = true;
                        break;
                    case "spacingVert":
                        spacingVert = int.Parse(value, CultureInfo.InvariantCulture);
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
                        options.MaxTextureWidth = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "outHeight":
                        options.MaxTextureHeight = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "fontDescFormat":
                        config.OutputFormat = int.Parse(value, CultureInfo.InvariantCulture) switch
                        {
                            0 => OutputFormat.Text,
                            1 => OutputFormat.Xml,
                            2 => OutputFormat.Binary,
                            _ => OutputFormat.Text
                        };
                        break;
                    case "textureFormat":
                        options.TextureFormat = value.ToLowerInvariant() switch
                        {
                            "png" => TextureFormat.Png,
                            "tga" => TextureFormat.Tga,
                            "dds" => TextureFormat.Dds,
                            _ => TextureFormat.Png
                        };
                        break;
                    case "fourChnlPacked":
                        options.ChannelPacking = value == "1";
                        break;

                    // Outline
                    case "outlineThickness":
                        options.Outline = int.Parse(value, CultureInfo.InvariantCulture);
                        break;

                    // Characters
                    case "chars":
                        ParseBmFontChars(unicodeRanges, value);
                        hasChars = true;
                        break;

                    // kernsmith extension keys
                    case "gradientTop":
                        if (!string.IsNullOrEmpty(value))
                            gradientTop = value;
                        break;
                    case "gradientBottom":
                        if (!string.IsNullOrEmpty(value))
                            gradientBottom = value;
                        break;
                    case "gradientAngle":
                        options.GradientAngle = float.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "gradientMidpoint":
                        options.GradientMidpoint = float.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "shadowOffsetX":
                        options.ShadowOffsetX = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "shadowOffsetY":
                        options.ShadowOffsetY = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "shadowColor":
                        if (!string.IsNullOrEmpty(value))
                            shadowColor = value;
                        break;
                    case "shadowBlur":
                        options.ShadowBlur = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "outlineColor":
                        if (!string.IsNullOrEmpty(value))
                            outlineColor = value;
                        break;
                    case "useSdf":
                        options.Sdf = value == "1";
                        break;
                    case "superSample":
                        options.SuperSampleLevel = int.Parse(value, CultureInfo.InvariantCulture);
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
                            config.OutputPath = value;
                        break;
                    case "fallbackChar":
                        if (!string.IsNullOrEmpty(value))
                        {
                            int fallbackCode = value.Length == 1 ? value[0] : int.Parse(value, CultureInfo.InvariantCulture);
                            if (fallbackCode > 0xFFFF)
                                options.FallbackCodepoint = fallbackCode;
                            else
                                options.FallbackCharacter = (char)fallbackCode;
                        }
                        break;
                    case "colorFont":
                        options.ColorFont = value == "1";
                        break;
                    case "colorPalette":
                        options.ColorPaletteIndex = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "faceIndex":
                        options.FaceIndex = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "dpi":
                        options.Dpi = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "powerOfTwo":
                        options.PowerOfTwo = value == "1";
                        break;
                    case "autofit":
                        options.AutofitTexture = value == "1";
                        break;
                    case "rasterizer":
                        if (Enum.TryParse<RasterizerBackend>(value, true, out var backend))
                            options.Backend = backend;
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
                        // Unknown key -- skip silently for forward compatibility
                        break;
                }
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                // Silently ignore invalid entries in library context
            }
        }

        if (hasPadding)
            options.Padding = new Padding(paddingUp, paddingRight, paddingDown, paddingLeft);
        if (hasSpacing)
            options.Spacing = new Spacing(spacingHoriz, spacingVert);

        // Build character set from parsed ranges
        if (hasChars && unicodeRanges.Count > 0)
            options.Characters = CharacterSet.FromRanges(unicodeRanges.ToArray());

        // Parse color strings into RGB bytes
        if (gradientTop != null && gradientBottom != null)
        {
            var top = ParseHexColor(gradientTop);
            var bottom = ParseHexColor(gradientBottom);
            options.GradientStartR = top.R;
            options.GradientStartG = top.G;
            options.GradientStartB = top.B;
            options.GradientEndR = bottom.R;
            options.GradientEndG = bottom.G;
            options.GradientEndB = bottom.B;
        }

        if (options.Outline > 0 && outlineColor != null)
        {
            var oc = ParseHexColor(outlineColor);
            options.OutlineR = oc.R;
            options.OutlineG = oc.G;
            options.OutlineB = oc.B;
        }

        if (shadowColor != null)
        {
            var sc = ParseHexColor(shadowColor);
            options.ShadowR = sc.R;
            options.ShadowG = sc.G;
            options.ShadowB = sc.B;
        }

        return config;
    }

    /// <summary>
    /// Parses BMFont chars= format: comma-separated decimal codepoints and ranges.
    /// Example: "32-126,160-255,8364"
    /// </summary>
    private static void ParseBmFontChars(List<(int Start, int End)> ranges, string value)
    {
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var dashIndex = part.IndexOf('-');
            if (dashIndex >= 0)
            {
                var startStr = part[..dashIndex].Trim();
                var endStr = part[(dashIndex + 1)..].Trim();
                var start = int.Parse(startStr, CultureInfo.InvariantCulture);
                var end = int.Parse(endStr, CultureInfo.InvariantCulture);
                ranges.Add((start, end));
            }
            else
            {
                var code = int.Parse(part, CultureInfo.InvariantCulture);
                ranges.Add((code, code));
            }
        }
    }

    /// <summary>
    /// Parses a hex color string (e.g., "FF0000", "#F00", "FF0000FF") into RGB bytes.
    /// </summary>
    private static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        var s = hex.TrimStart('#');

        if (s.Length == 3)
            s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });

        if (s.Length == 4)
            s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });

        if (s.Length == 8)
            s = s[..6];

        if (s.Length != 6)
            return (0, 0, 0); // Fallback to black for invalid colors

        try
        {
            var r = Convert.ToByte(s[..2], 16);
            var g = Convert.ToByte(s[2..4], 16);
            var b = Convert.ToByte(s[4..6], 16);
            return (r, g, b);
        }
        catch
        {
            return (0, 0, 0);
        }
    }
}
