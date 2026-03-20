using Bmfontier.Rasterizer;

namespace Bmfontier.Atlas;

/// <summary>
/// Composites rasterized glyphs into RGBA atlas pages based on per-channel configuration.
/// Each channel (R, G, B, A) can independently hold glyph data, outline data,
/// combined glyph+outline, zero, or one.
/// </summary>
internal static class ChannelCompositor
{
    public static IReadOnlyList<AtlasPage> Build(
        IReadOnlyList<RasterizedGlyph> glyphs,
        IReadOnlyList<RasterizedGlyph>? outlineGlyphs,
        PackResult packResult,
        Padding padding,
        ChannelConfig channelConfig,
        IAtlasEncoder encoder)
    {
        var pageWidth = packResult.PageWidth;
        var pageHeight = packResult.PageHeight;

        var glyphById = new Dictionary<int, RasterizedGlyph>();
        foreach (var g in glyphs)
            glyphById[g.Codepoint] = g;

        var outlineById = new Dictionary<int, RasterizedGlyph>();
        if (outlineGlyphs != null)
        {
            foreach (var g in outlineGlyphs)
                outlineById[g.Codepoint] = g;
        }

        var placementsByPage = new Dictionary<int, List<GlyphPlacement>>();
        foreach (var p in packResult.Placements)
        {
            if (!placementsByPage.TryGetValue(p.PageIndex, out var list))
            {
                list = new List<GlyphPlacement>();
                placementsByPage[p.PageIndex] = list;
            }
            list.Add(p);
        }

        var pages = new List<AtlasPage>();
        for (var pageIndex = 0; pageIndex < packResult.PageCount; pageIndex++)
        {
            var pixelData = new byte[pageWidth * pageHeight * 4];

            if (placementsByPage.TryGetValue(pageIndex, out var pagePlacements))
            {
                foreach (var placement in pagePlacements)
                {
                    if (!glyphById.TryGetValue(placement.Id, out var glyph))
                        continue;

                    if (glyph.BitmapData.Length == 0 || glyph.Width == 0 || glyph.Height == 0)
                        continue;

                    outlineById.TryGetValue(placement.Id, out var outlineGlyph);

                    var destX = placement.X + padding.Left;
                    var destY = placement.Y + padding.Up;

                    for (var row = 0; row < glyph.Height; row++)
                    {
                        for (var col = 0; col < glyph.Width; col++)
                        {
                            var glyphValue = GetGlyphAlpha(glyph, row, col);
                            var outlineValue = outlineGlyph != null
                                ? GetOutlineAlpha(outlineGlyph, glyph, row, col)
                                : (byte)0;

                            var dstIdx = ((destY + row) * pageWidth + destX + col) * 4;
                            if (dstIdx < 0 || dstIdx + 3 >= pixelData.Length)
                                continue;

                            pixelData[dstIdx + 0] = ResolveChannel(channelConfig.Red, channelConfig.InvertRed, glyphValue, outlineValue);
                            pixelData[dstIdx + 1] = ResolveChannel(channelConfig.Green, channelConfig.InvertGreen, glyphValue, outlineValue);
                            pixelData[dstIdx + 2] = ResolveChannel(channelConfig.Blue, channelConfig.InvertBlue, glyphValue, outlineValue);
                            pixelData[dstIdx + 3] = ResolveChannel(channelConfig.Alpha, channelConfig.InvertAlpha, glyphValue, outlineValue);
                        }
                    }
                }
            }

            var page = new AtlasPage
            {
                PageIndex = pageIndex,
                Width = pageWidth,
                Height = pageHeight,
                PixelData = pixelData,
                Format = PixelFormat.Rgba32
            };
            page.SetEncoder(encoder);
            pages.Add(page);
        }

        return pages;
    }

    private static byte GetGlyphAlpha(RasterizedGlyph glyph, int row, int col)
    {
        if (glyph.Format == PixelFormat.Rgba32)
        {
            var idx = row * glyph.Pitch + col * 4 + 3;
            return idx < glyph.BitmapData.Length ? glyph.BitmapData[idx] : (byte)0;
        }
        else
        {
            var idx = row * glyph.Pitch + col;
            return idx < glyph.BitmapData.Length ? glyph.BitmapData[idx] : (byte)0;
        }
    }

    /// <summary>
    /// Gets the outline alpha value for a pixel. The outline glyph may be larger than
    /// the original glyph due to the outline expansion, so we map coordinates accordingly.
    /// </summary>
    private static byte GetOutlineAlpha(RasterizedGlyph outlineGlyph, RasterizedGlyph glyph, int row, int col)
    {
        // The outline glyph is expanded by outlineWidth on each side.
        // The size difference tells us the offset.
        var offsetX = (outlineGlyph.Width - glyph.Width) / 2;
        var offsetY = (outlineGlyph.Height - glyph.Height) / 2;

        var oRow = row + offsetY;
        var oCol = col + offsetX;

        if (oRow < 0 || oRow >= outlineGlyph.Height || oCol < 0 || oCol >= outlineGlyph.Width)
            return 0;

        if (outlineGlyph.Format == PixelFormat.Rgba32)
        {
            var idx = oRow * outlineGlyph.Pitch + oCol * 4 + 3;
            return idx < outlineGlyph.BitmapData.Length ? outlineGlyph.BitmapData[idx] : (byte)0;
        }
        else
        {
            var idx = oRow * outlineGlyph.Pitch + oCol;
            return idx < outlineGlyph.BitmapData.Length ? outlineGlyph.BitmapData[idx] : (byte)0;
        }
    }

    private static byte ResolveChannel(ChannelContent content, bool invert, byte glyphValue, byte outlineValue)
    {
        var value = content switch
        {
            ChannelContent.Glyph => glyphValue,
            ChannelContent.Outline => outlineValue,
            ChannelContent.GlyphAndOutline => (byte)Math.Min(255, glyphValue + outlineValue),
            ChannelContent.Zero => (byte)0,
            ChannelContent.One => (byte)255,
            _ => glyphValue
        };

        return invert ? (byte)(255 - value) : value;
    }
}
