using KernSmith.Atlas;
using Rect = KernSmith.Atlas.MaxRectsState.Rect;

namespace KernSmith.Tests.Packing;

/// <summary>
/// Frozen copy of the straightforward MaxRects free-list geometry (full O(F²) prune after
/// every split). <see cref="MaxRectsEquivalenceTests"/> holds the optimised production
/// code to exactly this behaviour: same placements and the same free list, in the same
/// order, after every operation. Do not "optimise" this copy — it is the oracle.
/// </summary>
internal static class ReferenceMaxRects
{
    public static (int X, int Y)? TryPlaceInPage(List<Rect> freeRects, int width, int height)
    {
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
        SplitFreeRects(freeRects, placedRect);
        PruneContainedRects(freeRects);
        return (placedRect.X, placedRect.Y);
    }

    public static void Subtract(List<Rect> freeRects, OccupiedRect r)
    {
        SplitFreeRects(freeRects, new Rect(r.X, r.Y, r.Width, r.Height));
        PruneContainedRects(freeRects);
    }

    public static void Grow(List<Rect> freeRects, int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        var growX = newWidth - oldWidth;
        var growY = newHeight - oldHeight;
        for (var i = 0; i < freeRects.Count; i++)
        {
            var fr = freeRects[i];
            var width = fr.X + fr.Width == oldWidth ? fr.Width + growX : fr.Width;
            var height = fr.Y + fr.Height == oldHeight ? fr.Height + growY : fr.Height;
            if (width != fr.Width || height != fr.Height)
                freeRects[i] = new Rect(fr.X, fr.Y, width, height);
        }

        if (growX > 0)
            freeRects.Add(new Rect(oldWidth, 0, growX, newHeight));
        if (growY > 0)
            freeRects.Add(new Rect(0, oldHeight, newWidth, growY));

        PruneContainedRects(freeRects);
    }

    private static void SplitFreeRects(List<Rect> freeRects, Rect placed)
    {
        var originalCount = freeRects.Count;
        var writeIndex = 0;

        for (var i = 0; i < originalCount; i++)
        {
            var fr = freeRects[i];
            if (placed.X >= fr.X + fr.Width || placed.X + placed.Width <= fr.X ||
                placed.Y >= fr.Y + fr.Height || placed.Y + placed.Height <= fr.Y)
            {
                freeRects[writeIndex++] = fr;
                continue;
            }

            if (placed.X > fr.X)
                freeRects.Add(new Rect(fr.X, fr.Y, placed.X - fr.X, fr.Height));
            if (placed.X + placed.Width < fr.X + fr.Width)
                freeRects.Add(new Rect(placed.X + placed.Width, fr.Y, fr.X + fr.Width - placed.X - placed.Width, fr.Height));
            if (placed.Y > fr.Y)
                freeRects.Add(new Rect(fr.X, fr.Y, fr.Width, placed.Y - fr.Y));
            if (placed.Y + placed.Height < fr.Y + fr.Height)
                freeRects.Add(new Rect(fr.X, placed.Y + placed.Height, fr.Width, fr.Y + fr.Height - placed.Y - placed.Height));
        }

        var newCount = freeRects.Count - originalCount;
        for (var i = 0; i < newCount; i++)
            freeRects[writeIndex + i] = freeRects[originalCount + i];

        freeRects.RemoveRange(writeIndex + newCount, freeRects.Count - writeIndex - newCount);
    }

    private static void PruneContainedRects(List<Rect> freeRects)
    {
        var count = freeRects.Count;
        var remove = new bool[count];

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

        var write = 0;
        for (var read = 0; read < count; read++)
        {
            if (!remove[read])
                freeRects[write++] = freeRects[read];
        }
        freeRects.RemoveRange(write, count - write);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.X >= outer.X &&
        inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width &&
        inner.Y + inner.Height <= outer.Y + outer.Height;
}
