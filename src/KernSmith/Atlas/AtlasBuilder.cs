using KernSmith.Rasterizer;

namespace KernSmith.Atlas;

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

        // Detect if any glyph is RGBA — if so, all pages use RGBA (4 bpp).
        var hasRgba = glyphs.Any(g => g.Format == PixelFormat.Rgba32);
        var bpp = hasRgba ? 4 : 1;
        var pageFormat = hasRgba ? PixelFormat.Rgba32 : PixelFormat.Grayscale8;

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
            var pixelData = new byte[pageWidth * pageHeight * bpp];

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

                    if (hasRgba && glyph.Format == PixelFormat.Rgba32)
                    {
                        // RGBA glyph onto RGBA page: copy 4 bytes per pixel.
                        for (var row = 0; row < glyph.Height; row++)
                        {
                            var srcOffset = row * glyph.Pitch;
                            var dstOffset = ((destY + row) * pageWidth + destX) * 4;
                            var rowBytes = glyph.Width * 4;

                            if (dstOffset < 0 || dstOffset + rowBytes > pixelData.Length)
                                continue;
                            if (srcOffset + rowBytes > glyph.BitmapData.Length)
                                continue;

                            Array.Copy(glyph.BitmapData, srcOffset, pixelData, dstOffset, rowBytes);
                        }
                    }
                    else if (hasRgba)
                    {
                        // Grayscale glyph onto RGBA page: promote to (255, 255, 255, alpha).
                        for (var row = 0; row < glyph.Height; row++)
                        {
                            for (var col = 0; col < glyph.Width; col++)
                            {
                                var srcIdx = row * glyph.Pitch + col;
                                if (srcIdx >= glyph.BitmapData.Length)
                                    continue;

                                var dstIdx = ((destY + row) * pageWidth + destX + col) * 4;
                                if (dstIdx < 0 || dstIdx + 3 >= pixelData.Length)
                                    continue;

                                var alpha = glyph.BitmapData[srcIdx];
                                pixelData[dstIdx + 0] = 255;
                                pixelData[dstIdx + 1] = 255;
                                pixelData[dstIdx + 2] = 255;
                                pixelData[dstIdx + 3] = alpha;
                            }
                        }
                    }
                    else
                    {
                        // Grayscale glyph onto grayscale page: copy 1 byte per pixel.
                        for (var row = 0; row < glyph.Height; row++)
                        {
                            var srcOffset = row * glyph.Pitch;
                            var dstOffset = (destY + row) * pageWidth + destX;

                            if (dstOffset < 0 || dstOffset + glyph.Width > pixelData.Length)
                                continue;
                            if (srcOffset + glyph.Width > glyph.BitmapData.Length)
                                continue;

                            Array.Copy(glyph.BitmapData, srcOffset, pixelData, dstOffset, glyph.Width);
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
                Format = pageFormat
            };
            page.SetEncoder(encoder);
            pages.Add(page);
        }

        return pages;
    }

    internal static void CompositeOnto(byte[] targetPixels, int targetWidth, int regionX, int regionY, byte[] sourcePixels, int sourceWidth, int sourceHeight)
    {
        for (var row = 0; row < sourceHeight; row++)
        {
            for (var col = 0; col < sourceWidth; col++)
            {
                var srcIdx = (row * sourceWidth + col) * 4;
                if (srcIdx + 3 >= sourcePixels.Length)
                    continue;

                var alpha = sourcePixels[srcIdx + 3];
                if (alpha == 0)
                    continue;

                var dstIdx = ((regionY + row) * targetWidth + (regionX + col)) * 4;
                if (dstIdx < 0 || dstIdx + 3 >= targetPixels.Length)
                    continue;

                targetPixels[dstIdx + 0] = sourcePixels[srcIdx + 0];
                targetPixels[dstIdx + 1] = sourcePixels[srcIdx + 1];
                targetPixels[dstIdx + 2] = sourcePixels[srcIdx + 2];
                targetPixels[dstIdx + 3] = sourcePixels[srcIdx + 3];
            }
        }
    }
}
