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
        FontGeneratorOptions options)
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

        var common = new CommonBlock(
            LineHeight: lineHeight,
            Base: baseLine,
            ScaleW: packResult.PageWidth,
            ScaleH: packResult.PageHeight,
            Pages: packResult.PageCount);

        var pages = new List<PageEntry>();
        for (int i = 0; i < packResult.PageCount; i++)
        {
            pages.Add(new PageEntry(i, $"{fontInfo.FamilyName}_{i}.png"));
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

            characters.Add(new CharEntry(
                Id: glyph.Codepoint,
                X: placement.X,
                Y: placement.Y,
                Width: glyph.Width,
                Height: glyph.Height,
                XOffset: glyph.Metrics.BearingX,
                YOffset: baseLine - glyph.Metrics.BearingY,
                XAdvance: glyph.Metrics.Advance,
                Page: placement.PageIndex,
                Channel: 15));
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

        return new BmFontModel
        {
            Info = info,
            Common = common,
            Pages = pages,
            Characters = characters,
            KerningPairs = kerningPairs
        };
    }
}
