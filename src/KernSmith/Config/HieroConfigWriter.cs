using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KernSmith;

/// <summary>
/// Serializes <see cref="BmfcConfig"/> to Hiero <c>.hiero</c> flat key=value format.
/// Effects are emitted in a fixed canonical order for stable round-trips:
/// ColorEffect, OutlineEffect, GradientEffect, ShadowEffect, DistanceFieldEffect.
/// </summary>
public static class HieroConfigWriter
{
    private const string EffectPackage = "com.badlogic.gdx.tools.hiero.unicodefont.effects.";

    /// <summary>
    /// Writes a <see cref="BmfcConfig"/> as a .hiero format string.
    /// </summary>
    /// <param name="config">The configuration to serialize.</param>
    /// <returns>The .hiero file content as a string.</returns>
    public static string Write(BmfcConfig config)
    {
        return WriteCore(config, relativeBasePath: null);
    }

    /// <summary>
    /// Writes a <see cref="BmfcConfig"/> to a .hiero file on disk.
    /// The font file path is written as a relative path when possible.
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
        ArgumentNullException.ThrowIfNull(config);
        var options = config.Options;
        var sb = new StringBuilder();

        // Font properties
        sb.AppendLine($"font.name={config.FontName ?? ""}");
        // .hiero stores font.size as an integer; round here since FontGeneratorOptions.Size is float.
        var fontSize = (int)Math.Round(options.Size);
        if (options.Size != Math.Round(options.Size))
            Debug.WriteLine($"[HieroConfigWriter] Fractional font size {options.Size.ToString(CultureInfo.InvariantCulture)} was rounded to {fontSize} for the .hiero integer font.size field; this is lossy.");
        sb.AppendLine($"font.size={fontSize}");
        sb.AppendLine($"font.bold={Bool(options.Bold)}");
        sb.AppendLine($"font.italic={Bool(options.Italic)}");
        sb.AppendLine($"font.gamma={options.Gamma.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"font.mono={Bool(options.AntiAlias == AntiAliasMode.None)}");
        sb.AppendLine();

        // Secondary (file-based) font
        var font2File = FormatPath(config.FontFile, relativeBasePath);
        sb.AppendLine($"font2.file={font2File}");
        sb.AppendLine($"font2.use={Bool(!string.IsNullOrEmpty(font2File))}");
        sb.AppendLine();

        // Padding
        var pad = options.Padding;
        sb.AppendLine($"pad.top={pad.Up}");
        sb.AppendLine($"pad.right={pad.Right}");
        sb.AppendLine($"pad.bottom={pad.Down}");
        sb.AppendLine($"pad.left={pad.Left}");
        sb.AppendLine($"pad.advance.x={options.AdvanceAdjustX.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"pad.advance.y={options.AdvanceAdjustY.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        // Glyph / texture page settings
        sb.AppendLine("glyph.native.rendering=false");
        sb.AppendLine($"glyph.page.width={options.MaxTextureWidth}");
        sb.AppendLine($"glyph.page.height={options.MaxTextureHeight}");
        sb.AppendLine($"glyph.text={FormatChars(options.Characters)}");
        sb.AppendLine();

        // Render type: always FreeType.
        sb.AppendLine("render_type=2");
        sb.AppendLine();

        // Effects in canonical order.
        WriteColorEffect(sb, options);

        if (options.Outline > 0)
            WriteOutlineEffect(sb, options);

        if (options.GradientStartR.HasValue && options.GradientEndR.HasValue)
            WriteGradientEffect(sb, options);

        // A fully zero-offset, zero-blur shadow is intentionally treated as "no shadow"
        // (mirrors FontGeneratorOptions.HasShadow / the actual render gate), so the
        // opacity/color of such a no-op shadow are deliberately not serialized.
        if (options.ShadowOffsetX != 0 || options.ShadowOffsetY != 0 || options.ShadowBlur > 0)
            WriteShadowEffect(sb, options);

        if (options.Sdf)
            WriteDistanceFieldEffect(sb, options);

        return sb.ToString();
    }

    private static void WriteColorEffect(StringBuilder sb, FontGeneratorOptions options)
    {
        // Hiero ColorEffect stores RGB only (no alpha), so FillColorA is not serialized.
        sb.AppendLine($"effect.class={EffectPackage}ColorEffect");
        sb.AppendLine($"effect.Color={FormatColor(options.FillColorR, options.FillColorG, options.FillColorB)}");
        sb.AppendLine();
    }

    private static void WriteOutlineEffect(StringBuilder sb, FontGeneratorOptions options)
    {
        sb.AppendLine($"effect.class={EffectPackage}OutlineEffect");
        sb.AppendLine($"effect.Color={FormatColor(options.OutlineR, options.OutlineG, options.OutlineB)}");
        // KernSmith Outline is an int; Hiero Width is a float.
        sb.AppendLine($"effect.Width={options.Outline.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine("effect.Join=0");
        sb.AppendLine();
    }

    private static void WriteGradientEffect(StringBuilder sb, FontGeneratorOptions options)
    {
        sb.AppendLine($"effect.class={EffectPackage}GradientEffect");
        sb.AppendLine($"effect.Top color={FormatColor(options.GradientStartR!.Value, options.GradientStartG.GetValueOrDefault(), options.GradientStartB.GetValueOrDefault())}");
        sb.AppendLine($"effect.Bottom color={FormatColor(options.GradientEndR!.Value, options.GradientEndG.GetValueOrDefault(), options.GradientEndB.GetValueOrDefault())}");
        sb.AppendLine($"effect.Offset={options.GradientOffset.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"effect.Scale={options.GradientScale.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"effect.Cyclic={Bool(options.GradientCyclic)}");
        sb.AppendLine();
    }

    private static void WriteShadowEffect(StringBuilder sb, FontGeneratorOptions options)
    {
        // Unit mismatch: Hiero "Blur kernel size" is an OPTION whose legal values are
        // 0 (None) or >= 2 (a value of 1 is illegal), whereas KernSmith's ShadowBlur is
        // a pixel radius. Snap the pixel radius to the nearest legal Hiero kernel size.
        int blur;
        if (options.HardShadow)
        {
            // HardShadow has no Hiero equivalent: write blur 0 and warn.
            Debug.WriteLine("[HieroConfigWriter] HardShadow has no Hiero equivalent; writing blur kernel size 0.");
            blur = 0;
        }
        else if (options.ShadowBlur <= 0)
        {
            blur = 0;
        }
        else if (options.ShadowBlur == 1)
        {
            Debug.WriteLine("[HieroConfigWriter] A 1px ShadowBlur was snapped up to the smallest legal Hiero kernel size (2); this is lossy.");
            blur = 2;
        }
        else
        {
            blur = options.ShadowBlur;
        }

        sb.AppendLine($"effect.class={EffectPackage}ShadowEffect");
        sb.AppendLine($"effect.Color={FormatColor(options.ShadowR, options.ShadowG, options.ShadowB)}");
        sb.AppendLine($"effect.Opacity={options.ShadowOpacity.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"effect.X distance={options.ShadowOffsetX.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"effect.Y distance={options.ShadowOffsetY.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"effect.Blur kernel size={blur}");
        sb.AppendLine($"effect.Blur passes={options.ShadowBlurPasses}");
        sb.AppendLine();
    }

    private static void WriteDistanceFieldEffect(StringBuilder sb, FontGeneratorOptions options)
    {
        sb.AppendLine($"effect.class={EffectPackage}DistanceFieldEffect");
        sb.AppendLine("effect.Color=ffffff");
        sb.AppendLine($"effect.Scale={options.SdfScale}");
        sb.AppendLine($"effect.Spread={options.SdfSpread.ToString("0.0###", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// Formats a character set as literal Hiero glyph text, escaping ONLY an actual newline
    /// char (U+000A) as the two-char sequence <c>\n</c>. Matching real Hiero (HieroSettings.java),
    /// a literal backslash is emitted verbatim as a single <c>\</c> and is NOT escaped.
    /// </summary>
    private static string FormatChars(CharacterSet characters)
    {
        var sb = new StringBuilder();
        foreach (var cp in characters.GetCodepoints())
        {
            if (cp == '\n')
                sb.Append("\\n");
            else
                sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
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

    /// <summary>Formats RGB bytes as 6 lowercase hex chars (RRGGBB), matching Hiero's color serialization.</summary>
    private static string FormatColor(byte r, byte g, byte b)
    {
        return $"{r:x2}{g:x2}{b:x2}";
    }
}
