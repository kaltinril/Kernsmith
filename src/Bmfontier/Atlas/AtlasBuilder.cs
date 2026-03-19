using Bmfontier.Rasterizer;

namespace Bmfontier.Atlas;

internal static class AtlasBuilder
{
    public static IReadOnlyList<AtlasPage> Build(
        IReadOnlyList<RasterizedGlyph> glyphs,
        PackResult packResult,
        Padding padding,
        IAtlasEncoder encoder)
    {
        var pageWidth = packResult.PageWidth;
        var pageHeight = packResult.PageHeight;

        // Build a lookup from glyph Id (codepoint) to the rasterized glyph.
        var glyphById = new Dictionary<int, RasterizedGlyph>();
        foreach (var g in glyphs)
            glyphById[g.Codepoint] = g;

        // Group placements by page index.
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
            // Allocate page buffer (Grayscale8 = 1 byte per pixel).
            var pixelData = new byte[pageWidth * pageHeight];

            if (placementsByPage.TryGetValue(pageIndex, out var pagePlacements))
            {
                foreach (var placement in pagePlacements)
                {
                    if (!glyphById.TryGetValue(placement.Id, out var glyph))
                        continue;

                    if (glyph.BitmapData.Length == 0 || glyph.Width == 0 || glyph.Height == 0)
                        continue;

                    var destX = placement.X + padding.Left;
                    var destY = placement.Y + padding.Up;

                    // Copy glyph bitmap row-by-row into the page buffer.
                    for (var row = 0; row < glyph.Height; row++)
                    {
                        var srcOffset = row * glyph.Pitch;
                        var dstOffset = (destY + row) * pageWidth + destX;

                        // Bounds check to avoid overflows.
                        if (dstOffset < 0 || dstOffset + glyph.Width > pixelData.Length)
                            continue;
                        if (srcOffset + glyph.Width > glyph.BitmapData.Length)
                            continue;

                        Array.Copy(glyph.BitmapData, srcOffset, pixelData, dstOffset, glyph.Width);
                    }
                }
            }

            var page = new AtlasPage
            {
                PageIndex = pageIndex,
                Width = pageWidth,
                Height = pageHeight,
                PixelData = pixelData,
                Format = PixelFormat.Grayscale8
            };
            page.SetEncoder(encoder);
            pages.Add(page);
        }

        return pages;
    }
}
