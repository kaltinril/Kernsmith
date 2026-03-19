namespace Bmfontier.Atlas;

internal sealed class SkylinePacker : IAtlasPacker
{
    private record struct Segment(int X, int Y, int Width);

    public PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight)
    {
        // Sort glyphs by height descending (stable sort preserving original order for equal heights).
        var sorted = glyphs
            .Select((g, i) => (Glyph: g, OriginalIndex: i))
            .OrderByDescending(x => x.Glyph.Height)
            .ThenByDescending(x => x.Glyph.Width)
            .ToList();

        var placements = new List<GlyphPlacement>();
        var pages = new List<List<Segment>>(); // Skyline segments per page.

        foreach (var (glyph, _) in sorted)
        {
            var placed = false;

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var position = TryPlace(pages[pageIndex], glyph.Width, glyph.Height, maxWidth, maxHeight);
                if (position.HasValue)
                {
                    placements.Add(new GlyphPlacement(glyph.Id, pageIndex, position.Value.X, position.Value.Y));
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                // Start a new page.
                var skyline = new List<Segment> { new Segment(0, 0, maxWidth) };
                var position = TryPlace(skyline, glyph.Width, glyph.Height, maxWidth, maxHeight);
                if (position == null)
                    throw new InvalidOperationException(
                        $"Glyph {glyph.Id} ({glyph.Width}x{glyph.Height}) does not fit in a {maxWidth}x{maxHeight} page.");

                pages.Add(skyline);
                placements.Add(new GlyphPlacement(glyph.Id, pages.Count - 1, position.Value.X, position.Value.Y));
            }
        }

        return new PackResult
        {
            Placements = placements,
            PageCount = Math.Max(pages.Count, 1),
            PageWidth = maxWidth,
            PageHeight = maxHeight
        };
    }

    private static (int X, int Y)? TryPlace(List<Segment> skyline, int width, int height, int maxWidth, int maxHeight)
    {
        var bestX = -1;
        var bestY = int.MaxValue;
        var bestIndex = -1;

        // Try placing at each skyline segment start position.
        for (var i = 0; i < skyline.Count; i++)
        {
            var fitY = FitAt(skyline, i, width, height, maxWidth, maxHeight);
            if (fitY >= 0 && fitY < bestY)
            {
                bestY = fitY;
                bestX = skyline[i].X;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return null;

        // Place the glyph: add a new segment and trim/remove spanned segments.
        var newSeg = new Segment(bestX, bestY + height, width);
        var rightEdge = bestX + width;

        // Find and remove all segments that are fully or partially covered.
        var insertAt = bestIndex;
        var removeStart = bestIndex;
        var removeEnd = bestIndex; // inclusive

        for (var i = bestIndex; i < skyline.Count; i++)
        {
            var segRight = skyline[i].X + skyline[i].Width;
            if (skyline[i].X >= rightEdge)
                break;

            removeEnd = i;

            if (segRight > rightEdge)
            {
                // This segment extends beyond the glyph; shrink it.
                var trimmed = new Segment(rightEdge, skyline[i].Y, segRight - rightEdge);
                skyline[i] = trimmed;
                // Don't remove this segment, it was trimmed.
                removeEnd = i - 1;
                break;
            }
        }

        // Remove fully covered segments.
        if (removeEnd >= removeStart)
            skyline.RemoveRange(removeStart, removeEnd - removeStart + 1);

        // Insert the new segment.
        skyline.Insert(insertAt, newSeg);

        // Merge adjacent segments with the same Y.
        MergeSegments(skyline);

        return (bestX, bestY);
    }

    /// <summary>
    /// Check if a glyph of the given size can fit starting at skyline segment index <paramref name="segIndex"/>.
    /// Returns the Y position (top of the glyph) if it fits, or -1 if it doesn't.
    /// </summary>
    private static int FitAt(List<Segment> skyline, int segIndex, int width, int height, int maxWidth, int maxHeight)
    {
        var x = skyline[segIndex].X;
        if (x + width > maxWidth)
            return -1;

        var rightEdge = x + width;
        var maxY = 0;

        for (var i = segIndex; i < skyline.Count; i++)
        {
            if (skyline[i].X >= rightEdge)
                break;

            if (skyline[i].Y > maxY)
                maxY = skyline[i].Y;

            // If the glyph would exceed page height, it doesn't fit here.
            if (maxY + height > maxHeight)
                return -1;
        }

        return maxY;
    }

    private static void MergeSegments(List<Segment> skyline)
    {
        for (var i = 0; i < skyline.Count - 1; i++)
        {
            if (skyline[i].Y == skyline[i + 1].Y)
            {
                skyline[i] = new Segment(skyline[i].X, skyline[i].Y, skyline[i].Width + skyline[i + 1].Width);
                skyline.RemoveAt(i + 1);
                i--; // Re-check this position.
            }
        }
    }
}
