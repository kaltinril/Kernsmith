namespace Bmfontier.Atlas;

/// <summary>
/// Options controlling atlas size estimation.
/// </summary>
internal sealed record AtlasSizingOptions
{
    /// <summary>
    /// Expected packing efficiency (0.50 to 0.99). Default 0.90 is tuned for MaxRects BSSF with font glyphs.
    /// </summary>
    public float PackingEfficiency { get; init; } = 0.90f;

    /// <summary>Whether atlas dimensions must be powers of two.</summary>
    public bool PowerOfTwo { get; init; }

    /// <summary>Whether non-square atlas dimensions are allowed.</summary>
    public bool AllowNonSquare { get; init; }

    /// <summary>Maximum aspect ratio (wider/narrower) for non-square atlases. Default 2.0 (e.g., 512x256 is ok, 512x128 is not).</summary>
    public float MaxAspectRatio { get; init; } = 2.0f;

    /// <summary>Maximum atlas width in pixels.</summary>
    public int MaxWidth { get; init; } = 1024;

    /// <summary>Maximum atlas height in pixels.</summary>
    public int MaxHeight { get; init; } = 1024;

    /// <summary>Minimum atlas dimension in pixels.</summary>
    public int MinSize { get; init; } = 64;

    /// <summary>Whether channel packing is enabled (4 glyphs per pixel via RGBA channels).</summary>
    public bool ChannelPacking { get; init; }

    /// <summary>Whether all glyph cells have been equalized to the same height.</summary>
    public bool EqualizedCellHeights { get; init; }
}

/// <summary>
/// Predicts the minimum atlas texture size using shelf-packing estimation.
/// Stateless and thread-safe — all state flows through parameters.
/// </summary>
/// <remarks>
/// Operates on GlyphRects that already include padding and spacing.
/// Do not add padding inside the estimator.
/// </remarks>
internal static class AtlasSizeEstimator
{
    /// <summary>Safety margin applied to shelf height estimates to account for FFDH vs MaxRects differences.</summary>
    private const double SafetyMargin = 1.05;

    /// <summary>
    /// Estimates the optimal atlas size for the given glyph rectangles.
    /// </summary>
    /// <param name="glyphRects">Glyph rectangles including padding and spacing.</param>
    /// <param name="options">Sizing options.</param>
    /// <returns>Estimated (width, height) for the atlas.</returns>
    public static (int Width, int Height) Estimate(IReadOnlyList<GlyphRect> glyphRects, AtlasSizingOptions options)
    {
        var efficiency = Math.Clamp(options.PackingEfficiency, 0.50f, 0.99f);
        var minSize = Math.Max(options.MinSize, 1);

        // Filter out zero-area glyphs.
        var rects = new List<GlyphRect>();
        foreach (var r in glyphRects)
        {
            if (r.Width > 0 && r.Height > 0)
                rects.Add(r);
        }

        // Empty glyph list.
        if (rects.Count == 0)
        {
            var emptySize = options.PowerOfTwo ? NextPowerOfTwo(minSize) : minSize;
            return (emptySize, emptySize);
        }

        // Compute total area (long to avoid int32 overflow), max dimensions.
        long totalArea = 0;
        var maxGlyphWidth = 0;
        var maxGlyphHeight = 0;

        foreach (var r in rects)
        {
            totalArea += (long)r.Width * r.Height;
            if (r.Width > maxGlyphWidth) maxGlyphWidth = r.Width;
            if (r.Height > maxGlyphHeight) maxGlyphHeight = r.Height;
        }

        // Channel packing: 4 glyphs per pixel position.
        if (options.ChannelPacking)
            totalArea = (totalArea + 3) / 4; // Ceiling division.

        // Equalized cell heights: 1D strip packing fast path.
        if (options.EqualizedCellHeights)
            return EstimateEqualizedCells(rects, options, maxGlyphWidth, maxGlyphHeight, minSize);

        // Sort by height descending for shelf estimation.
        var sorted = rects.OrderByDescending(r => r.Height).ThenByDescending(r => r.Width).ToArray();

        // Area-based lower bound.
        var areaLowerBound = (int)Math.Ceiling(Math.Sqrt((double)totalArea / efficiency));
        var lowerBound = Math.Max(areaLowerBound, Math.Max(maxGlyphWidth, Math.Max(maxGlyphHeight, minSize)));

        if (!options.AllowNonSquare)
        {
            // Square atlas.
            var side = lowerBound;

            // Verify with shelf estimate: the side must also accommodate the estimated height.
            var shelfH = EstimateShelfHeight(sorted, side);
            side = Math.Max(side, shelfH);

            if (options.PowerOfTwo)
                side = NextPowerOfTwo(side);

            side = Math.Max(side, minSize);
            side = Math.Min(side, Math.Min(options.MaxWidth, options.MaxHeight));

            return (side, side);
        }

        // Non-square allowed.
        if (options.PowerOfTwo)
            return EstimatePotNonSquare(sorted, options, maxGlyphWidth, lowerBound, minSize);

        return EstimateNpotNonSquare(sorted, options, maxGlyphWidth, lowerBound, minSize);
    }

    /// <summary>
    /// Simulates First-Fit Decreasing Height (FFDH) shelf packing in O(N).
    /// Glyphs must be pre-sorted by height descending.
    /// </summary>
    /// <param name="sortedGlyphs">Glyphs sorted by height descending.</param>
    /// <param name="targetWidth">Target atlas width.</param>
    /// <returns>Estimated total height with safety margin applied.</returns>
    internal static int EstimateShelfHeight(ReadOnlySpan<GlyphRect> sortedGlyphs, int targetWidth)
    {
        if (sortedGlyphs.Length == 0 || targetWidth <= 0)
            return 0;

        var totalHeight = 0;
        var shelfWidth = 0;
        var shelfHeight = 0;

        foreach (var glyph in sortedGlyphs)
        {
            if (shelfWidth + glyph.Width > targetWidth)
            {
                totalHeight += shelfHeight;
                shelfWidth = 0;
                shelfHeight = 0;
            }

            shelfWidth += glyph.Width;
            if (glyph.Height > shelfHeight)
                shelfHeight = glyph.Height;
        }

        totalHeight += shelfHeight; // Close last shelf.

        // Apply safety margin.
        return (int)Math.Ceiling(totalHeight * SafetyMargin);
    }

    /// <summary>
    /// Fast path for equalized cell heights (1D strip packing).
    /// </summary>
    private static (int Width, int Height) EstimateEqualizedCells(
        List<GlyphRect> rects, AtlasSizingOptions options,
        int maxGlyphWidth, int maxGlyphHeight, int minSize)
    {
        var cellWidth = maxGlyphWidth;
        var cellHeight = maxGlyphHeight;
        var n = options.ChannelPacking ? (rects.Count + 3) / 4 : rects.Count;

        // Find optimal width.
        var bestWidth = options.MaxWidth;
        var bestHeight = options.MaxHeight;
        long bestArea = (long)bestWidth * bestHeight + 1;

        if (options.PowerOfTwo)
        {
            for (var w = NextPowerOfTwo(cellWidth); w <= options.MaxWidth; w *= 2)
            {
                var cellsPerRow = w / cellWidth;
                if (cellsPerRow <= 0) continue;

                var rows = (n + cellsPerRow - 1) / cellsPerRow;
                var h = rows * cellHeight;

                if (!options.AllowNonSquare)
                    h = Math.Max(h, w);

                if (options.PowerOfTwo)
                    h = NextPowerOfTwo(h);

                h = Math.Max(h, minSize);

                if (h > options.MaxHeight) continue;

                long area = (long)w * h;
                if (area < bestArea)
                {
                    bestArea = area;
                    bestWidth = w;
                    bestHeight = h;
                }
            }
        }
        else
        {
            // Try a range of widths.
            var wMin = Math.Max(cellWidth, minSize);
            var wMax = options.MaxWidth;

            for (var w = wMin; w <= wMax; w++)
            {
                var cellsPerRow = w / cellWidth;
                if (cellsPerRow <= 0) continue;

                var rows = (n + cellsPerRow - 1) / cellsPerRow;
                var h = rows * cellHeight;

                if (!options.AllowNonSquare)
                    h = Math.Max(h, w);

                h = Math.Max(h, minSize);

                if (h > options.MaxHeight) continue;

                long area = (long)w * h;
                if (area < bestArea)
                {
                    bestArea = area;
                    bestWidth = w;
                    bestHeight = h;
                }
            }
        }

        return (bestWidth, bestHeight);
    }

    /// <summary>
    /// Exhaustive evaluation of all POT width candidates for non-square POT atlas.
    /// </summary>
    private static (int Width, int Height) EstimatePotNonSquare(
        GlyphRect[] sorted, AtlasSizingOptions options,
        int maxGlyphWidth, int lowerBound, int minSize)
    {
        var bestWidth = 0;
        var bestHeight = 0;
        long bestArea = long.MaxValue;

        var wMin = NextPowerOfTwo(Math.Max(maxGlyphWidth, minSize));
        var wMax = options.MaxWidth;

        for (var w = wMin; w <= wMax; w *= 2)
        {
            var h = EstimateShelfHeight(sorted, w);
            h = Math.Max(h, minSize);
            h = NextPowerOfTwo(h);

            if (h > options.MaxHeight) continue;

            // Enforce max aspect ratio.
            var ratio = w >= h ? (float)w / h : (float)h / w;
            if (ratio > options.MaxAspectRatio) continue;

            long area = (long)w * h;
            if (area < bestArea)
            {
                bestArea = area;
                bestWidth = w;
                bestHeight = h;
            }
        }

        // Fallback if nothing fit within limits.
        if (bestWidth == 0)
        {
            bestWidth = Math.Min(NextPowerOfTwo(lowerBound), options.MaxWidth);
            bestHeight = Math.Min(NextPowerOfTwo(lowerBound), options.MaxHeight);
        }

        return (bestWidth, bestHeight);
    }

    /// <summary>
    /// Sweep over step-function breakpoints for non-square non-POT atlas.
    /// Breakpoints occur at widths where accumulated glyph widths cross the target width boundary.
    /// </summary>
    private static (int Width, int Height) EstimateNpotNonSquare(
        GlyphRect[] sorted, AtlasSizingOptions options,
        int maxGlyphWidth, int lowerBound, int minSize)
    {
        var bestWidth = 0;
        var bestHeight = 0;
        long bestArea = long.MaxValue;

        var wMin = Math.Max(maxGlyphWidth, minSize);
        var wMax = options.MaxWidth;

        // Collect breakpoint widths: these are cumulative prefix sums of glyph widths
        // modulo the target width. We also check the area-based lower bound.
        var candidateWidths = new HashSet<int> { wMin, lowerBound };

        // Add breakpoints where shelves change.
        var cumWidth = 0;
        foreach (var g in sorted)
        {
            cumWidth += g.Width;
            // A breakpoint occurs at widths that are divisors of the cumulative sum,
            // but more practically, we add the cumulative width itself as a candidate
            // and also check just before it wraps.
            if (cumWidth >= wMin && cumWidth <= wMax)
                candidateWidths.Add(cumWidth);
        }

        // Also add some systematic candidates around the lower bound.
        for (var w = wMin; w <= wMax; w = Math.Max(w + 1, (int)(w * 1.05)))
        {
            candidateWidths.Add(w);
            if (candidateWidths.Count > 2000) break; // Cap iterations.
        }

        foreach (var w in candidateWidths)
        {
            if (w < wMin || w > wMax) continue;

            var h = EstimateShelfHeight(sorted, w);
            h = Math.Max(h, minSize);

            if (h > options.MaxHeight) continue;

            // Enforce max aspect ratio.
            var ratio = w >= h ? (float)w / h : (float)h / w;
            if (ratio > options.MaxAspectRatio) continue;

            long area = (long)w * h;
            if (area < bestArea)
            {
                bestArea = area;
                bestWidth = w;
                bestHeight = h;
            }
        }

        // Fallback if nothing fit within limits.
        if (bestWidth == 0)
        {
            bestWidth = Math.Min(lowerBound, options.MaxWidth);
            bestHeight = Math.Min(lowerBound, options.MaxHeight);
        }

        return (bestWidth, bestHeight);
    }

    private static int NextPowerOfTwo(int v)
    {
        if (v <= 0) return 1;
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
