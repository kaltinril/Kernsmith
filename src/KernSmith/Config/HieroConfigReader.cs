using System.Diagnostics;
using System.Globalization;

namespace KernSmith;

/// <summary>
/// Reads Hiero <c>.hiero</c> configuration files (flat key=value format) into <see cref="BmfcConfig"/>.
/// Maps Hiero properties and effect blocks onto KernSmith <see cref="FontGeneratorOptions"/>.
/// </summary>
public static class HieroConfigReader
{
    /// <summary>
    /// Reads a <c>.hiero</c> file from disk and returns a <see cref="BmfcConfig"/>.
    /// A relative <c>font2.file</c> path is resolved against the config file's directory.
    /// </summary>
    /// <param name="filePath">Path to the .hiero file.</param>
    /// <returns>A <see cref="BmfcConfig"/> containing parsed options and font source info.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="filePath"/> does not exist.</exception>
    public static BmfcConfig Read(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Config file not found: {filePath}", filePath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var content = File.ReadAllText(filePath);
        var config = ParseCore(content);

        // Resolve relative font file path against the config file directory
        if (!string.IsNullOrEmpty(config.FontFile) && !Path.IsPathRooted(config.FontFile))
            config.FontFile = Path.GetFullPath(Path.Combine(dir, config.FontFile));

        return config;
    }

    /// <summary>
    /// Parses <c>.hiero</c> content from a string and returns a <see cref="BmfcConfig"/>.
    /// Relative paths are not resolved since there is no file context.
    /// </summary>
    /// <param name="content">The .hiero file content.</param>
    /// <returns>A <see cref="BmfcConfig"/> containing parsed options and font source info.</returns>
    public static BmfcConfig Parse(string content)
    {
        return ParseCore(content);
    }

    private static BmfcConfig ParseCore(string content)
    {
        // Strip a leading UTF-8 BOM (U+FEFF) so Parse(string) matches Read (File.ReadAllText already strips it).
        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content[1..];

        var config = new BmfcConfig();
        var options = config.Options;

        // Accumulate padding values with defaults
        int padTop = 0, padRight = 0, padBottom = 0, padLeft = 0;
        bool hasPadding = false;

        // Font2 (file-based) source
        string? font2File = null;
        bool font2Use = false;

        // Track current effect block
        string? currentEffectClass = null;
        var effectValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var effects = new List<(string Class, Dictionary<string, string> Values)>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Split on first '='; key is trimmed, value is NOT trimmed (per REF-10)
            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
                continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..];

            if (key.Length == 0)
                continue;

            try
            {
                if (key == "effect.class")
                {
                    // Starting a new effect block: flush the previous one.
                    if (currentEffectClass != null)
                        effects.Add((currentEffectClass, new Dictionary<string, string>(effectValues, StringComparer.Ordinal)));
                    currentEffectClass = value.Trim();
                    effectValues.Clear();
                    continue;
                }

                if (key.StartsWith("effect.", StringComparison.Ordinal))
                {
                    // Effect value: the part after "effect." is the Hiero value name (may contain spaces).
                    var valueName = key["effect.".Length..];
                    effectValues[valueName] = value.Trim();
                    continue;
                }

                switch (key)
                {
                    // Font properties
                    case "font.name":
                        // An empty font.name (e.g. when the source font was a file, not a system
                        // font) must map to null, NOT "". A non-null FontName flows to the CLI as
                        // SystemFontName and would trigger a system-font lookup for "" -> "System
                        // font '' not found" on macOS/Linux (Windows leniently resolves "").
                        var fontName = value.Trim();
                        config.FontName = fontName.Length == 0 ? null : fontName;
                        break;
                    case "font.size":
                        // FontGeneratorOptions.Size is a float; tolerate "32", "32.0", "32.5".
                        if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                            options.Size = size;
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse font.size value '{value.Trim()}'; keeping default.");
                        break;
                    case "font.bold":
                        options.Bold = value.Trim() == "true";
                        break;
                    case "font.italic":
                        options.Italic = value.Trim() == "true";
                        break;
                    case "font.mono":
                        if (value.Trim() == "true")
                            options.AntiAlias = AntiAliasMode.None;
                        break;
                    case "font.gamma":
                        // No KernSmith equivalent -- dropped (deferred to Phase 100).
                        break;

                    // Secondary (file-based) font
                    case "font2.file":
                        if (!string.IsNullOrEmpty(value.Trim()))
                            font2File = value.Trim();
                        break;
                    case "font2.use":
                        font2Use = value.Trim() == "true";
                        break;

                    // Padding
                    case "pad.top":
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ptop))
                        {
                            padTop = ptop;
                            hasPadding = true;
                        }
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse pad.top value '{value.Trim()}'; keeping default.");
                        break;
                    case "pad.right":
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pright))
                        {
                            padRight = pright;
                            hasPadding = true;
                        }
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse pad.right value '{value.Trim()}'; keeping default.");
                        break;
                    case "pad.bottom":
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pbottom))
                        {
                            padBottom = pbottom;
                            hasPadding = true;
                        }
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse pad.bottom value '{value.Trim()}'; keeping default.");
                        break;
                    case "pad.left":
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pleft))
                        {
                            padLeft = pleft;
                            hasPadding = true;
                        }
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse pad.left value '{value.Trim()}'; keeping default.");
                        break;
                    case "pad.advance.x":
                    case "pad.advance.y":
                        // Per-glyph advance adjustment has no KernSmith equivalent.
                        // Dropped with a warning (deferred to Phase 100).
                        Debug.WriteLine($"[HieroConfigReader] Dropping unsupported key '{key}' (per-glyph advance adjustment has no KernSmith equivalent).");
                        break;

                    // Glyph / texture page settings
                    case "glyph.page.width":
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageWidth))
                            options.MaxTextureWidth = pageWidth;
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse glyph.page.width value '{value.Trim()}'; keeping default.");
                        break;
                    case "glyph.page.height":
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageHeight))
                            options.MaxTextureHeight = pageHeight;
                        else
                            Debug.WriteLine($"[HieroConfigReader] Could not parse glyph.page.height value '{value.Trim()}'; keeping default.");
                        break;
                    case "glyph.text":
                        // Literal characters; real Hiero escapes only newlines as "\n" (a backslash
                        // is otherwise literal). Mirror that with a single left-to-right unescape pass.
                        options.Characters = CharacterSet.FromChars(UnescapeGlyphText(value));
                        break;
                    case "glyph.native.rendering":
                        // KernSmith always uses its configured rasterizer backend; ignored.
                        break;

                    case "render_type":
                        // KernSmith always uses FreeType-style rendering; warn if non-FreeType (2).
                        if (value.Trim() != "2")
                            Debug.WriteLine($"[HieroConfigReader] render_type={value.Trim()} is not FreeType (2); KernSmith always renders via its configured backend.");
                        break;

                    default:
                        // Unknown key -- skip silently for forward compatibility.
                        break;
                }
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                // Silently ignore invalid entries in library context.
            }
        }

        // Flush the final effect block.
        if (currentEffectClass != null)
            effects.Add((currentEffectClass, new Dictionary<string, string>(effectValues, StringComparer.Ordinal)));

        if (hasPadding)
            options.Padding = new Padding(padTop, padRight, padBottom, padLeft);

        // Resolve font source: when font2.use is true the secondary FILE font is authoritative
        // and Hiero ignores the primary font.name. Clear FontName so downstream consumers use the
        // file -- otherwise the CLI (which prefers SystemFontName) would attempt a system-font
        // lookup for that name and fail (e.g. "System font 'CherokeeHandone' not found"), even
        // though font2.file points at a real font.
        if (font2Use && !string.IsNullOrEmpty(font2File))
        {
            config.FontFile = font2File;
            config.FontName = null;
        }

        ApplyEffects(options, effects);

        return config;
    }

    private static void ApplyEffects(FontGeneratorOptions options, List<(string Class, Dictionary<string, string> Values)> effects)
    {
        foreach (var (className, values) in effects)
        {
            var shortName = ShortClassName(className);
            switch (shortName)
            {
                case "ColorEffect":
                    // Fill color: ignored on import (always white on export). Warn if non-white.
                    if (values.TryGetValue("Color", out var fill) && !IsWhite(fill))
                        Debug.WriteLine($"[HieroConfigReader] ColorEffect fill color '{fill}' ignored on import; KernSmith always uses white fill.");
                    break;

                case "OutlineEffect":
                    if (values.TryGetValue("Width", out var widthStr)
                        && float.TryParse(widthStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                    {
                        // Hiero Width is a float (range 0.1-999); KernSmith Outline is an int.
                        // Round for width >= 0.5, but bump any positive sub-pixel width up to 1 so a
                        // legitimately thin Hiero outline isn't silently dropped (along with its color).
                        options.Outline = width > 0 ? Math.Max(1, (int)Math.Round(width)) : 0;
                        if (width > 0 && width < 0.5f)
                            Debug.WriteLine($"[HieroConfigReader] OutlineEffect Width '{widthStr}' rounds to 0; bumped to 1 to preserve a thin outline.");
                    }
                    else
                        // Hiero's documented Width default is 2 (REF-10); apply it (with the same
                        // Math.Round semantics) when the outline block exists but omits the key,
                        // rather than leaving KernSmith's 0 default and dropping the outline.
                        options.Outline = (int)Math.Round(2f);
                    if (options.Outline > 0 && values.TryGetValue("Color", out var outlineColor))
                    {
                        var oc = ParseHexColor(outlineColor);
                        options.OutlineR = oc.R;
                        options.OutlineG = oc.G;
                        options.OutlineB = oc.B;
                    }
                    // 'Join' has no KernSmith equivalent -- dropped.
                    break;

                case "GradientEffect":
                    {
                        var hasTop = values.TryGetValue("Top color", out var topColor);
                        var hasBottom = values.TryGetValue("Bottom color", out var bottomColor);
                        if (hasTop && hasBottom)
                        {
                            var top = ParseHexColor(topColor!);
                            var bottom = ParseHexColor(bottomColor!);
                            options.GradientStartR = top.R;
                            options.GradientStartG = top.G;
                            options.GradientStartB = top.B;
                            options.GradientEndR = bottom.R;
                            options.GradientEndG = bottom.G;
                            options.GradientEndB = bottom.B;
                        }
                        // Offset/Scale/Cyclic have no KernSmith equivalent -- dropped.
                    }
                    break;

                case "ShadowEffect":
                    if (values.TryGetValue("X distance", out var xdStr)
                        && float.TryParse(xdStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var xd))
                        options.ShadowOffsetX = (int)Math.Round(xd);
                    else
                        // Hiero's documented X distance default is 2 (REF-10); apply it when the shadow
                        // block exists but omits the key, rather than leaving KernSmith's 0 default.
                        options.ShadowOffsetX = (int)Math.Round(2f);
                    if (values.TryGetValue("Y distance", out var ydStr)
                        && float.TryParse(ydStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var yd))
                        options.ShadowOffsetY = (int)Math.Round(yd);
                    else
                        // Hiero's documented Y distance default is 2 (REF-10); apply it when the shadow
                        // block exists but omits the key, rather than leaving KernSmith's 0 default.
                        options.ShadowOffsetY = (int)Math.Round(2f);
                    if (values.TryGetValue("Color", out var shadowColor))
                    {
                        var sc = ParseHexColor(shadowColor);
                        options.ShadowR = sc.R;
                        options.ShadowG = sc.G;
                        options.ShadowB = sc.B;
                    }
                    if (values.TryGetValue("Opacity", out var opStr)
                        && float.TryParse(opStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var op))
                        options.ShadowOpacity = op;
                    else
                        // Hiero's documented Opacity default is 0.6 (REF-10); apply it when the shadow
                        // block exists but omits the key, rather than leaving KernSmith's 1.0 default.
                        options.ShadowOpacity = 0.6f;
                    // Hiero's two-parameter blur (kernel size + passes) collapses to a single blur radius.
                    if (values.TryGetValue("Blur kernel size", out var blurStr)
                        && int.TryParse(blurStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var blur))
                        options.ShadowBlur = blur;
                    break;

                case "DistanceFieldEffect":
                    options.Sdf = true;
                    // Scale/Spread have no KernSmith equivalent -- dropped.
                    break;

                case "OutlineWobbleEffect":
                case "OutlineZigzagEffect":
                    Debug.WriteLine($"[HieroConfigReader] '{shortName}' has no KernSmith equivalent; skipped.");
                    break;

                default:
                    Debug.WriteLine($"[HieroConfigReader] Unknown effect class '{className}'; skipped.");
                    break;
            }
        }
    }

    /// <summary>
    /// Reverses real Hiero's <c>glyph.text</c> escaping with a single left-to-right pass:
    /// only the two-char sequence "\n" -> newline. A backslash is NOT an escape introducer in
    /// real Hiero (HieroSettings.java), so any other '\' (including a '\' not followed by 'n',
    /// or a lone trailing '\') is passed through verbatim as a literal backslash.
    /// </summary>
    private static string UnescapeGlyphText(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length && value[i + 1] == 'n')
            {
                sb.Append('\n');
                i++;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ShortClassName(string fullClassName)
    {
        var idx = fullClassName.LastIndexOf('.');
        return idx >= 0 ? fullClassName[(idx + 1)..] : fullClassName;
    }

    private static bool IsWhite(string hex)
    {
        var (r, g, b) = ParseHexColor(hex);
        return r == 255 && g == 255 && b == 255;
    }

    /// <summary>
    /// Parses a Hiero hex color string (6 lowercase hex chars, RRGGBB, no prefix) into RGB bytes.
    /// Returns white for invalid or non-6-character colors (matches Hiero's <c>Color.white</c> fallback, REF-10).
    /// </summary>
    private static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        var s = hex.Trim().TrimStart('#');

        if (s.Length != 6)
            return (255, 255, 255);

        try
        {
            var r = Convert.ToByte(s[..2], 16);
            var g = Convert.ToByte(s[2..4], 16);
            var b = Convert.ToByte(s[4..6], 16);
            return (r, g, b);
        }
        catch
        {
            return (255, 255, 255);
        }
    }
}
