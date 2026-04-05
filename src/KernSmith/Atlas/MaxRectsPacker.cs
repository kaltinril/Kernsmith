namespace KernSmith.Atlas;

/// <summary>
/// Packs glyph rectangles into atlas pages using the MaxRects algorithm
/// with Best Short Side Fit heuristic.
/// </summary>
internal sealed class MaxRectsPacker : IAtlasPacker
{
    private record struct Rect(int X, int Y, int Width, int Height);

    public PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight)
    {
        // Sort glyphs by height descending (stable sort preserving original order for equal heights).
        var sorted = glyphs
            .Select((g, i) => (Glyph: g, OriginalIndex: i))
            .OrderByDescending(x => x.Glyph.Height)
            .ThenByDescending(x => x.Glyph.Width)
            .ToList();

        var placements = new List<GlyphPlacement>();
        var pages = new List<List<Rect>>(); // Free rects per page.

        foreach (var (glyph, _) in sorted)
        {
            var placed = false;

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var position = TryPlace(pages[pageIndex], glyph.Width, glyph.Height);
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
                var freeRects = new List<Rect> { new Rect(0, 0, maxWidth, maxHeight) };
                var position = TryPlace(freeRects, glyph.Width, glyph.Height);
                if (position == null)
                    throw new InvalidOperationException(
                        $"Glyph {glyph.Id} ({glyph.Width}x{glyph.Height}) does not fit in a {maxWidth}x{maxHeight} page.");

                pages.Add(freeRects);
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

    private static (int X, int Y)? TryPlace(List<Rect> freeRects, int width, int height)
    {
        // Best Short Side Fit: find the free rect where the shorter leftover side is minimized,
        // with the longer leftover side as a tiebreaker.
        var bestShortSide = int.MaxValue;
        var bestLongSide = int.MaxValue;
        var bestIndex = -1;

        for (var i = 0; i < freeRects.Count; i++)
        {
            var fr = freeRects[i];
            if (width <= fr.Width && height <= fr.Height)
            {
                var leftoverX = fr.Width - width;
                var leftoverY = fr.Height - height;
                var shortSide = Math.Min(leftoverX, leftoverY);
                var longSide = Math.Max(leftoverX, leftoverY);
                if (shortSide < bestShortSide || (shortSide == bestShortSide && longSide < bestLongSide))
                {
                    bestShortSide = shortSide;
                    bestLongSide = longSide;
                    bestIndex = i;
                }
            }
        }

        if (bestIndex < 0)
            return null;

        var bestRect = freeRects[bestIndex];
        var placedRect = new Rect(bestRect.X, bestRect.Y, width, height);

        // Split all overlapping free rects.
        SplitFreeRects(freeRects, placedRect);

        // Prune contained rects.
        PruneContainedRects(freeRects);

        return (placedRect.X, placedRect.Y);
    }

    private static void SplitFreeRects(List<Rect> freeRects, Rect placed)
    {
        var originalCount = freeRects.Count;
        var writeIndex = 0;

        for (var i = 0; i < originalCount; i++)
        {
            var fr = freeRects[i];

            // Check if they overlap.
            if (placed.X >= fr.X + fr.Width || placed.X + placed.Width <= fr.X ||
                placed.Y >= fr.Y + fr.Height || placed.Y + placed.Height <= fr.Y)
            {
                // No overlap — keep this rect.
                freeRects[writeIndex++] = fr;
                continue;
            }

            // They overlap; add up to 4 new rects at the end.
            // Left strip.
            if (placed.X > fr.X)
                freeRects.Add(new Rect(fr.X, fr.Y, placed.X - fr.X, fr.Height));

            // Right strip.
            if (placed.X + placed.Width < fr.X + fr.Width)
                freeRects.Add(new Rect(placed.X + placed.Width, fr.Y, fr.X + fr.Width - placed.X - placed.Width, fr.Height));

            // Top strip.
            if (placed.Y > fr.Y)
                freeRects.Add(new Rect(fr.X, fr.Y, fr.Width, placed.Y - fr.Y));

            // Bottom strip.
            if (placed.Y + placed.Height < fr.Y + fr.Height)
                freeRects.Add(new Rect(fr.X, placed.Y + placed.Height, fr.Width, fr.Y + fr.Height - placed.Y - placed.Height));
        }

        // Move the newly added rects right after the survivors, then trim.
        var newCount = freeRects.Count - originalCount;
        for (var i = 0; i < newCount; i++)
            freeRects[writeIndex + i] = freeRects[originalCount + i];

        freeRects.RemoveRange(writeIndex + newCount, freeRects.Count - writeIndex - newCount);
    }

    private static void PruneContainedRects(List<Rect> freeRects)
    {
        var count = freeRects.Count;
        // Use a simple bool array to mark rects for removal.
        Span<bool> remove = count <= 256 ? stackalloc bool[count] : new bool[count];

        for (var i = 0; i < count; i++)
        {
            if (remove[i]) continue;
            for (var j = 0; j < count; j++)
            {
                if (i == j || remove[j]) continue;
                if (Contains(freeRects[j], freeRects[i]))
                {
                    remove[i] = true;
                    break;
                }
            }
        }

        // Compact: shift survivors down in-place.
        var write = 0;
        for (var read = 0; read < count; read++)
        {
            if (!remove[read])
                freeRects[write++] = freeRects[read];
        }
        freeRects.RemoveRange(write, count - write);
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        return inner.X >= outer.X &&
               inner.Y >= outer.Y &&
               inner.X + inner.Width <= outer.X + outer.Width &&
               inner.Y + inner.Height <= outer.Y + outer.Height;
    }
}
