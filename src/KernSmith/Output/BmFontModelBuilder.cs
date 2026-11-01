using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;

namespace KernSmith.Output;

/// <summary>
/// Assembles a <see cref="BmFontModel"/> from pipeline outputs.
/// </summary>
internal static class BmFontModelBuilder
{
    public static BmFontModel Build(
        FontInfo fontInfo,
        IReadOnlyList<RasterizedGlyph> glyphs,
        PackResult packResult,
        FontGeneratorOptions options,
        IReadOnlyDictionary<int, int>? glyphChannels = null,
        string? outputBaseName = null,
        IReadOnlyDictionary<int, GlyphPlacement>? placementOverride = null,
        int charOffsetX = 0,
        int charOffsetY = 0,
        int? overrideScaleW = null,
        int? overrideScaleH = null,
        float? effectiveSize = null,
        RasterizerFontMetrics? rasterizerFontMetrics = null,
        IReadOnlyList<ScaledKerningPair>? rasterizerKerningPairs = null,
        int superSampleLevel = 1)
    {
        // The effective ppem used for rasterization. When cell-height scaling is
        // applied, this is smaller than options.Size. For metric calculations we
        // use the effective size; for the InfoBlock we record the original.
        // Backends render at size × Dpi / 72 pixels per em, so font-unit metrics scale by it too.
        var metricSize = (float)((effectiveSize ?? options.Size) * DpiScale(options));

        var info = BuildInfo(fontInfo, options, superSampleLevel);

        var (lineHeight, baseLine) = ComputeLineMetrics(fontInfo, metricSize, rasterizerFontMetrics, options.HeightPercent);

        var common = BuildCommon(
            options, lineHeight, baseLine,
            overrideScaleW ?? packResult.PageWidth,
            overrideScaleH ?? packResult.PageHeight,
            packResult.PageCount);

        var textureExtension = TextureExtension(options);
        var pages = new List<PageEntry>();
        for (int i = 0; i < packResult.PageCount; i++)
        {
            var pageBaseName = outputBaseName ?? PageStem(fontInfo.FamilyName);
            pages.Add(new PageEntry(i, $"{pageBaseName}_{i}{textureExtension}"));
        }

        // Build a lookup from glyph Id to placement.
        IReadOnlyDictionary<int, GlyphPlacement> placementById;
        if (placementOverride != null)
        {
            placementById = placementOverride;
        }
        else
        {
            var dict = new Dictionary<int, GlyphPlacement>();
            foreach (var p in packResult.Placements)
                dict[p.Id] = p;
            placementById = dict;
        }

        // Global advance adjustment (Hiero pad.advance.x). Added in whole pixels to every
        // glyph's xadvance. Default 0 -> no change -> byte-identical metrics. The BMFont
        // char entry has no per-glyph yadvance field (horizontal layout only), so
        // AdvanceAdjustY still cannot be applied to xadvance here; instead it is surfaced
        // (when non-default) via the optional extended-metadata advanceAdjustY field below.
        var advanceAdjustX = (int)Math.Round(options.AdvanceAdjustX, MidpointRounding.AwayFromZero);

        var characters = new List<CharEntry>();
        foreach (var glyph in glyphs)
        {
            if (!placementById.TryGetValue(glyph.Codepoint, out var placement))
                continue;

            var channel = glyphChannels != null && glyphChannels.TryGetValue(glyph.Codepoint, out var ch) ? ch : 15;

            characters.Add(BuildCharEntry(
                glyph, placement, options, baseLine, advanceAdjustX, channel, charOffsetX, charOffsetY));
        }

        // Build kerning pairs, filtering to glyphs in the generated set.
        var glyphCodepoints = new HashSet<int>(glyphs.Count);
        foreach (var glyph in glyphs)
            glyphCodepoints.Add(glyph.Codepoint);
        var kerningPairs = new List<KerningEntry>();

        if (options.Kerning && rasterizerKerningPairs is not null)
        {
            // Use pre-scaled pairs directly — already in pixel values.
            foreach (var pair in rasterizerKerningPairs)
            {
                if (!glyphCodepoints.Contains(pair.LeftCodepoint) ||
                    !glyphCodepoints.Contains(pair.RightCodepoint))
                    continue;

                if (pair.Amount == 0)
                    continue;

                kerningPairs.Add(new KerningEntry(pair.LeftCodepoint, pair.RightCodepoint, pair.Amount));
            }
        }
        else if (options.Kerning && fontInfo.KerningPairs.Count > 0)
        {
            foreach (var pair in fontInfo.KerningPairs)
            {
                if (!glyphCodepoints.Contains(pair.LeftCodepoint) ||
                    !glyphCodepoints.Contains(pair.RightCodepoint))
                    continue;

                // A synthetic width scale (WidthPercent) narrows every advance, so the pair
                // adjustments scale with them; at 100 the factor is exactly 1.
                int amount = (int)Math.Round((double)pair.XAdvanceAdjustment * metricSize / fontInfo.UnitsPerEm * (options.WidthPercent / 100.0), MidpointRounding.AwayFromZero);
                if (amount == 0)
                    continue;

                kerningPairs.Add(new KerningEntry(pair.LeftCodepoint, pair.RightCodepoint, amount));
            }
        }

        var extended = BuildExtendedMetadata(options);

        return new BmFontModel
        {
            Info = info,
            Common = common,
            Pages = pages,
            Characters = characters,
            KerningPairs = kerningPairs,
            Extended = extended
        };
    }

    /// <summary>
    /// Builds one BMFont char entry from a placed glyph. Shared with
    /// <see cref="BmFontIncrementalSession"/> so incremental entries stay identical
    /// to full-generate entries.
    /// </summary>
    internal static CharEntry BuildCharEntry(
        RasterizedGlyph glyph, GlyphPlacement placement, FontGeneratorOptions options,
        int baseLine, int advanceAdjustX, int channel, int charOffsetX = 0, int charOffsetY = 0)
    {
        var xOffset = options.ForceOffsetsToZero ? 0 : glyph.Metrics.BearingX;
        var yOffset = options.ForceOffsetsToZero ? 0 : baseLine - glyph.Metrics.BearingY;

        // BMFont applies padding to the character entry: offsets shift inward,
        // dimensions expand to cover the full padded cell. This matches
        // CFontPage::AddChar in the reference implementation.
        var pad = options.Padding;

        return new CharEntry(
            Id: glyph.Codepoint,
            X: placement.X + charOffsetX,
            Y: placement.Y + charOffsetY,
            Width: glyph.Width + pad.Left + pad.Right,
            Height: glyph.Height + pad.Up + pad.Down,
            XOffset: xOffset - pad.Left,
            YOffset: yOffset - pad.Up,
            XAdvance: glyph.Metrics.Advance + advanceAdjustX,
            Page: placement.PageIndex,
            Channel: channel);
    }

    /// <summary>
    /// The default page-name stem for a family name: every character Windows forbids in a file
    /// name (path separators included) becomes '_'. The family name comes from the font, and a
    /// page name is joined to the descriptor's directory by whatever loads it, so it must stay a
    /// plain file name. The set is fixed rather than <see cref="Path.GetInvalidFileNameChars"/>
    /// so the same font names its pages the same way on every OS.
    /// </summary>
    internal static string PageStem(string familyName)
    {
        const string Forbidden = "\"*/:<>?\\|";
        if (!familyName.Any(c => c < ' ' || Forbidden.Contains(c)))
            return familyName;

        return string.Create(familyName.Length, familyName, static (chars, name) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = name[i] < ' ' || Forbidden.Contains(name[i]) ? '_' : name[i];
        });
    }

    /// <summary>
    /// Builds the BMFont info block from the parsed font and generation options.
    /// Shared with <see cref="BmFontIncrementalSession"/> so incremental models
    /// stay identical to full-generate models.
    /// </summary>
    /// <param name="superSampleLevel">The level the glyphs were rendered at
    /// (<see cref="PreparedRasterization.SuperSampleLevel"/>), written as <c>aa</c>.</param>
    internal static InfoBlock BuildInfo(FontInfo fontInfo, FontGeneratorOptions options, int superSampleLevel)
    {
        return new InfoBlock(
            Face: fontInfo.FamilyName,
            Size: options.Size,
            Bold: fontInfo.IsBold || options.Bold,
            Italic: fontInfo.IsItalic || options.Italic,
            Unicode: true,
            Smooth: options.AntiAlias != AntiAliasMode.None,
            FixedHeight: false,
            // BMFont writes its height percent and super-sample level here (the stretch clamps to 10%).
            StretchH: Math.Max(10, options.HeightPercent),
            Charset: "",
            Aa: superSampleLevel,
            Padding: options.Padding,
            Spacing: options.Spacing,
            Outline: options.Outline);
    }

    /// <summary>
    /// Computes lineHeight and base (ascent) in pixels for the common block, stretched by
    /// <paramref name="heightPercent"/> like the glyphs: BMFont rounds the stretched values up
    /// (<c>ceil(height × scaleH / 100)</c>). Shared with <see cref="BmFontIncrementalSession"/>.
    /// </summary>
    internal static (int LineHeight, int BaseLine) ComputeLineMetrics(
        FontInfo fontInfo, float metricSize, RasterizerFontMetrics? rasterizerFontMetrics, int heightPercent = 100)
    {
        var (lineHeight, baseLine) = ComputeUnstretchedLineMetrics(fontInfo, metricSize, rasterizerFontMetrics);
        if (heightPercent == 100)
            return (lineHeight, baseLine);

        // The same clamp as HeightStretchPostProcessor's.
        var percent = Math.Max(10, heightPercent);
        return (
            (int)Math.Ceiling(lineHeight * (double)percent / 100),
            (int)Math.Ceiling(baseLine * (double)percent / 100));
    }

    private static (int LineHeight, int BaseLine) ComputeUnstretchedLineMetrics(
        FontInfo fontInfo, float metricSize, RasterizerFontMetrics? rasterizerFontMetrics)
    {
        if (rasterizerFontMetrics is not null)
        {
            // Use rasterizer-provided metrics directly (already in pixels).
            return (rasterizerFontMetrics.LineHeight, rasterizerFontMetrics.Ascent);
        }

        if (fontInfo.Os2 is { } os2 && os2.WinAscent > 0)
        {
            // Use OS/2 usWinAscent/usWinDescent when available. These match what
            // Windows GDI uses for TEXTMETRIC.tmAscent/tmDescent, and therefore
            // what bmfont.exe uses for lineHeight and base. This produces consistent
            // line spacing across generators. Note: WinDescent is positive (unlike
            // hhea Descender which is negative).
            //
            // CeilingToPixels, not Math.Ceiling: with cell sizing metricSize is a float ppem chosen
            // so the win box is exactly the requested size, and a plain ceiling turns its float
            // noise into size + 1 (issue #266).
            return (
                RasterizerFontMetrics.CeilingToPixels((double)(os2.WinAscent + os2.WinDescent) * metricSize / fontInfo.UnitsPerEm),
                RasterizerFontMetrics.CeilingToPixels((double)os2.WinAscent * metricSize / fontInfo.UnitsPerEm));
        }

        return (
            RasterizerFontMetrics.CeilingToPixels((double)fontInfo.LineHeight * metricSize / fontInfo.UnitsPerEm),
            RasterizerFontMetrics.CeilingToPixels((double)fontInfo.Ascender * metricSize / fontInfo.UnitsPerEm));
    }

    /// <summary>
    /// Builds the BMFont common block. Shared with <see cref="BmFontIncrementalSession"/>.
    /// </summary>
    internal static CommonBlock BuildCommon(
        FontGeneratorOptions options, int lineHeight, int baseLine,
        int scaleW, int scaleH, int pageCount)
    {
        // When channel packing is enabled, mark the font as packed and indicate
        // that each channel holds glyph data (value 0 = glyph data per BMFont spec).
        var packed = options.ChannelPacking;

        // Per-channel configuration: write the channel content values to the common block.
        // Only emit the separated-channel layout when it was actually honored during
        // compositing (see BmFont.ShouldApplyChannelConfig). A skipped font writes default
        // channel values consistent with its baked single-composite output.
        int alphaChnl = 0, redChnl = 0, greenChnl = 0, blueChnl = 0;

        // MSDF: RGB hold glyph (distance) data and alpha is a constant one (BMFont code 4).
        // MTSDF's alpha is distance data too, so it keeps the all-zero (glyph data) layout.
        // Neither can carry a custom channel layout (rejected before rendering).
        if (options.EffectiveSdfMode == SdfMode.Msdf)
            alphaChnl = 4;

        if (BmFont.ShouldApplyChannelConfig(options) && options.Channels is { } channelConfig)
        {
            alphaChnl = (int)channelConfig.Alpha;
            redChnl = (int)channelConfig.Red;
            greenChnl = (int)channelConfig.Green;
            blueChnl = (int)channelConfig.Blue;
        }

        return new CommonBlock(
            LineHeight: lineHeight,
            Base: baseLine,
            ScaleW: scaleW,
            ScaleH: scaleH,
            Pages: pageCount,
            Packed: packed,
            AlphaChnl: alphaChnl,
            RedChnl: redChnl,
            GreenChnl: greenChnl,
            BlueChnl: blueChnl);
    }

    /// <summary>
    /// Pixels per em per point of size: every backend renders at <c>Size × Dpi / 72</c> ppem
    /// (<c>Dpi</c> 0 meaning 72, as on FreeType and Native).
    /// </summary>
    internal static double DpiScale(FontGeneratorOptions options) =>
        options.Dpi > 0 ? options.Dpi / 72.0 : 1.0;

    /// <summary>Atlas page file extension for the configured texture format.</summary>
    internal static string TextureExtension(FontGeneratorOptions options) => options.TextureFormat switch
    {
        TextureFormat.Tga => ".tga",
        TextureFormat.Dds => ".dds",
        _ => ".png"
    };

    /// <summary>
    /// Returns a copy of <paramref name="model"/> with its <see cref="ExtendedMetadata"/>
    /// sibling-linkage fields set, so a variant model and its primary can each discover the
    /// other (stock BMFont has no such field). See <see cref="AtlasVariant"/>.
    /// </summary>
    internal static BmFontModel WithVariantLinks(BmFontModel model, string? variantOf, IReadOnlyList<string>? variants)
    {
        var meta = model.Extended;
        var newMeta = new ExtendedMetadata
        {
            GeneratorVersion = meta?.GeneratorVersion ?? KernSmithVersionInfo.Version,
            SdfSpread = meta?.SdfSpread,
            SdfMode = meta?.SdfMode,
            OutlineThickness = meta?.OutlineThickness,
            GradientTopColor = meta?.GradientTopColor,
            GradientBottomColor = meta?.GradientBottomColor,
            ShadowOffsetX = meta?.ShadowOffsetX,
            ShadowOffsetY = meta?.ShadowOffsetY,
            ShadowColor = meta?.ShadowColor,
            SuperSampleLevel = meta?.SuperSampleLevel,
            VariationAxes = meta?.VariationAxes,
            ColorFont = meta?.ColorFont,
            FallbackCharacter = meta?.FallbackCharacter,
            AdvanceAdjustY = meta?.AdvanceAdjustY,
            VariantOf = variantOf,
            Variants = variants
        };

        return new BmFontModel
        {
            Info = model.Info,
            Common = model.Common,
            Pages = model.Pages,
            Characters = model.Characters,
            KerningPairs = model.KerningPairs,
            Extended = newMeta
        };
    }

    internal static ExtendedMetadata? BuildExtendedMetadata(FontGeneratorOptions options)
    {
        var version = KernSmithVersionInfo.Version;

        // Record the actually-configured SDF spread. The default (8) matches the prior
        // hardcoded value and FreeType's default, so default output is unchanged.
        var mode = options.EffectiveSdfMode;
        int? sdfSpread = mode != KernSmith.SdfMode.None
            ? (int)Math.Round(options.SdfSpread, MidpointRounding.AwayFromZero)
            : null;

        // Single-channel SDF stays identified by sdfSpread alone, as it always was.
        SdfMode? sdfMode = mode is KernSmith.SdfMode.Msdf or KernSmith.SdfMode.Mtsdf ? mode : null;

        float? outlineThickness = null;
        string? gradientTop = null;
        string? gradientBottom = null;

        if (options.PostProcessors != null)
        {
            foreach (var pp in options.PostProcessors)
            {
                if (pp is OutlinePostProcessor outline)
                {
                    outlineThickness = outline.OutlineWidth;
                }
                else if (pp is GradientPostProcessor gradient)
                {
                    gradientTop = $"{gradient.StartR:X2}{gradient.StartG:X2}{gradient.StartB:X2}";
                    gradientBottom = $"{gradient.EndR:X2}{gradient.EndG:X2}{gradient.EndB:X2}";
                }
            }
        }

        bool? colorFont = options.ColorFont ? true : null;

        Dictionary<string, float>? variationAxes = options.VariationAxes is { Count: > 0 }
            ? new Dictionary<string, float>(options.VariationAxes)
            : null;

        // FallbackCodepoint takes precedence over FallbackCharacter
        int? fallbackCharacter = options.FallbackCodepoint
            ?? (options.FallbackCharacter.HasValue ? (int)options.FallbackCharacter.Value : null);

        // Only emit AdvanceAdjustY when non-default (0) so default output stays byte-identical.
        float? advanceAdjustY = options.AdvanceAdjustY != 0 ? options.AdvanceAdjustY : (float?)null;

        var meta = new ExtendedMetadata
        {
            GeneratorVersion = version,
            SdfSpread = sdfSpread,
            SdfMode = sdfMode,
            OutlineThickness = outlineThickness,
            GradientTopColor = gradientTop,
            GradientBottomColor = gradientBottom,
            ColorFont = colorFont,
            VariationAxes = variationAxes,
            FallbackCharacter = fallbackCharacter,
            AdvanceAdjustY = advanceAdjustY
        };

        // Only include metadata when there are extended fields worth storing.
        // GeneratorVersion alone is always present, so we check for extras.
        return meta;
    }
}
