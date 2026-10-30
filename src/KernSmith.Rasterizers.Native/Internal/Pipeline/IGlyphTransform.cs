using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Stage 2 of the glyph pipeline: one outline-level edit, in font design units, before
/// anything is scaled — synthetic bold or italic, stroking, condensing.
/// </summary>
/// <remarks>
/// Return the input unchanged, or a copy built with <see cref="DecodedGlyph.WithOutline"/>
/// (which drops the declared bounds, so stage 3 measures the new outline) and/or
/// <see cref="DecodedGlyph.WithAdvanceWidth"/>. Transforms are built per request by
/// <see cref="GlyphTransforms.For"/>, so they carry their own parameters.
/// </remarks>
internal interface IGlyphTransform
{
    /// <summary>Applies the transform.</summary>
    DecodedGlyph Apply(DecodedGlyph glyph);
}
