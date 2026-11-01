using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KernSmith;

/// <summary>
/// Serializes <see cref="BmfcConfig"/> to the standard BMFont/AngelCode <c>.bmfc</c> flat key=value format.
/// To write the libGDX Hiero <c>.hiero</c> format use <see cref="HieroConfigWriter"/>, or let
/// <see cref="ConfigFormatFactory.WriteConfig"/> pick the writer based on the file extension.
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"fontName={config.FontName ?? ""}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"fontFile={FormatPath(config.FontFile, relativeBasePath)}");
        // .bmfc is an integer-only format; round here since FontGeneratorOptions.Size is float.
        var fontSize = (int)Math.Round(options.Size);
        if (options.Size != Math.Round(options.Size))
            Debug.WriteLine($"[BmfcConfigWriter] Fractional font size {options.Size.ToString(CultureInfo.InvariantCulture)} was rounded to {fontSize} for the .bmfc integer fontSize field; this is lossy.");
        if (options.MatchCharHeight)
            fontSize = -fontSize;
        sb.AppendLine(CultureInfo.InvariantCulture, $"fontSize={fontSize}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"isBold={(options.Bold ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"isItalic={(options.Italic ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"useSmoothing={(options.AntiAlias == AntiAliasMode.None ? 0 : 1)}");
        // BMFont's `aa` is the supersampling factor (1, 2 or 4), not the anti-alias mode --
        // which is also how BmfcConfigReader reads it. The mode itself rides on useSmoothing
        // (for None) plus the `antiAlias` extension below (for Light/Lcd).
        sb.AppendLine(CultureInfo.InvariantCulture, $"aa={options.SuperSampleLevel}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"useHinting={(options.EnableHinting ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"scaleH={options.HeightPercent}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"dontIncludeKerningPairs={(options.Kerning ? 0 : 1)}");
        sb.AppendLine();

        // Character alignment
        sb.AppendLine("# character alignment");
        var pad = options.Padding;
        sb.AppendLine(CultureInfo.InvariantCulture, $"paddingUp={pad.Up}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"paddingDown={pad.Down}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"paddingRight={pad.Right}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"paddingLeft={pad.Left}");
        var spc = options.Spacing;
        sb.AppendLine(CultureInfo.InvariantCulture, $"spacingHoriz={spc.Horizontal}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"spacingVert={spc.Vertical}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"useFixedHeight={(options.EqualizeCellHeights ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"forceZero={(options.ForceOffsetsToZero ? 1 : 0)}");
        sb.AppendLine();

        // Output file
        sb.AppendLine("# output file");
        sb.AppendLine(CultureInfo.InvariantCulture, $"outWidth={options.MaxTextureWidth}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"outHeight={options.MaxTextureHeight}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"fontDescFormat={FormatOutputFormat(config.OutputFormat)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"textureFormat={FormatTextureFormat(options.TextureFormat)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"fourChnlPacked={(options.ChannelPacking ? 1 : 0)}");
        WriteChannelConfig(sb, options.Channels);
        sb.AppendLine();

        // Outline
        sb.AppendLine("# outline");
        sb.AppendLine(CultureInfo.InvariantCulture, $"outlineThickness={options.Outline}");
        sb.AppendLine();

        // Selected chars
        sb.AppendLine("# selected chars");
        sb.AppendLine(CultureInfo.InvariantCulture, $"chars={FormatChars(options.Characters)}");
        sb.AppendLine();

        // Output path (if set)
        if (!string.IsNullOrEmpty(config.OutputPath))
        {
            sb.AppendLine("# output path");
            sb.AppendLine(CultureInfo.InvariantCulture, $"outputPath={FormatPath(config.OutputPath, relativeBasePath)}");
            sb.AppendLine();
        }

        // kernsmith extensions -- only include non-default values
        var extensions = new StringBuilder();
        if (options.GradientStartR.HasValue && options.GradientEndR.HasValue)
        {
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gradientTop={FormatColor(options.GradientStartR.Value, options.GradientStartG.GetValueOrDefault(), options.GradientStartB.GetValueOrDefault())}");
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gradientBottom={FormatColor(options.GradientEndR.Value, options.GradientEndG.GetValueOrDefault(), options.GradientEndB.GetValueOrDefault())}");
        }
        if (options.GradientAngle != 90f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gradientAngle={options.GradientAngle.ToString(CultureInfo.InvariantCulture)}");
        if (options.GradientMidpoint != 0.5f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gradientMidpoint={options.GradientMidpoint.ToString(CultureInfo.InvariantCulture)}");
        if (options.GradientOffset != 0f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gradientOffset={options.GradientOffset.ToString(CultureInfo.InvariantCulture)}");
        if (options.GradientScale != 1f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gradientScale={options.GradientScale.ToString(CultureInfo.InvariantCulture)}");
        if (options.GradientCyclic)
            extensions.AppendLine("gradientCyclic=1");
        if (options.ShadowOffsetX != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowOffsetX={options.ShadowOffsetX}");
        if (options.ShadowOffsetY != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowOffsetY={options.ShadowOffsetY}");
        if (options.ShadowR != 0 || options.ShadowG != 0 || options.ShadowB != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowColor={FormatColor(options.ShadowR, options.ShadowG, options.ShadowB)}");
        if (options.ShadowBlur != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowBlur={options.ShadowBlur}");
        if (options.ShadowBlurKernelSize != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowBlurKernelSize={options.ShadowBlurKernelSize}");
        if (options.ShadowBlurPasses != 1)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowBlurPasses={options.ShadowBlurPasses}");
        if (options.Variants is { Count: > 0 } && options.Variants.Any(v => v.Name == "shadow" && v.Kind == AtlasVariantKind.ShadowSilhouette))
            extensions.AppendLine("variantShadow=1");
        if (options.FillColorR != 255 || options.FillColorG != 255 || options.FillColorB != 255 || options.FillColorA != 255)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"fillColor={options.FillColorR:X2}{options.FillColorG:X2}{options.FillColorB:X2}{options.FillColorA:X2}");
        if (options.SdfSpread != 8f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"sdfSpread={options.SdfSpread.ToString(CultureInfo.InvariantCulture)}");
        if (options.Gamma != 1.8f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"gamma={options.Gamma.ToString(CultureInfo.InvariantCulture)}");
        if (options.AdvanceAdjustX != 0f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"advanceAdjustX={options.AdvanceAdjustX.ToString(CultureInfo.InvariantCulture)}");
        if (options.AdvanceAdjustY != 0f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"advanceAdjustY={options.AdvanceAdjustY.ToString(CultureInfo.InvariantCulture)}");
        if (options.Outline > 0 && (options.OutlineR != 0 || options.OutlineG != 0 || options.OutlineB != 0))
            extensions.AppendLine(CultureInfo.InvariantCulture, $"outlineColor={FormatColor(options.OutlineR, options.OutlineG, options.OutlineB)}");
        if (options.Outline > 0 && options.OutlineShape != OutlineShape.Round)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"outlineShape={options.OutlineShape.ToString().ToLowerInvariant()}");
        if (options.OutlineMode != OutlineMode.Effect)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"outlineMode={options.OutlineMode.ToString().ToLowerInvariant()}");
        if (options.WidthPercent != 100)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"widthPercent={options.WidthPercent}");
        if (!options.ItalicAngle.Equals(12f))
            extensions.AppendLine(CultureInfo.InvariantCulture, $"italicAngle={options.ItalicAngle.ToString(CultureInfo.InvariantCulture)}");
        if (options.Sdf)
            extensions.AppendLine("useSdf=1");
        if (options.SdfScale != 1)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"sdfScale={options.SdfScale}");
        if (options.SdfMode != SdfMode.None)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"sdfMode={options.SdfMode.ToString().ToLowerInvariant()}");
        // None is already conveyed by useSmoothing=0 and Grayscale is the default, so only the
        // two modes the standard keys cannot express need an extension.
        if (options.AntiAlias is AntiAliasMode.Light or AntiAliasMode.Lcd)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"antiAlias={options.AntiAlias.ToString().ToLowerInvariant()}");
        if (options.LcdFilter != LcdFilter.Default)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"lcdFilter={options.LcdFilter.ToString().ToLowerInvariant()}");
        if (options.LcdSubpixelOrder != LcdSubpixelOrder.Rgb)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"lcdOrder={options.LcdSubpixelOrder.ToString().ToLowerInvariant()}");
        if (options.ShadowOpacity != 1.0f)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"shadowOpacity={options.ShadowOpacity.ToString(CultureInfo.InvariantCulture)}");
        if (options.HardShadow)
            extensions.AppendLine("hardShadow=1");
        if (options.VariationAxes is { Count: > 0 })
            extensions.AppendLine(CultureInfo.InvariantCulture, $"variationAxes={FormatVariationAxes(options.VariationAxes)}");
        if (options.PackingAlgorithm != PackingAlgorithm.MaxRects)
            extensions.AppendLine("packer=skyline");
        // FallbackCodepoint takes precedence over FallbackCharacter. The reader takes a single
        // character literally, so the codepoint is written with at least two digits ("05").
        if (options.FallbackCodepoint.HasValue)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"fallbackChar={options.FallbackCodepoint.Value:00}");
        else if (options.FallbackCharacter.HasValue)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"fallbackChar={(int)options.FallbackCharacter.Value:00}");
        if (options.ColorFont)
            extensions.AppendLine("colorFont=1");
        if (options.ColorPaletteIndex != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"colorPalette={options.ColorPaletteIndex}");
        if (options.FaceIndex != 0)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"faceIndex={options.FaceIndex}");
        if (options.Dpi != 72)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"dpi={options.Dpi}");
        if (!options.PowerOfTwo)
            extensions.AppendLine("powerOfTwo=0");
        if (options.AutofitTexture)
            extensions.AppendLine("autofit=1");
        if (options.Backend != RasterizerBackend.FreeType)
            extensions.AppendLine(CultureInfo.InvariantCulture, $"rasterizer={options.Backend.ToString().ToLowerInvariant()}");
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

    /// <summary>
    /// Formats variable-font axis settings as a comma-separated <c>tag:value</c> list,
    /// e.g. <c>wght:700,wdth:87.5</c>.
    /// </summary>
    private static string FormatVariationAxes(Dictionary<string, float> axes) =>
        string.Join(',', axes.Select(a =>
            $"{a.Key}:{a.Value.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// Writes the per-channel content and inversion keys, mirroring the keys
    /// <see cref="BmfcConfigReader"/> parses so a channel configuration survives a
    /// write/read round trip.
    /// </summary>
    /// <remarks>
    /// Nothing is written for a null or all-default configuration. Emitting the keys
    /// unconditionally would turn an unset <see cref="FontGeneratorOptions.Channels"/> into a
    /// non-null default on round trip, which downstream code reads as "channel routing was
    /// requested" -- so silence here keeps default output byte-identical.
    /// <para>
    /// <see cref="ChannelContent"/> values map 1:1 onto BMFont's numbering, except
    /// <see cref="ChannelContent.Shadow"/> (5), which is a KernSmith extension outside
    /// BMFont's documented 0-4 range. It is written faithfully so the value survives the round
    /// trip; BMFont.exe itself does not define a meaning for it.
    /// </para>
    /// </remarks>
    private static void WriteChannelConfig(StringBuilder sb, ChannelConfig? channels)
    {
        if (channels is null || channels.IsDefault)
            return;

        sb.AppendLine(CultureInfo.InvariantCulture, $"alphaChnl={(int)channels.Alpha}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"redChnl={(int)channels.Red}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"greenChnl={(int)channels.Green}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"blueChnl={(int)channels.Blue}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"invA={(channels.InvertAlpha ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"invR={(channels.InvertRed ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"invG={(channels.InvertGreen ? 1 : 0)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"invB={(channels.InvertBlue ? 1 : 0)}");
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
