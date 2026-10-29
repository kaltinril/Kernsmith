using System.Buffers;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Converts a <see cref="GlyphOutline"/> into directed <see cref="EdgeSegment"/>s in pixel
/// space: scales and flips the coordinates, then flattens every cubic with adaptive
/// De Casteljau subdivision.
/// </summary>
internal static class OutlineFlattener
{
    /// <summary>
    /// Default flatness tolerance, in pixels. A curve is emitted as a straight segment once
    /// its control points sit within this distance of its chord.
    /// </summary>
    public const float DefaultTolerance = 0.25f;

    /// <summary>
    /// Hard limit on subdivision recursion. Reached only by degenerate outlines with control
    /// points enormously far from the curve; 2^16 segments is far beyond visually useful.
    /// </summary>
    public const int MaxSubdivisionDepth = 16;

    /// <summary>
    /// Flattens an outline into the edges the scanline rasterizer consumes.
    /// </summary>
    /// <param name="outline">The extracted outline, in font units.</param>
    /// <param name="transform">The font-unit to pixel mapping.</param>
    /// <param name="tolerance">Maximum flattening error in pixels.</param>
    /// <param name="includeHorizontal">Keep exactly horizontal edges. They cross no horizontal
    /// scanline and contribute no coverage, so coverage rendering omits them; mono dropout
    /// control needs them to see thin horizontal strokes from vertical scanlines.</param>
    /// <returns>
    /// The contours' edges in outline order, keeping their original winding. Horizontal
    /// edges are omitted unless <paramref name="includeHorizontal"/> is set.
    /// </returns>
    public static EdgeSegment[] Flatten(GlyphOutline outline, OutlineTransform transform, float tolerance = DefaultTolerance, bool includeHorizontal = false)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return [];

        if (!(tolerance > 0f))
            tolerance = DefaultTolerance;

        var edges = new EdgeBuffer(outline.Commands.Length * 4, includeHorizontal);
        try
        {
            AddOutline(ref edges, outline, transform, tolerance);
            return edges.ToArray();
        }
        finally
        {
            edges.Dispose();
        }
    }

    private static void AddOutline(ref EdgeBuffer edges, GlyphOutline outline, OutlineTransform transform, float tolerance)
    {
        float currentX = 0f, currentY = 0f;

        foreach (var command in outline.Commands)
        {
            float endX = transform.ToPixelX(command.EndX);
            float endY = transform.ToPixelY(command.EndY);

            switch (command.Type)
            {
                case OutlineCommandType.MoveTo:
                    // Lifts the pen; the contour's start is carried on its Close command.
                    break;

                case OutlineCommandType.LineTo:
                    edges.Add(currentX, currentY, endX, endY);
                    break;

                case OutlineCommandType.CubicTo:
                    FlattenCubic(
                        ref edges,
                        currentX, currentY,
                        transform.ToPixelX(command.Ctrl1X), transform.ToPixelY(command.Ctrl1Y),
                        transform.ToPixelX(command.Ctrl2X), transform.ToPixelY(command.Ctrl2Y),
                        endX, endY,
                        tolerance,
                        depth: 0);
                    break;

                case OutlineCommandType.Close:
                default:
                    // Close carries the contour's start point, so the closing segment needs
                    // no extra bookkeeping here.
                    edges.Add(currentX, currentY, endX, endY);
                    break;
            }

            currentX = endX;
            currentY = endY;
        }
    }

    /// <summary>
    /// Recursively splits a cubic at its midpoint until it is flat enough to stand in for a
    /// straight segment, or until the depth cap stops it.
    /// </summary>
    private static void FlattenCubic(
        ref EdgeBuffer edges,
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float tolerance,
        int depth)
    {
        // The depth check comes first: with a tolerance the curve's own float precision can
        // never satisfy, the flatness test alone would subdivide essentially forever.
        if (depth >= MaxSubdivisionDepth || IsFlatEnough(x0, y0, x1, y1, x2, y2, x3, y3, tolerance))
        {
            edges.Add(x0, y0, x3, y3);
            return;
        }

        // De Casteljau split at t = 0.5.
        float m01X = (x0 + x1) * 0.5f, m01Y = (y0 + y1) * 0.5f;
        float m12X = (x1 + x2) * 0.5f, m12Y = (y1 + y2) * 0.5f;
        float m23X = (x2 + x3) * 0.5f, m23Y = (y2 + y3) * 0.5f;
        float m012X = (m01X + m12X) * 0.5f, m012Y = (m01Y + m12Y) * 0.5f;
        float m123X = (m12X + m23X) * 0.5f, m123Y = (m12Y + m23Y) * 0.5f;
        float midX = (m012X + m123X) * 0.5f, midY = (m012Y + m123Y) * 0.5f;

        FlattenCubic(ref edges, x0, y0, m01X, m01Y, m012X, m012Y, midX, midY, tolerance, depth + 1);
        FlattenCubic(ref edges, midX, midY, m123X, m123Y, m23X, m23Y, x3, y3, tolerance, depth + 1);
    }

    /// <summary>
    /// True when both control points lie within <paramref name="tolerance"/> of the chord
    /// P0-P3. A cubic stays inside its control hull, so this bounds the error of replacing
    /// the curve with that chord.
    /// </summary>
    private static bool IsFlatEnough(
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float tolerance)
    {
        float chordX = x3 - x0;
        float chordY = y3 - y0;
        float chordLengthSquared = (chordX * chordX) + (chordY * chordY);

        if (chordLengthSquared <= float.Epsilon)
        {
            // A degenerate chord (the curve loops back on itself): measure the controls
            // against the shared endpoint instead.
            return SquaredDistance(x1, y1, x0, y0) <= tolerance * tolerance
                && SquaredDistance(x2, y2, x0, y0) <= tolerance * tolerance;
        }

        // Perpendicular distance to the chord, squared, without the square root:
        // |cross| / |chord| <= tolerance  =>  cross^2 <= tolerance^2 * |chord|^2.
        float cross1 = ((x1 - x0) * chordY) - ((y1 - y0) * chordX);
        float cross2 = ((x2 - x0) * chordY) - ((y2 - y0) * chordX);
        float limit = tolerance * tolerance * chordLengthSquared;

        return (cross1 * cross1) <= limit && (cross2 * cross2) <= limit;
    }

    private static float SquaredDistance(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// The edges collected so far, in a pooled array; the caller gets an exact-size copy, which
    /// is the only per-glyph allocation flattening makes.
    /// </summary>
    private struct EdgeBuffer : IDisposable
    {
        private readonly bool _includeHorizontal;
        private EdgeSegment[] _items;
        private int _count;

        public EdgeBuffer(int capacity, bool includeHorizontal)
        {
            _includeHorizontal = includeHorizontal;
            _items = ArrayPool<EdgeSegment>.Shared.Rent(Math.Max(capacity, 16));
            _count = 0;
        }

        /// <summary>
        /// Appends an edge, dropping it when it is exactly horizontal and horizontals are not
        /// wanted. Exactly horizontal only — an epsilon would drop near-horizontal edges and
        /// break the rasterizer's closed-contour invariant (signed dY must sum to zero). Equals,
        /// not ==, because the comparison is intentionally exact.
        /// </summary>
        public void Add(float x0, float y0, float x1, float y1)
        {
            if (!_includeHorizontal && y0.Equals(y1))
                return;

            if (_count == _items.Length)
                Grow();

            _items[_count++] = new EdgeSegment(x0, y0, x1, y1);
        }

        public readonly EdgeSegment[] ToArray() => _count == 0 ? [] : _items.AsSpan(0, _count).ToArray();

        public void Dispose()
        {
            ArrayPool<EdgeSegment>.Shared.Return(_items);
            _items = [];
        }

        private void Grow()
        {
            var grown = ArrayPool<EdgeSegment>.Shared.Rent(_items.Length * 2);
            _items.AsSpan(0, _count).CopyTo(grown);
            ArrayPool<EdgeSegment>.Shared.Return(_items);
            _items = grown;
        }
    }
}
