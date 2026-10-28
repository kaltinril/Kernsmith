using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Stage 1 of the glyph pipeline: turns a glyph index into a <see cref="DecodedGlyph"/> in font
/// design units. One implementation per outline format (<c>glyf</c>, CFF, CFF2); the rasterizer
/// picks one per face in <see cref="OutlineDecoders.ForFace"/>.
/// </summary>
/// <remarks>
/// Implementations emit cubic <see cref="OutlineCommand"/>s — quadratic sources elevate, as
/// <see cref="OutlineExtractor"/> does — and need not declare a bounding box. Variation deltas
/// belong inside the decoder, so <see cref="Decode"/> receives the request and its axis coordinates.
/// Throw <see cref="FontFormatException"/> for malformed glyph data; the rasterizer converts it
/// into a <see cref="RasterizationException"/> naming the codepoint.
/// </remarks>
internal interface IOutlineDecoder
{
    /// <summary>Decodes one glyph.</summary>
    /// <param name="glyphIndex">A glyph index inside the font.</param>
    /// <param name="options">The request being rendered.</param>
    DecodedGlyph Decode(int glyphIndex, RasterOptions options);
}
