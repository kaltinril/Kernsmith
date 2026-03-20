using Bmfontier.Cli.Utilities;

namespace Bmfontier.Cli.Config;

/// <summary>
/// Reads .bmfc INI-like configuration files into <see cref="CliOptions"/>.
/// </summary>
internal static class BmfcParser
{
    public static CliOptions Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Config file not found: {filePath}", filePath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var options = new CliOptions();
        var section = "";

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (line.Length == 0 || line[0] == '#')
                continue;

            // Section header
            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            // Key = value (strip inline comments)
            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
                continue;

            var key = line[..eqIndex].Trim().ToLowerInvariant();
            var value = line[(eqIndex + 1)..].Trim();

            // Strip inline comment
            var commentIndex = value.IndexOf('#');
            if (commentIndex >= 0)
                value = value[..commentIndex].Trim();

            if (value.Length == 0)
                continue;

            try
            {
                ApplyConfigValue(options, section, key, value, dir);
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                ConsoleOutput.WriteWarning($"Config: ignoring unknown or invalid entry [{section}] {key} = {value}: {ex.Message}");
            }
        }

        return options;
    }

    private static void ApplyConfigValue(CliOptions options, string section, string key, string value, string baseDir)
    {
        switch (section)
        {
            case "font":
                switch (key)
                {
                    case "path":
                        options.FontPath = Path.IsPathRooted(value)
                            ? value
                            : Path.GetFullPath(Path.Combine(baseDir, value));
                        break;
                    case "system-font":
                        options.SystemFontName = value;
                        break;
                    case "face-index":
                        options.FaceIndex = int.Parse(value);
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            case "rendering":
                switch (key)
                {
                    case "size":
                        options.Size = int.Parse(value);
                        break;
                    case "dpi":
                        options.Dpi = int.Parse(value);
                        break;
                    case "anti-alias":
                        options.AntiAlias = ParseAntiAlias(value);
                        break;
                    case "sdf":
                        options.Sdf = ParseBool(value);
                        break;
                    case "bold":
                        options.Bold = ParseBool(value);
                        break;
                    case "italic":
                        options.Italic = ParseBool(value);
                        break;
                    case "super-sample":
                        options.SuperSampleLevel = int.Parse(value);
                        break;
                    case "fallback-char":
                        options.FallbackCharacter = value.Length == 1 ? value[0] : (char)int.Parse(value);
                        break;
                    case "hinting":
                        options.EnableHinting = ParseBool(value);
                        break;
                    case "height-percent":
                        options.HeightPercent = int.Parse(value);
                        break;
                    case "match-char-height":
                        options.MatchCharHeight = ParseBool(value);
                        break;
                    case "color-font":
                        options.ColorFont = ParseBool(value);
                        break;
                    case "color-palette":
                        options.ColorPaletteIndex = int.Parse(value);
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            case "characters":
                switch (key)
                {
                    case "preset":
                        options.CharsetPreset = value.ToLowerInvariant();
                        break;
                    case "chars":
                        options.ExplicitChars = value;
                        break;
                    case "chars-file":
                        var charsPath = Path.IsPathRooted(value)
                            ? value
                            : Path.GetFullPath(Path.Combine(baseDir, value));
                        options.CharsFilePath = charsPath;
                        break;
                    case "ranges":
                        foreach (var range in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            options.UnicodeRanges.Add(ParseRange(range));
                        }
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            case "atlas":
                switch (key)
                {
                    case "max-texture-size":
                        options.MaxTextureSize = int.Parse(value);
                        break;
                    case "padding":
                        options.Padding = ParsePadding(value);
                        break;
                    case "spacing":
                        options.Spacing = ParseSpacing(value);
                        break;
                    case "power-of-two":
                        options.PowerOfTwo = ParseBool(value);
                        break;
                    case "packer":
                        options.PackingAlgorithm = value.ToLowerInvariant() switch
                        {
                            "maxrects" => Bmfontier.PackingAlgorithm.MaxRects,
                            "skyline" => Bmfontier.PackingAlgorithm.Skyline,
                            _ => throw new ArgumentException($"Unknown packer: {value}")
                        };
                        break;
                    case "channel-pack":
                        options.ChannelPacking = ParseBool(value);
                        break;
                    case "max-texture-width":
                        options.MaxTextureWidth = int.Parse(value);
                        break;
                    case "max-texture-height":
                        options.MaxTextureHeight = int.Parse(value);
                        break;
                    case "autofit":
                        options.AutofitTexture = ParseBool(value);
                        break;
                    case "texture-format":
                        options.TextureFormat = value.ToLowerInvariant();
                        break;
                    case "equalize-heights":
                        options.EqualizeCellHeights = ParseBool(value);
                        break;
                    case "force-offsets-zero":
                        options.ForceOffsetsToZero = ParseBool(value);
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            case "effects":
                switch (key)
                {
                    case "outline":
                        options.Outline = int.Parse(value);
                        break;
                    case "gradient-top":
                        options.GradientTop = value;
                        break;
                    case "gradient-bottom":
                        options.GradientBottom = value;
                        break;
                    case "shadow-offset-x":
                        options.ShadowOffsetX = int.Parse(value);
                        break;
                    case "shadow-offset-y":
                        options.ShadowOffsetY = int.Parse(value);
                        break;
                    case "shadow-color":
                        options.ShadowColor = value;
                        break;
                    case "shadow-blur":
                        options.ShadowBlur = int.Parse(value);
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            case "kerning":
                switch (key)
                {
                    case "enabled":
                        options.Kerning = ParseBool(value);
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            case "variable":
                if (key == "instance")
                {
                    options.InstanceName = value;
                }
                else if (float.TryParse(value, out var axisValue))
                {
                    // Any other key in [variable] is an axis tag
                    options.VariationAxes[key] = axisValue;
                }
                else
                {
                    ConsoleOutput.WriteWarning($"Invalid axis value: {key} = {value}");
                }
                break;

            case "output":
                switch (key)
                {
                    case "format":
                        options.OutputFormat = value.ToLowerInvariant() switch
                        {
                            "text" => Bmfontier.OutputFormat.Text,
                            "xml" => Bmfontier.OutputFormat.Xml,
                            "binary" => Bmfontier.OutputFormat.Binary,
                            _ => throw new ArgumentException($"Unknown format: {value}")
                        };
                        break;
                    case "path":
                        options.OutputPath = Path.IsPathRooted(value)
                            ? value
                            : Path.GetFullPath(Path.Combine(baseDir, value));
                        break;
                    default:
                        ConsoleOutput.WriteWarning($"Unknown config key: [{section}] {key}");
                        break;
                }
                break;

            default:
                ConsoleOutput.WriteWarning($"Unknown config section: [{section}]");
                break;
        }
    }

    private static bool ParseBool(string value)
        => value.ToLowerInvariant() is "true" or "1" or "yes";

    private static AntiAliasMode ParseAntiAlias(string value)
        => value.ToLowerInvariant() switch
        {
            "none" => AntiAliasMode.None,
            "grayscale" => AntiAliasMode.Grayscale,
            "light" => AntiAliasMode.Light,
            "lcd" => AntiAliasMode.Lcd,
            _ => throw new ArgumentException($"Unknown anti-alias mode: {value}")
        };

    private static (int Start, int End) ParseRange(string range)
    {
        var parts = range.Split('-', 2);
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid range: {range}. Expected format: 0020-007E");
        var start = Convert.ToInt32(parts[0].Trim(), 16);
        var end = Convert.ToInt32(parts[1].Trim(), 16);
        return (start, end);
    }

    private static Padding ParsePadding(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 4)
            return new Padding(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        if (parts.Length == 1)
            return new Padding(int.Parse(parts[0]));
        throw new ArgumentException($"Invalid padding: {value}. Use a single value or u,r,d,l");
    }

    private static Spacing ParseSpacing(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
            return new Spacing(int.Parse(parts[0]), int.Parse(parts[1]));
        if (parts.Length == 1)
            return new Spacing(int.Parse(parts[0]));
        throw new ArgumentException($"Invalid spacing: {value}. Use a single value or h,v");
    }
}
