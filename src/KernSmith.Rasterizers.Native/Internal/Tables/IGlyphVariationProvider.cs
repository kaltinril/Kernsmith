namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// Supplies per-point variation deltas for one glyph at the current variation instance, so
/// <see cref="GlyfTable"/> can vary each glyph of a composite tree at its own level — the
/// <c>gvar</c> point numbers of a composite address its components, not the resolved points.
/// </summary>
internal interface IGlyphVariationProvider
{
    /// <summary>
    /// Computes the net deltas for a glyph's points plus its four trailing phantom points.
    /// </summary>
    /// <param name="glyphIndex">The glyph whose own variation data to read.</param>
    /// <param name="pointCount">Outline points of a simple glyph, or components of a composite;
    /// the phantom points are not included.</param>
    /// <param name="xs">Default X coordinates of a simple glyph's raw points (empty for a composite).</param>
    /// <param name="ys">Default Y coordinates of a simple glyph's raw points (empty for a composite).</param>
    /// <param name="contourEnds">Last point index of each contour of a simple glyph; empty for a
    /// composite, which never infers deltas for unreferenced entries.</param>
    /// <param name="dx">Net X deltas, <paramref name="pointCount"/> + 4 long.</param>
    /// <param name="dy">Net Y deltas, <paramref name="pointCount"/> + 4 long.</param>
    /// <returns>False when the glyph has no variation data that applies at this instance.</returns>
    bool TryGetDeltas(
        int glyphIndex,
        int pointCount,
        ReadOnlySpan<int> xs,
        ReadOnlySpan<int> ys,
        ReadOnlySpan<int> contourEnds,
        out float[] dx,
        out float[] dy);
}

/// <summary>
/// How a varied glyph's horizontal phantom points moved, in font units.
/// </summary>
/// <param name="Left">X delta of the left side bearing phantom point (the horizontal origin).</param>
/// <param name="Right">X delta of the advance-width phantom point.</param>
/// <param name="OutlineVaried">True when any outline point (or component offset) moved.</param>
internal readonly record struct PhantomDeltas(float Left, float Right, bool OutlineVaried);
