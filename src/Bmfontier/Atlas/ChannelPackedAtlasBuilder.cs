using Bmfontier.Rasterizer;

namespace Bmfontier.Atlas;

/// <summary>
/// Builds atlas pages with channel packing: each monochrome glyph is written
/// into a single RGBA channel (B, G, R, or A) via round-robin assignment.
/// </summary>
internal static class ChannelPackedAtlasBuilder
{
    /// <summary>
    /// BMFont channel flag values: 1=Blue, 2=Green, 4=Red, 8=Alpha.
    /// Index in this array corresponds to (glyphIndex % 4).
    /// </summary>
    private static readonly int[] ChannelFlags = [1, 2, 4, 8];

    /// <summary>
    /// Byte offset within an RGBA pixel for each channel assignment.
    /// RGBA layout: [R=0, G=1, B=2, A=3].
    /// Index corresponds to (glyphIndex % 4): 0=Blue(offset 2), 1=Green(offset 1), 2=Red(offset 0), 3=Alpha(offset 3).
    /// </summary>
    private static readonly int[] ChannelOffsets = [2, 1, 0, 3];

    public static (IReadOnlyList<AtlasPage> Pages, IReadOnlyDictionary<int, int> GlyphChannels) Build(
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

        // Assign channels round-robin by placement order.
        var glyphChannels = new Dictionary<int, int>();
        var orderedPlacements = packResult.Placements.ToList();
        for (var i = 0; i < orderedPlacements.Count; i++)
            glyphChannels[orderedPlacements[i].Id] = ChannelFlags[i % 4];

        // Group placements by page index.
        var placementsByPage = new Dictionary<int, List<(GlyphPlacement Placement, int Index)>>();
        for (var i = 0; i < orderedPlacements.Count; i++)
        {
            var p = orderedPlacements[i];
            if (!placementsByPage.TryGetValue(p.PageIndex, out var list))
            {
                list = [];
                placementsByPage[p.PageIndex] = list;
            }
            list.Add((p, i));
        }

        var pages = new List<AtlasPage>();
        for (var pageIndex = 0; pageIndex < packResult.PageCount; pageIndex++)
        {
            // Allocate RGBA page buffer (4 bytes per pixel).
            var pixelData = new byte[pageWidth * pageHeight * 4];

            if (placementsByPage.TryGetValue(pageIndex, out var pagePlacements))
            {
                foreach (var (placement, globalIndex) in pagePlacements)
                {
                    if (!glyphById.TryGetValue(placement.Id, out var glyph))
                        continue;

                    if (glyph.BitmapData.Length == 0 || glyph.Width == 0 || glyph.Height == 0)
                        continue;

                    // Channel packing requires grayscale glyphs (1 byte per pixel).
                    // RGBA glyphs (e.g., color emoji) cannot be packed into individual channels.
                    if (glyph.Format == PixelFormat.Rgba32)
                        continue;

                    var destX = placement.X + padding.Left;
                    var destY = placement.Y + padding.Up;
                    var channelOffset = ChannelOffsets[globalIndex % 4];

                    // Copy glyph bitmap pixel-by-pixel into the assigned channel.
                    for (var row = 0; row < glyph.Height; row++)
                    {
                        var srcOffset = row * glyph.Pitch;
                        var dstY = destY + row;

                        for (var col = 0; col < glyph.Width; col++)
                        {
                            var srcIdx = srcOffset + col;
                            if (srcIdx >= glyph.BitmapData.Length)
                                continue;

                            var dstIdx = (dstY * pageWidth + destX + col) * 4 + channelOffset;
                            if (dstIdx < 0 || dstIdx >= pixelData.Length)
                                continue;

                            pixelData[dstIdx] = glyph.BitmapData[srcIdx];
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

        return (pages, glyphChannels);
    }
}
