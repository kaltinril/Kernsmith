using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Decodes TrueType (<c>glyf</c>) outlines: composites resolved by <see cref="GlyfTable"/>,
/// quadratics elevated to cubics by <see cref="OutlineExtractor"/>, the advance from <c>hmtx</c>
/// and the <c>glyf</c> header box as the declared bounds.
/// </summary>
internal sealed class TrueTypeOutlineDecoder : IOutlineDecoder
{
    private readonly GlyfTable _glyf;
    private readonly HmtxTable _hmtx;

    /// <summary>Creates a decoder over a face's <c>glyf</c> and <c>hmtx</c> tables.</summary>
    /// <exception cref="ArgumentException">If the face has no <c>glyf</c> table.</exception>
    public TrueTypeOutlineDecoder(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        _glyf = face.Glyf ?? throw new ArgumentException("The face has no 'glyf' table.", nameof(face));
        _hmtx = face.Hmtx;
    }

    /// <inheritdoc />
    public DecodedGlyph Decode(int glyphIndex, RasterOptions options)
    {
        var parsed = _glyf.GetGlyph(glyphIndex);
        return new DecodedGlyph(
            OutlineExtractor.Extract(parsed),
            _hmtx.GetAdvanceWidth(glyphIndex),
            new OutlineBounds(parsed.XMin, parsed.YMin, parsed.XMax, parsed.YMax));
    }
}
