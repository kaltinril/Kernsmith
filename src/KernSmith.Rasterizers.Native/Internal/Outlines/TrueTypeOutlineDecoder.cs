using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Tables;
using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Decodes TrueType (<c>glyf</c>) outlines: composites resolved by <see cref="GlyfTable"/>,
/// quadratics elevated to cubics by <see cref="OutlineExtractor"/>, the advance from <c>hmtx</c>
/// and the <c>glyf</c> header box as the declared bounds. At a non-default variation instance the
/// outline carries <c>gvar</c> deltas and the advance its <c>HVAR</c> (or phantom-point) delta.
/// </summary>
/// <remarks>
/// The outline is placed by its left side bearing phantom point, pp1 = <c>xMin - lsb</c>, which
/// becomes x = 0 — so it lands with its xMin at the <c>hmtx</c> bearing even where a font's
/// bearing and xMin disagree. <see cref="ParsedGlyph.Origin"/> names whose metrics apply (a
/// <c>USE_MY_METRICS</c> component's, for such a composite); <see cref="GlyfTable"/> has already
/// applied the varied part. <c>HVAR</c> side-bearing deltas are not read: they restate the
/// phantom-point metrics for layout without outlines, while the outline is placed by the phantom
/// points themselves.
/// </remarks>
internal sealed class TrueTypeOutlineDecoder : IOutlineDecoder
{
    private readonly GlyfTable _glyf;
    private readonly HmtxTable _hmtx;
    private readonly FontVariations? _variations;

    /// <summary>Creates a decoder over a face's <c>glyf</c> and <c>hmtx</c> tables.</summary>
    /// <param name="face">The face.</param>
    /// <param name="variations">The face's variation state, for a variable font; the decoder reads
    /// whatever instance it currently holds.</param>
    /// <exception cref="ArgumentException">If the face has no <c>glyf</c> table.</exception>
    public TrueTypeOutlineDecoder(NativeFontFace face, FontVariations? variations = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        _glyf = face.Glyf ?? throw new ArgumentException("The face has no 'glyf' table.", nameof(face));
        _hmtx = face.Hmtx;
        _variations = variations;
    }

    /// <inheritdoc />
    public DecodedGlyph Decode(int glyphIndex, RasterOptions options)
    {
        // The default instance takes exactly the static path, so its output cannot drift.
        if (_variations is not { IsDefaultInstance: false } variations)
        {
            var parsed = _glyf.GetGlyph(glyphIndex);
            float shift = BearingShift(parsed);
            return new DecodedGlyph(
                OutlineExtractor.Extract(parsed, shift),
                _hmtx.GetAdvanceWidth(glyphIndex),
                new OutlineBounds(parsed.XMin + shift, parsed.YMin, parsed.XMax + shift, parsed.YMax));
        }

        var varied = _glyf.GetGlyph(glyphIndex, variations, out var phantom);
        float bearingShift = BearingShift(varied);
        var outline = OutlineExtractor.Extract(varied, bearingShift);

        // HVAR, when present, is the font's own statement of the varied advance; otherwise the
        // phantom points carry it.
        int defaultAdvance = _hmtx.GetAdvanceWidth(glyphIndex);
        float advance = variations.HasAdvanceVariations
            ? defaultAdvance + variations.GetAdvanceDelta(glyphIndex)
            : defaultAdvance + phantom.Right - phantom.Left;

        // The glyf header box describes the default outline only; a moved outline is measured.
        bool unmoved = !phantom.OutlineVaried && varied.Origin.VariationDelta.Equals(0f);
        return unmoved
            ? new DecodedGlyph(outline, advance,
                new OutlineBounds(varied.XMin + bearingShift, varied.YMin, varied.XMax + bearingShift, varied.YMax))
            : new DecodedGlyph(outline, advance);
    }

    /// <summary>
    /// How far right the outline moves so its default-instance pp1 (<c>xMin - lsb</c> of the
    /// glyph whose metrics apply) sits at x = 0: <c>lsb - xMin</c>, zero for a consistent font.
    /// </summary>
    private float BearingShift(ParsedGlyph glyph)
    {
        if (glyph.IsEmpty)
            return 0f;

        var origin = glyph.Origin;
        return _hmtx.GetLeftSideBearing(origin.MetricsGlyph) - origin.XMin;
    }
}
