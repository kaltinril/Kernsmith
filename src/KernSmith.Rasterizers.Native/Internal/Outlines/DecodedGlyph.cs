namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// A glyph as it travels through the decode and transform stages: its outline and advance in
/// font design units, plus the bounding box the font itself declares, when it declares one.
/// </summary>
/// <remarks>
/// Immutable. Transforms return a modified copy through <see cref="WithOutline"/> and
/// <see cref="WithAdvanceWidth"/>; there is deliberately no way to swap the outline while keeping
/// <see cref="DeclaredBounds"/>, because a declared box only describes the outline it came with.
/// </remarks>
internal sealed class DecodedGlyph
{
    /// <summary>Creates a decoded glyph.</summary>
    /// <param name="outline">The outline, in font units.</param>
    /// <param name="advanceWidth">The horizontal advance, in font units.</param>
    /// <param name="declaredBounds">The font's own bounding box for this outline, if it has one.</param>
    public DecodedGlyph(GlyphOutline outline, float advanceWidth, OutlineBounds? declaredBounds = null)
    {
        ArgumentNullException.ThrowIfNull(outline);
        Outline = outline;
        AdvanceWidth = advanceWidth;
        DeclaredBounds = declaredBounds;
    }

    /// <summary>The outline, in font units.</summary>
    public GlyphOutline Outline { get; }

    /// <summary>The horizontal advance, in font units.</summary>
    public float AdvanceWidth { get; }

    /// <summary>
    /// The bounding box the font declares for <see cref="Outline"/> (the <c>glyf</c> header box,
    /// or the points' control box where the header is stale),
    /// or null when the source has none (CFF) or a transform has replaced the outline.
    /// </summary>
    public OutlineBounds? DeclaredBounds { get; }

    /// <summary>Replaces the outline, keeping the advance and dropping the declared bounds.</summary>
    public DecodedGlyph WithOutline(GlyphOutline outline) => new(outline, AdvanceWidth, declaredBounds: null);

    /// <summary>Replaces the advance width, in font units.</summary>
    public DecodedGlyph WithAdvanceWidth(float advanceWidth) => new(Outline, advanceWidth, DeclaredBounds);
}
