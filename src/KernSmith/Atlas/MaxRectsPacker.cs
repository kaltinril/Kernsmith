namespace KernSmith.Atlas;

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
        // Best Short Side Fit: find the free rect where the shorter leftover side is minimized.
        var bestScore = int.MaxValue;
        var bestIndex = -1;

        for (var i = 0; i < freeRects.Count; i++)
        {
            var fr = freeRects[i];
            if (width <= fr.Width && height <= fr.Height)
            {
                var leftoverX = fr.Width - width;
                var leftoverY = fr.Height - height;
                var score = Math.Min(leftoverX, leftoverY);
                if (score < bestScore)
                {
                    bestScore = score;
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
        var count = freeRects.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var fr = freeRects[i];

            // Check if they overlap.
            if (placed.X >= fr.X + fr.Width || placed.X + placed.Width <= fr.X ||
                placed.Y >= fr.Y + fr.Height || placed.Y + placed.Height <= fr.Y)
                continue;

            // They overlap; remove this free rect and add up to 4 new ones.
            freeRects.RemoveAt(i);

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
    }

    private static void PruneContainedRects(List<Rect> freeRects)
    {
        for (var i = freeRects.Count - 1; i >= 0; i--)
        {
            for (var j = freeRects.Count - 1; j >= 0; j--)
            {
                if (i == j) continue;
                if (i >= freeRects.Count || j >= freeRects.Count) continue;

                if (Contains(freeRects[j], freeRects[i]))
                {
                    freeRects.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        return inner.X >= outer.X &&
               inner.Y >= outer.Y &&
               inner.X + inner.Width <= outer.X + outer.Width &&
               inner.Y + inner.Height <= outer.Y + outer.Height;
    }
}
