using System.Reflection;
using Bmfontier.Atlas;
using Bmfontier.Font.Models;
using Bmfontier.Output.Model;
using Bmfontier.Rasterizer;

namespace Bmfontier.Output;

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
        IReadOnlyDictionary<int, int>? glyphChannels = null)
    {
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
            Spacing: options.Spacing);

        int lineHeight = (int)Math.Ceiling((double)fontInfo.LineHeight * options.Size / fontInfo.UnitsPerEm);
        int baseLine = (int)Math.Ceiling((double)fontInfo.Ascender * options.Size / fontInfo.UnitsPerEm);

        // When channel packing is enabled, mark the font as packed and indicate
        // that each channel holds glyph data (value 0 = glyph data per BMFont spec).
        var packed = options.ChannelPacking;
        var common = new CommonBlock(
            LineHeight: lineHeight,
            Base: baseLine,
            ScaleW: packResult.PageWidth,
            ScaleH: packResult.PageHeight,
            Pages: packResult.PageCount,
            Packed: packed,
            AlphaChnl: packed ? 0 : 0,
            RedChnl: packed ? 0 : 0,
            GreenChnl: packed ? 0 : 0,
            BlueChnl: packed ? 0 : 0);

        var textureExtension = options.TextureFormat == TextureFormat.Tga ? ".tga" : ".png";
        var pages = new List<PageEntry>();
        for (int i = 0; i < packResult.PageCount; i++)
        {
            pages.Add(new PageEntry(i, $"{fontInfo.FamilyName}_{i}{textureExtension}"));
        }

        // Build a lookup from glyph Id to placement.
        var placementById = new Dictionary<int, GlyphPlacement>();
        foreach (var p in packResult.Placements)
            placementById[p.Id] = p;

        var characters = new List<CharEntry>();
        foreach (var glyph in glyphs)
        {
            if (!placementById.TryGetValue(glyph.Codepoint, out var placement))
                continue;

            var channel = glyphChannels != null && glyphChannels.TryGetValue(glyph.Codepoint, out var ch) ? ch : 15;

            var xOffset = options.ForceOffsetsToZero ? 0 : glyph.Metrics.BearingX;
            var yOffset = options.ForceOffsetsToZero ? 0 : baseLine - glyph.Metrics.BearingY;

            characters.Add(new CharEntry(
                Id: glyph.Codepoint,
                X: placement.X,
                Y: placement.Y,
                Width: glyph.Width,
                Height: glyph.Height,
                XOffset: xOffset,
                YOffset: yOffset,
                XAdvance: glyph.Metrics.Advance,
                Page: placement.PageIndex,
                Channel: channel));
        }

        // Build kerning pairs, filtering to glyphs in the generated set.
        var glyphCodepoints = new HashSet<int>(glyphs.Select(g => g.Codepoint));
        var kerningPairs = new List<KerningEntry>();

        if (options.Kerning && fontInfo.KerningPairs.Count > 0)
        {
            foreach (var pair in fontInfo.KerningPairs)
            {
                if (!glyphCodepoints.Contains(pair.LeftCodepoint) ||
                    !glyphCodepoints.Contains(pair.RightCodepoint))
                    continue;

                int amount = (int)Math.Round((double)pair.XAdvanceAdjustment * options.Size / fontInfo.UnitsPerEm);
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
                    // The OutlinePostProcessor stores the width in a private field.
                    // We can read it via the constructor parameter that was passed.
                    var field = typeof(OutlinePostProcessor).GetField("_outlineWidth",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                        outlineThickness = (int?)field.GetValue(outline);
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

        var meta = new ExtendedMetadata
        {
            GeneratorVersion = version,
            SdfSpread = sdfSpread,
            OutlineThickness = outlineThickness,
            GradientTopColor = gradientTop,
            GradientBottomColor = gradientBottom,
            ColorFont = colorFont,
            VariationAxes = variationAxes
        };

        // Only include metadata when there are extended fields worth storing.
        // GeneratorVersion alone is always present, so we check for extras.
        return meta;
    }
}
