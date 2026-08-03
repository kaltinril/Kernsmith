using KernSmith.Rasterizer;

namespace KernSmith.Atlas;

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
        IReadOnlyList<RasterizedGlyph>? shadowGlyphs,
        PackResult packResult,
        Padding padding,
        ChannelConfig channelConfig,
        IAtlasEncoder encoder)
    {
        var pageWidth = packResult.PageWidth;
        var pageHeight = packResult.PageHeight;

        // When no outline layer was generated (e.g. outlineThickness=0), Outline-content
        // channels fall back to the glyph's own coverage instead of an empty/synthesized ring.
        var hasOutline = outlineGlyphs != null;

        var glyphById = new Dictionary<int, RasterizedGlyph>();
        foreach (var g in glyphs)
            glyphById[g.Codepoint] = g;

        var outlineById = new Dictionary<int, RasterizedGlyph>();
        if (outlineGlyphs != null)
        {
            foreach (var g in outlineGlyphs)
                outlineById[g.Codepoint] = g;
        }

        var shadowById = new Dictionary<int, RasterizedGlyph>();
        if (shadowGlyphs != null)
        {
            foreach (var g in shadowGlyphs)
                shadowById[g.Codepoint] = g;
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
                    shadowById.TryGetValue(placement.Id, out var shadowGlyph);
                    var hasShadow = shadowGlyph != null;

                    // Outline expands symmetrically (same width added on every side), so its
                    // offset from the base glyph is derived from the width/height delta.
                    var outlineExpandLeft = outlineGlyph != null ? (outlineGlyph.Width - glyph.Width) / 2 : 0;
                    var outlineExpandTop = outlineGlyph != null ? (outlineGlyph.Height - glyph.Height) / 2 : 0;

                    // Shadow expansion is generally asymmetric (an offset can push the shadow
                    // further to one side than the other), unlike outline's symmetric ring,
                    // so its offset is derived from the actual bearing delta baked into the
                    // shadow glyph's metrics rather than assumed to be centered.
                    var shadowExpandLeft = shadowGlyph != null ? glyph.Metrics.BearingX - shadowGlyph.Metrics.BearingX : 0;
                    var shadowExpandTop = shadowGlyph != null ? shadowGlyph.Metrics.BearingY - glyph.Metrics.BearingY : 0;

                    // The iteration canvas is the union of the base glyph and any outline/shadow
                    // footprints, expressed in a coordinate frame where the base glyph's own
                    // top-left is (0,0). This keeps neither overlay clipped even when outline
                    // and shadow expand by different amounts on each side.
                    var minX = 0;
                    var minY = 0;
                    var maxX = glyph.Width;
                    var maxY = glyph.Height;

                    if (outlineGlyph != null)
                    {
                        minX = Math.Min(minX, -outlineExpandLeft);
                        minY = Math.Min(minY, -outlineExpandTop);
                        maxX = Math.Max(maxX, -outlineExpandLeft + outlineGlyph.Width);
                        maxY = Math.Max(maxY, -outlineExpandTop + outlineGlyph.Height);
                    }
                    if (shadowGlyph != null)
                    {
                        minX = Math.Min(minX, -shadowExpandLeft);
                        minY = Math.Min(minY, -shadowExpandTop);
                        maxX = Math.Max(maxX, -shadowExpandLeft + shadowGlyph.Width);
                        maxY = Math.Max(maxY, -shadowExpandTop + shadowGlyph.Height);
                    }

                    var iterW = maxX - minX;
                    var iterH = maxY - minY;
                    // Canvas offset relative to the base glyph's own top-left.
                    var offsetX = -minX;
                    var offsetY = -minY;

                    var destX = placement.X + padding.Left;
                    var destY = placement.Y + padding.Up;

                    for (var row = 0; row < iterH; row++)
                    {
                        for (var col = 0; col < iterW; col++)
                        {
                            // Map from canvas coords back to base glyph coords.
                            var baseRow = row - offsetY;
                            var baseCol = col - offsetX;
                            var inGlyph = baseRow >= 0 && baseRow < glyph.Height && baseCol >= 0 && baseCol < glyph.Width;
                            // Coverage (alpha) drives non-color channels and grayscale glyphs.
                            var glyphValue = inGlyph ? GetGlyphAlpha(glyph, baseRow, baseCol) : (byte)0;
                            // For color (Rgba32) glyphs, Glyph content fills each output channel from
                            // the matching source component so gradient color survives; grayscale glyphs
                            // reuse the single coverage byte (see GetGlyphComponent).
                            var glyphRed = inGlyph ? GetGlyphComponent(glyph, baseRow, baseCol, 0) : (byte)0;
                            var glyphGreen = inGlyph ? GetGlyphComponent(glyph, baseRow, baseCol, 1) : (byte)0;
                            var glyphBlue = inGlyph ? GetGlyphComponent(glyph, baseRow, baseCol, 2) : (byte)0;
                            var outlineValue = outlineGlyph != null
                                ? GetOutlineAlphaDirect(outlineGlyph, row - offsetY + outlineExpandTop, col - offsetX + outlineExpandLeft)
                                : (byte)0;
                            var shadowValue = shadowGlyph != null
                                ? GetShadowAlphaDirect(shadowGlyph, row - offsetY + shadowExpandTop, col - offsetX + shadowExpandLeft)
                                : (byte)0;

                            var dstIdx = ((destY + row) * pageWidth + destX + col) * 4;
                            if (dstIdx < 0 || dstIdx + 3 >= pixelData.Length)
                                continue;

                            pixelData[dstIdx + 0] = ResolveChannel(channelConfig.Red, channelConfig.InvertRed, glyphRed, outlineValue, hasOutline, shadowValue, hasShadow);
                            pixelData[dstIdx + 1] = ResolveChannel(channelConfig.Green, channelConfig.InvertGreen, glyphGreen, outlineValue, hasOutline, shadowValue, hasShadow);
                            pixelData[dstIdx + 2] = ResolveChannel(channelConfig.Blue, channelConfig.InvertBlue, glyphBlue, outlineValue, hasOutline, shadowValue, hasShadow);
                            pixelData[dstIdx + 3] = ResolveChannel(channelConfig.Alpha, channelConfig.InvertAlpha, glyphValue, outlineValue, hasOutline, shadowValue, hasShadow);
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
    /// Reads one color component (0=R, 1=G, 2=B) of a glyph pixel. For color (Rgba32)
    /// glyphs this returns the matching channel byte so a gradient survives compositing;
    /// for grayscale glyphs there is no per-channel color, so the single coverage byte
    /// is returned (preserving the original white-glyph behavior).
    /// </summary>
    private static byte GetGlyphComponent(RasterizedGlyph glyph, int row, int col, int component)
    {
        if (glyph.Format == PixelFormat.Rgba32)
        {
            var idx = row * glyph.Pitch + col * 4 + component;
            return idx < glyph.BitmapData.Length ? glyph.BitmapData[idx] : (byte)0;
        }

        var grayIdx = row * glyph.Pitch + col;
        return grayIdx < glyph.BitmapData.Length ? glyph.BitmapData[grayIdx] : (byte)0;
    }

    /// <summary>
    /// Gets the outline alpha value directly using outline glyph coordinates.
    /// </summary>
    private static byte GetOutlineAlphaDirect(RasterizedGlyph outlineGlyph, int row, int col)
    {
        if (row < 0 || row >= outlineGlyph.Height || col < 0 || col >= outlineGlyph.Width)
            return 0;

        if (outlineGlyph.Format == PixelFormat.Rgba32)
        {
            var idx = row * outlineGlyph.Pitch + col * 4 + 3;
            return idx < outlineGlyph.BitmapData.Length ? outlineGlyph.BitmapData[idx] : (byte)0;
        }
        else
        {
            var idx = row * outlineGlyph.Pitch + col;
            return idx < outlineGlyph.BitmapData.Length ? outlineGlyph.BitmapData[idx] : (byte)0;
        }
    }

    /// <summary>
    /// Gets the shadow-only coverage alpha value directly using shadow glyph coordinates.
    /// </summary>
    private static byte GetShadowAlphaDirect(RasterizedGlyph shadowGlyph, int row, int col)
    {
        if (row < 0 || row >= shadowGlyph.Height || col < 0 || col >= shadowGlyph.Width)
            return 0;

        if (shadowGlyph.Format == PixelFormat.Rgba32)
        {
            var idx = row * shadowGlyph.Pitch + col * 4 + 3;
            return idx < shadowGlyph.BitmapData.Length ? shadowGlyph.BitmapData[idx] : (byte)0;
        }
        else
        {
            var idx = row * shadowGlyph.Pitch + col;
            return idx < shadowGlyph.BitmapData.Length ? shadowGlyph.BitmapData[idx] : (byte)0;
        }
    }

    private static byte ResolveChannel(ChannelContent content, bool invert, byte glyphValue, byte outlineValue, bool hasOutline, byte shadowValue, bool hasShadow)
    {
        var value = content switch
        {
            ChannelContent.Glyph => glyphValue,
            // With no outline layer (outlineThickness=0), fall back to glyph coverage so the
            // channel isn't empty and glyphs don't gain a phantom 1px ring.
            ChannelContent.Outline => hasOutline ? outlineValue : glyphValue,
            ChannelContent.GlyphAndOutline => hasOutline ? ThresholdEncode(glyphValue, outlineValue) : glyphValue,
            ChannelContent.Zero => (byte)0,
            ChannelContent.One => (byte)255,
            // Unlike Outline, Shadow has no glyph-coverage fallback: with no shadow configured
            // there is nothing to show, so the channel is empty (0) rather than reusing the
            // glyph's own coverage (which would defeat the point of an isolated shadow channel).
            ChannelContent.Shadow => hasShadow ? shadowValue : (byte)0,
            _ => glyphValue
        };

        return invert ? (byte)(255 - value) : value;
    }

    /// <summary>
    /// Encodes glyph and outline into a single byte using threshold encoding.
    /// Outline maps to 0-127, glyph maps to 128-255. Glyph takes precedence when both present.
    /// Shader decodes: glyph = val > 0.5 ? 2*val-1 : 0; outline = val > 0.5 ? 1 : 2*val.
    /// </summary>
    private static byte ThresholdEncode(byte glyphValue, byte outlineValue)
    {
        var outlineEncoded = (int)(outlineValue / 255.0 * 127);
        var glyphEncoded = 128 + (int)(glyphValue / 255.0 * 127);
        return (byte)Math.Max(outlineEncoded, glyphValue > 0 ? glyphEncoded : outlineEncoded);
    }
}
