using System.Runtime.InteropServices;

namespace KernSmith.Atlas;

/// <summary>
/// Live MaxRects free-rectangle state, held between incremental additions.
/// Seeded with fully-free pages; existing occupancy is carved out with
/// <see cref="Subtract"/>, then new glyphs are placed with <see cref="TryPlace"/>
/// without ever moving what's already there. Pages can be appended
/// (<see cref="AddPage"/>) or enlarged in place (<see cref="Grow"/>).
/// </summary>
internal sealed class MaxRectsState
{
    internal readonly record struct Rect(int X, int Y, int Width, int Height);

    private readonly List<List<Rect>> _pages;

    /// <summary>Current page width in pixels.</summary>
    public int PageWidth { get; private set; }

    /// <summary>Current page height in pixels.</summary>
    public int PageHeight { get; private set; }

    /// <summary>Number of pages currently tracked.</summary>
    public int PageCount => _pages.Count;

    /// <summary>The free-rectangle list of one page, in list order (test hook).</summary>
    internal IReadOnlyList<Rect> GetFreeRects(int pageIndex) => _pages[pageIndex];

    public MaxRectsState(int pageWidth, int pageHeight, int initialPageCount)
    {
        if (pageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pageWidth));
        if (pageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pageHeight));
        if (initialPageCount <= 0) throw new ArgumentOutOfRangeException(nameof(initialPageCount));

        PageWidth = pageWidth;
        PageHeight = pageHeight;
        _pages = new List<List<Rect>>(initialPageCount);
        for (var i = 0; i < initialPageCount; i++)
            _pages.Add(new List<Rect> { new Rect(0, 0, pageWidth, pageHeight) });
    }

    /// <summary>
    /// Subtracts occupied regions in the canonical order — (page, Y, X, Width, Height) —
    /// so the reconstructed free-list state is deterministic and identical everywhere
    /// occupancy is replayed (<see cref="MaxRectsPacker.PackInto"/>, session resume,
    /// mid-batch rollback).
    /// </summary>
    public void SubtractAll(IReadOnlyList<OccupiedRect> occupied)
    {
        var sorted = new List<OccupiedRect>(occupied);
        sorted.Sort(static (a, b) =>
        {
            var cmp = a.PageIndex.CompareTo(b.PageIndex);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            cmp = a.Width.CompareTo(b.Width);
            if (cmp != 0) return cmp;
            return a.Height.CompareTo(b.Height);
        });
        foreach (var rect in sorted)
            Subtract(rect);
    }

    /// <summary>
    /// Carves an already-occupied region out of its page's free list (split + prune).
    /// </summary>
    public void Subtract(OccupiedRect r)
    {
        if (r.PageIndex < 0 || r.PageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(r),
                $"Occupied rect references page {r.PageIndex} but only {_pages.Count} page(s) exist.");

        var freeRects = _pages[r.PageIndex];
        var firstNew = SplitFreeRects(freeRects, new Rect(r.X, r.Y, r.Width, r.Height));
        PruneAfterSplit(freeRects, firstNew);
    }

    /// <summary>
    /// Places a glyph on the first page (in page order) with any fit, using Best Short
    /// Side Fit within that page — the same strategy as <see cref="MaxRectsPacker.Pack"/>.
    /// Returns null if no page has room.
    /// </summary>
    public GlyphPlacement? TryPlace(GlyphRect glyph)
    {
        for (var pageIndex = 0; pageIndex < _pages.Count; pageIndex++)
        {
            var position = TryPlaceInPage(_pages[pageIndex], glyph.Width, glyph.Height);
            if (position.HasValue)
                return new GlyphPlacement(glyph.Id, pageIndex, position.Value.X, position.Value.Y);
        }

        return null;
    }

    /// <summary>Appends a fully-free page.</summary>
    public void AddPage()
    {
        _pages.Add(new List<Rect> { new Rect(0, 0, PageWidth, PageHeight) });
    }

    /// <summary>
    /// Enlarges every page in place to the new dimensions. Existing placements stay
    /// valid; each page gains a right strip spanning the full new height and a bottom
    /// strip spanning the old width. Shrinking is not allowed.
    /// </summary>
    public void Grow(int newWidth, int newHeight)
    {
        if (newWidth < PageWidth)
            throw new ArgumentOutOfRangeException(nameof(newWidth),
                $"Cannot shrink page width from {PageWidth} to {newWidth}.");
        if (newHeight < PageHeight)
            throw new ArgumentOutOfRangeException(nameof(newHeight),
                $"Cannot shrink page height from {PageHeight} to {newHeight}.");

        var growX = newWidth - PageWidth;
        var growY = newHeight - PageHeight;

        foreach (var freeRects in _pages)
        {
            // Free rects touching the old right/bottom edge extend across the seam —
            // they ARE the maximal rects spanning old free space and the new strips.
            // Without this, seam-spanning free space is invisible to TryPlace and a
            // rect that fits it would force another (wasteful) grow or a no-fit.
            for (var i = 0; i < freeRects.Count; i++)
            {
                var fr = freeRects[i];
                var width = fr.X + fr.Width == PageWidth ? fr.Width + growX : fr.Width;
                var height = fr.Y + fr.Height == PageHeight ? fr.Height + growY : fr.Height;
                if (width != fr.Width || height != fr.Height)
                    freeRects[i] = new Rect(fr.X, fr.Y, width, height);
            }

            // Both strips span the full new extent on their long axis. Maximal rects may
            // overlap each other and the extended rects; Prune removes only contained ones.
            if (growX > 0)
                freeRects.Add(new Rect(PageWidth, 0, growX, newHeight));
            if (growY > 0)
                freeRects.Add(new Rect(0, PageHeight, newWidth, growY));

            PruneContainedRects(freeRects);
        }

        PageWidth = newWidth;
        PageHeight = newHeight;
    }

    // ---------------------------------------------------------------
    // Shared MaxRects geometry — also used by MaxRectsPacker.Pack.
    // ---------------------------------------------------------------

    internal static (int X, int Y)? TryPlaceInPage(List<Rect> freeRects, int width, int height)
    {
        // Best Short Side Fit: find the free rect where the shorter leftover side is minimized,
        // with the longer leftover side as a tiebreaker.
        var bestShortSide = int.MaxValue;
        var bestLongSide = int.MaxValue;
        var bestIndex = -1;

        var rects = CollectionsMarshal.AsSpan(freeRects);
        for (var i = 0; i < rects.Length; i++)
        {
            var fr = rects[i];
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

        var bestRect = rects[bestIndex];
        var placedRect = new Rect(bestRect.X, bestRect.Y, width, height);

        // Split all overlapping free rects, then prune contained rects.
        var firstNew = SplitFreeRects(freeRects, placedRect);
        PruneAfterSplit(freeRects, firstNew);

        return (placedRect.X, placedRect.Y);
    }

    /// <summary>
    /// Replaces every free rect overlapping <paramref name="placed"/> with its up-to-four
    /// leftover strips. Non-overlapping rects keep their relative order at the front; the
    /// strips follow. Returns the index of the first strip.
    /// </summary>
    internal static int SplitFreeRects(List<Rect> freeRects, Rect placed)
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
        return writeIndex;
    }

    /// <summary>
    /// The result of <see cref="PruneContainedRects"/> on a list fresh from
    /// <see cref="SplitFreeRects"/>, without testing pairs of old survivors against each other.
    /// </summary>
    /// <remarks>
    /// Only strips (<c>[firstNew, count)</c>) can be removed, so only they are tested, against
    /// every rect in the full prune's order and with its removal marks. That is exact because
    /// no old survivor (<c>[0, firstNew)</c>) can be contained by anything: (1) before the
    /// split the list was pruned — it starts as one rect and every mutation ends in a prune —
    /// so no survivor contains or duplicates another; (2) every strip lies inside the rect it
    /// was cut from, a member of that pruned list, which therefore contained no survivor.
    /// Keep both true: a mutation that skips the prune, or a split that emits a strip
    /// reaching outside its parent, breaks this and must use <see cref="PruneContainedRects"/>.
    /// </remarks>
    internal static void PruneAfterSplit(List<Rect> freeRects, int firstNew)
    {
        var count = freeRects.Count;
        if (firstNew >= count)
            return;

        var rects = CollectionsMarshal.AsSpan(freeRects);
        Span<bool> remove = count <= 256 ? stackalloc bool[count] : new bool[count];

        for (var i = firstNew; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                if (i == j || remove[j]) continue;
                if (Contains(rects[j], rects[i]))
                {
                    remove[i] = true;
                    break;
                }
            }
        }

        var write = firstNew;
        for (var read = firstNew; read < count; read++)
        {
            if (!remove[read])
                rects[write++] = rects[read];
        }
        freeRects.RemoveRange(write, count - write);
    }

    internal static void PruneContainedRects(List<Rect> freeRects)
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
