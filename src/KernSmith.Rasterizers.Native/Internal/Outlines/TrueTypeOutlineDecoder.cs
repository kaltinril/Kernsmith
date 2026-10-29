using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Tables;
using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Decodes TrueType (<c>glyf</c>) outlines: composites resolved by <see cref="GlyfTable"/>,
/// quadratics elevated to cubics by <see cref="OutlineExtractor"/>, the advance from <c>hmtx</c>
/// and the <c>glyf</c> header box as the declared bounds — or, where the header is stale, the
/// points' control box (see <see cref="DeclaredBox"/>). At a non-default variation instance the
/// outline carries <c>gvar</c> deltas and the advance its <c>HVAR</c> (or phantom-point) delta.
/// </summary>
/// <remarks>
/// The outline is placed by its left side bearing phantom point, pp1 = <c>xMin - lsb</c>, which
/// becomes x = 0 — so it lands with its xMin at the <c>hmtx</c> bearing even where a font's
/// bearing and xMin disagree. <see cref="ParsedGlyph.Origin"/> names whose metrics apply — the
/// bearing and the advance alike: a <c>USE_MY_METRICS</c> component's, for such a composite,
/// with that component's <c>HVAR</c> or phantom-point advance delta at an instance;
/// <see cref="GlyfTable"/> has already applied the varied origin. <c>HVAR</c> side-bearing deltas
/// are not read: they restate the phantom-point metrics for layout without outlines, while the
/// outline is placed by the phantom points themselves.
/// </remarks>
internal sealed class TrueTypeOutlineDecoder : IOutlineDecoder
{
    private readonly GlyfTable _glyf;
    private readonly HmtxTable _hmtx;
    private readonly FontVariations? _variations;
    private readonly GlyfScratch _scratch = new();

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
            // Simple glyphs skip the ParsedGlyph detour; composites need it to resolve components.
            if (_glyf.TryReadSimpleHeader(glyphIndex, out var simple))
            {
                float simpleShift = SimpleBearingShift(glyphIndex, simple);
                var header = new OutlineBounds(simple.XMin + simpleShift, simple.YMin, simple.XMax + simpleShift, simple.YMax);
                if (simple.NumberOfContours == 0)
                    return new DecodedGlyph(GlyphOutline.Empty, _hmtx.GetAdvanceWidth(glyphIndex), header);

                var simpleOutline = _glyf.DecodeSimpleOutline(glyphIndex, simpleShift, _scratch, out var points);
                return new DecodedGlyph(
                    simpleOutline,
                    _hmtx.GetAdvanceWidth(glyphIndex),
                    DeclaredBox(header, Shifted(points, simpleShift), simpleOutline));
            }

            var parsed = _glyf.GetGlyph(glyphIndex);
            float shift = BearingShift(parsed);
            var parsedOutline = OutlineExtractor.Extract(parsed, shift);
            return new DecodedGlyph(
                parsedOutline,
                _hmtx.GetAdvanceWidth(parsed.Origin.MetricsGlyph),
                DeclaredBox(HeaderBox(parsed, shift), ControlBox(parsed, shift), parsedOutline));
        }

        var varied = _glyf.GetGlyph(glyphIndex, variations, out var phantom);
        float bearingShift = BearingShift(varied);
        var outline = OutlineExtractor.Extract(varied, bearingShift);

        // HVAR, when present, is the font's own statement of the varied advance; otherwise the
        // phantom points carry it. Both are the metrics glyph's, as the phantom deltas are.
        int metricsGlyph = varied.Origin.MetricsGlyph;
        int defaultAdvance = _hmtx.GetAdvanceWidth(metricsGlyph);
        float advance = variations.HasAdvanceVariations
            ? defaultAdvance + variations.GetAdvanceDelta(metricsGlyph)
            : defaultAdvance + phantom.Right - phantom.Left;

        // The glyf header box describes the default outline only; a moved outline is measured.
        bool unmoved = !phantom.OutlineVaried && varied.Origin.VariationDelta.Equals(0f);
        return unmoved
            ? new DecodedGlyph(outline, advance,
                DeclaredBox(HeaderBox(varied, bearingShift), ControlBox(varied, bearingShift), outline))
            : new DecodedGlyph(outline, advance);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers for a simple glyph at the default instance — the header box, contour end points
    /// and stored coordinates say everything <see cref="Decode"/> would — unless its only
    /// contours are single points, whose flags decide whether anything draws, or its header box
    /// misses a stored point, where only the drawn outline tells whether the header is stale.
    /// </remarks>
    public bool TryDecodeBounds(int glyphIndex, RasterOptions options, out DecodedBounds bounds)
    {
        bounds = default;
        if (_variations is { IsDefaultInstance: false } || !_glyf.TryReadSimpleHeader(glyphIndex, out var simple))
            return false;

        float advance = _hmtx.GetAdvanceWidth(glyphIndex);
        if (simple.NumberOfContours == 0)
        {
            bounds = new DecodedBounds(advance, IsEmpty: true, default);
            return true;
        }

        if (simple.LargestContour < 2)
            return false;

        float shift = SimpleBearingShift(glyphIndex, simple);
        var header = new OutlineBounds(simple.XMin + shift, simple.YMin, simple.XMax + shift, simple.YMax);
        if (!Holds(header, Shifted(_glyf.ReadSimpleControlBox(glyphIndex, _scratch), shift)))
            return false;

        bounds = new DecodedBounds(advance, IsEmpty: false, header);
        return true;
    }

    /// <summary>
    /// How far, in font units, a stored point may lie outside the header box before the header
    /// counts as missing it: headers are whole units, rounded from the fractional points a scaled
    /// composite component produces, and an overshoot under a unit clips nothing visible.
    /// </summary>
    private const float HeaderSlack = 1f;

    /// <summary>
    /// The box to declare for an untransformed outline: its <c>glyf</c> header box, unless that
    /// box is stale — it misses a stored point and the drawn outline as well — and then the
    /// points' control box, which is what FreeType sizes every glyph from.
    /// </summary>
    /// <remarks>
    /// A header that holds the ink but not every off-curve point (a tight-bounds header) is
    /// kept: only a header that would clip ink is replaced. Correct headers hold every point, so
    /// they never reach the tight-bounds test, and their output cannot change.
    /// </remarks>
    private static OutlineBounds DeclaredBox(OutlineBounds header, OutlineBounds controlBox, GlyphOutline outline) =>
        Holds(header, controlBox) || Holds(header, OutlineBounds.Tight(outline)) ? header : controlBox;

    /// <summary>Whether <paramref name="header"/> contains <paramref name="inner"/>, give or take <see cref="HeaderSlack"/>.</summary>
    private static bool Holds(OutlineBounds header, OutlineBounds inner) =>
        inner.XMin >= header.XMin - HeaderSlack && inner.YMin >= header.YMin - HeaderSlack
        && inner.XMax <= header.XMax + HeaderSlack && inner.YMax <= header.YMax + HeaderSlack;

    private static OutlineBounds Shifted(OutlineBounds box, float shift) =>
        box with { XMin = box.XMin + shift, XMax = box.XMax + shift };

    /// <summary>A parsed glyph's header box, moved right by <paramref name="shift"/>.</summary>
    private static OutlineBounds HeaderBox(ParsedGlyph glyph, float shift) =>
        new(glyph.XMin + shift, glyph.YMin, glyph.XMax + shift, glyph.YMax);

    /// <summary>The bounds of a parsed glyph's points, moved right by <paramref name="shift"/>.</summary>
    private static OutlineBounds ControlBox(ParsedGlyph glyph, float shift)
    {
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        foreach (var contour in glyph.Contours)
        {
            foreach (var point in contour.Points)
            {
                xMin = MathF.Min(xMin, point.X);
                xMax = MathF.Max(xMax, point.X);
                yMin = MathF.Min(yMin, point.Y);
                yMax = MathF.Max(yMax, point.Y);
            }
        }

        return glyph.IsEmpty ? default : new OutlineBounds(xMin + shift, yMin, xMax + shift, yMax);
    }

    /// <summary><see cref="BearingShift"/> for a simple glyph, whose own metrics place it.</summary>
    private float SimpleBearingShift(int glyphIndex, SimpleGlyphHeader glyph) =>
        glyph.NumberOfContours == 0 ? 0f : _hmtx.GetLeftSideBearing(glyphIndex) - glyph.XMin;

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
