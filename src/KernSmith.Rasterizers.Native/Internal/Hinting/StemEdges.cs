using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// Whether a glyph's facing edges — the ink at its leftmost and rightmost x — are straight
/// vertical stems. Two such edges meeting across a pair with no white between them fuse ("rn"
/// into "m", "ll" into one bar); a bowl or a diagonal meeting a stem at the same distance still
/// reads as two letters, so only stem-to-stem pairs are spaced apart.
/// </summary>
internal static class StemEdges
{
    /// <summary>How close to the ink's extreme x, in pixels, a straight vertical segment must lie to be the facing edge.</summary>
    private const float EdgeTolerance = 0.1f;

    /// <summary>The share of the ink's height the straight segments at an extreme must cover for the edge to count as a stem.</summary>
    private const float MinHeightShare = 0.4f;

    /// <summary>The least straight length, in pixels, that counts as a stem at all.</summary>
    private const float MinLength = 1f;

    /// <summary>
    /// Finds the stem edges of a pixel-space outline: an extreme (left or right) is a stem when
    /// the straight segments within <see cref="EdgeTolerance"/> of it, no further off vertical
    /// than <paramref name="slopeTolerance"/> (|dx/dy|), together cover at least
    /// <see cref="MinHeightShare"/> of the ink's height and <see cref="MinLength"/>. A terminal's
    /// short vertical cut (the end of a 'T' bar or an 'r' arm) is far short of that; a bowl has
    /// no straight segment at its extremum at all.
    /// </summary>
    public static (bool Left, bool Right) Detect(GlyphOutline outline, float slopeTolerance)
    {
        ArgumentNullException.ThrowIfNull(outline);
        if (outline.IsEmpty)
            return (false, false);

        var bounds = OutlineBounds.Tight(outline);
        float height = bounds.YMax - bounds.YMin;
        if (height <= 0f)
            return (false, false);

        float leftLength = 0f, rightLength = 0f;
        float startX = 0f, startY = 0f, x = 0f, y = 0f;
        foreach (var c in outline.Commands)
        {
            switch (c.Type)
            {
                case OutlineCommandType.MoveTo:
                    startX = x = c.EndX;
                    startY = y = c.EndY;
                    continue;
                case OutlineCommandType.LineTo:
                    Accumulate(x, y, c.EndX, c.EndY);
                    break;
                case OutlineCommandType.Close:
                    Accumulate(x, y, startX, startY);
                    break;
                default:
                    break;
            }

            x = c.EndX;
            y = c.EndY;
        }

        float minimum = MathF.Max(MinLength, MinHeightShare * height);
        return (leftLength >= minimum, rightLength >= minimum);

        void Accumulate(float x0, float y0, float x1, float y1)
        {
            float dy = MathF.Abs(y1 - y0);
            if (dy <= 0f || MathF.Abs(x1 - x0) > slopeTolerance * dy)
                return;
            float edgeX = (x0 + x1) / 2f;
            if (edgeX - bounds.XMin <= EdgeTolerance)
                leftLength += dy;
            if (bounds.XMax - edgeX <= EdgeTolerance)
                rightLength += dy;
        }
    }
}
