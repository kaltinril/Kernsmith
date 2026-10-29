using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// The decoder for a face whose glyphs are only color bitmaps (<c>CBDT</c>/<c>sbix</c>, no
/// <c>glyf</c> or CFF): every outline is empty and the advance is the <c>hmtx</c> one. The
/// bitmap renderer draws the glyphs; this keeps advances, blanks and metrics flowing through the
/// same pipeline as outline fonts.
/// </summary>
internal sealed class BitmapOnlyOutlineDecoder : IOutlineDecoder
{
    private readonly HmtxTable _hmtx;

    /// <summary>Creates a decoder over a face's <c>hmtx</c> table.</summary>
    public BitmapOnlyOutlineDecoder(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        _hmtx = face.Hmtx;
    }

    /// <inheritdoc />
    public DecodedGlyph Decode(int glyphIndex, RasterOptions options) =>
        new(GlyphOutline.Empty, _hmtx.GetAdvanceWidth(glyphIndex));

    /// <inheritdoc />
    public bool TryDecodeBounds(int glyphIndex, RasterOptions options, out DecodedBounds bounds)
    {
        bounds = new DecodedBounds(_hmtx.GetAdvanceWidth(glyphIndex), IsEmpty: true, default);
        return true;
    }
}
