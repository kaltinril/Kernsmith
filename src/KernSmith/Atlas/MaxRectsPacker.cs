namespace KernSmith.Atlas;

/// <summary>
/// Packs glyph rectangles into atlas pages using the MaxRects algorithm
/// with Best Short Side Fit heuristic.
/// </summary>
internal sealed class MaxRectsPacker : IAtlasPacker
{
    public PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight)
    {
        // Sort glyphs by height descending (stable sort preserving original order for equal heights).
        var sorted = SortForPacking(glyphs);

        var placements = new List<GlyphPlacement>();
        var pages = new List<List<MaxRectsState.Rect>>(); // Free rects per page.

        foreach (var glyph in sorted)
        {
            var placed = false;

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var position = MaxRectsState.TryPlaceInPage(pages[pageIndex], glyph.Width, glyph.Height);
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
                var freeRects = new List<MaxRectsState.Rect> { new MaxRectsState.Rect(0, 0, maxWidth, maxHeight) };
                var position = MaxRectsState.TryPlaceInPage(freeRects, glyph.Width, glyph.Height);
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

    public PackResult PackInto(IReadOnlyList<GlyphRect> newGlyphs, IReadOnlyList<OccupiedRect> occupied,
        int pageCount, int maxWidth, int maxHeight)
    {
        var state = new MaxRectsState(maxWidth, maxHeight, Math.Max(pageCount, 1));
        state.SubtractAll(occupied);

        var sorted = SortForPacking(newGlyphs);
        var placements = new List<GlyphPlacement>(newGlyphs.Count);

        foreach (var glyph in sorted)
        {
            var placement = state.TryPlace(glyph);
            if (placement == null)
            {
                state.AddPage();
                placement = state.TryPlace(glyph);
                if (placement == null)
                    throw new InvalidOperationException(
                        $"Glyph {glyph.Id} ({glyph.Width}x{glyph.Height}) does not fit in a {maxWidth}x{maxHeight} page.");
            }

            placements.Add(placement.Value);
        }

        return new PackResult
        {
            Placements = placements,
            PageCount = state.PageCount,
            PageWidth = maxWidth,
            PageHeight = maxHeight
        };
    }

    /// <summary>
    /// Sorts glyphs by height descending, then width descending (stable sort
    /// preserving original order for ties) — the packing order used by
    /// <see cref="Pack"/>, <see cref="PackInto"/> and
    /// <see cref="BmFontIncrementalSession"/>.
    /// </summary>
    internal static List<GlyphRect> SortForPacking(IReadOnlyList<GlyphRect> glyphs)
    {
        return glyphs
            .OrderByDescending(g => g.Height)
            .ThenByDescending(g => g.Width)
            .ToList();
    }
}
