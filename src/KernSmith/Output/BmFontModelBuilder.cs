using System.Reflection;
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
        IReadOnlyList<ScaledKerningPair>? rasterizerKerningPairs = null)
    {
        // The effective ppem used for rasterization. When cell-height scaling is
        // applied, this is smaller than options.Size. For metric calculations we
        // use the effective size; for the InfoBlock we record the original.
        var metricSize = effectiveSize ?? options.Size;

        var info = new InfoBlock(
            Face: fontInfo.FamilyName,
            Size: options.Size,
            Bold: fontInfo.IsBold || options.Bold,
            Italic: fontInfo.IsItalic || options.Italic,
            Unicode: true,
            Smooth: options.AntiAlias != AntiAliasMode.None,
            FixedHeight: false,
            StretchH: 100,
            Charset: "",
            Aa: 1,
            Padding: options.Padding,
            Spacing: options.Spacing,
            Outline: options.Outline);

        int lineHeight;
        int baseLine;

        if (rasterizerFontMetrics is not null)
        {
            // Use rasterizer-provided metrics directly (already in pixels).
            lineHeight = rasterizerFontMetrics.LineHeight;
            baseLine = rasterizerFontMetrics.Ascent;
        }
        else if (fontInfo.Os2 is { } os2 && os2.WinAscent > 0)
        {
            // Use OS/2 usWinAscent/usWinDescent when available. These match what
            // Windows GDI uses for TEXTMETRIC.tmAscent/tmDescent, and therefore
            // what bmfont.exe uses for lineHeight and base. This produces consistent
            // line spacing across generators. Note: WinDescent is positive (unlike
            // hhea Descender which is negative).
            lineHeight = (int)Math.Ceiling((double)(os2.WinAscent + os2.WinDescent) * metricSize / fontInfo.UnitsPerEm);
            baseLine = (int)Math.Ceiling((double)os2.WinAscent * metricSize / fontInfo.UnitsPerEm);
        }
        else
        {
            lineHeight = (int)Math.Ceiling((double)fontInfo.LineHeight * metricSize / fontInfo.UnitsPerEm);
            baseLine = (int)Math.Ceiling((double)fontInfo.Ascender * metricSize / fontInfo.UnitsPerEm);
        }

        // When channel packing is enabled, mark the font as packed and indicate
        // that each channel holds glyph data (value 0 = glyph data per BMFont spec).
        var packed = options.ChannelPacking;

        // Per-channel configuration: write the channel content values to the common block.
        int alphaChnl = 0, redChnl = 0, greenChnl = 0, blueChnl = 0;
        if (options.Channels is { } channelConfig && !channelConfig.IsDefault)
        {
            alphaChnl = (int)channelConfig.Alpha;
            redChnl = (int)channelConfig.Red;
            greenChnl = (int)channelConfig.Green;
            blueChnl = (int)channelConfig.Blue;
        }

        var common = new CommonBlock(
            LineHeight: lineHeight,
            Base: baseLine,
            ScaleW: overrideScaleW ?? packResult.PageWidth,
            ScaleH: overrideScaleH ?? packResult.PageHeight,
            Pages: packResult.PageCount,
            Packed: packed,
            AlphaChnl: alphaChnl,
            RedChnl: redChnl,
            GreenChnl: greenChnl,
            BlueChnl: blueChnl);

        var textureExtension = options.TextureFormat switch
        {
            TextureFormat.Tga => ".tga",
            TextureFormat.Dds => ".dds",
            _ => ".png"
        };
        var pages = new List<PageEntry>();
        for (int i = 0; i < packResult.PageCount; i++)
        {
            var pageBaseName = outputBaseName ?? fontInfo.FamilyName;
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

        var characters = new List<CharEntry>();
        foreach (var glyph in glyphs)
        {
            if (!placementById.TryGetValue(glyph.Codepoint, out var placement))
                continue;

            var channel = glyphChannels != null && glyphChannels.TryGetValue(glyph.Codepoint, out var ch) ? ch : 15;

            var xOffset = options.ForceOffsetsToZero ? 0 : glyph.Metrics.BearingX;
            var yOffset = options.ForceOffsetsToZero ? 0 : baseLine - glyph.Metrics.BearingY;

            // BMFont applies padding to the character entry: offsets shift inward,
            // dimensions expand to cover the full padded cell. This matches
            // CFontPage::AddChar in the reference implementation.
            var pad = options.Padding;
            xOffset -= pad.Left;
            yOffset -= pad.Up;
            var charWidth = glyph.Width + pad.Left + pad.Right;
            var charHeight = glyph.Height + pad.Up + pad.Down;

            characters.Add(new CharEntry(
                Id: glyph.Codepoint,
                X: placement.X + charOffsetX,
                Y: placement.Y + charOffsetY,
                Width: charWidth,
                Height: charHeight,
                XOffset: xOffset,
                YOffset: yOffset,
                XAdvance: glyph.Metrics.Advance,
                Page: placement.PageIndex,
                Channel: channel));
        }

        // Build kerning pairs, filtering to glyphs in the generated set.
        var glyphCodepoints = new HashSet<int>(glyphs.Select(g => g.Codepoint));
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

                int amount = (int)Math.Round((double)pair.XAdvanceAdjustment * metricSize / fontInfo.UnitsPerEm, MidpointRounding.AwayFromZero);
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

    private static ExtendedMetadata? BuildExtendedMetadata(FontGeneratorOptions options)
    {
        var version = typeof(BmFontModelBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(BmFontModelBuilder).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        int? sdfSpread = options.Sdf ? 8 : null; // FreeType SDF default spread

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

        var meta = new ExtendedMetadata
        {
            GeneratorVersion = version,
            SdfSpread = sdfSpread,
            OutlineThickness = outlineThickness,
            GradientTopColor = gradientTop,
            GradientBottomColor = gradientBottom,
            ColorFont = colorFont,
            VariationAxes = variationAxes,
            FallbackCharacter = fallbackCharacter
        };

        // Only include metadata when there are extended fields worth storing.
        // GeneratorVersion alone is always present, so we check for extras.
        return meta;
    }
}
