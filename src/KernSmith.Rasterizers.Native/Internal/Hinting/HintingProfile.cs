using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// What a face says about how it wants to be hinted — "the font already knows": whether it
/// carries TrueType bytecode, whether that bytecode was written for integer ppem
/// (<c>head.flags</c> bit 3), its <c>hdmx</c> device advances, its <c>gasp</c> grid-fitting
/// ranges and its x-height ratio. The <c>Normal</c> hinter reads it once per face to decide
/// the ppem it fits at, whether to fit at a size at all, and how the x-height rounds; every
/// other level ignores it. The <c>hdmx</c> advances are parsed and exposed for diagnostics
/// (parity tooling) only — nothing in the pipeline consumes them, see <see cref="HdmxAdvance"/>.
/// </summary>
/// <remarks>
/// A font has bytecode by FreeType's rule — an <c>fpgm</c> or <c>prep</c> table, or
/// <c>maxp.maxSizeOfInstructions</c> above zero — which is the rule that decides whether
/// FreeType runs its TrueType interpreter or its auto-hinter on the font. FreeType's
/// interpreter renders a bit-3 font at <c>round(ppem)</c>, its auto-hinter at the fractional
/// ppem, so <see cref="PrefersIntegerPpem"/> needs both: Roboto sets bit 3 but has no
/// bytecode, and rendering it at the integer ppem made it worse against FreeType; a CFF face
/// never has bytecode.
/// </remarks>
internal sealed class HintingProfile
{
    /// <summary>A fractional x-height rounds to the nearest row: floor(x + 0.5).</summary>
    private const float NearestRow = 0.5f;

    private HintingProfile(bool hasBytecode, bool integerPpemPreferred, HdmxTable? hdmx, GaspTable? gasp, float xHeightRatio)
    {
        HasBytecode = hasBytecode;
        IntegerPpemPreferred = integerPpemPreferred;
        Hdmx = hdmx;
        Gasp = gasp;
        XHeightRatio = xHeightRatio;
    }

    /// <summary>
    /// A face that tells the hinter nothing: no bytecode, no device metrics, no <c>gasp</c>.
    /// The auto-hinter's own rules apply throughout, and the fitter behaves as it does for the
    /// fixture fonts.
    /// </summary>
    public static HintingProfile Autohint { get; } = new(false, false, null, null, 0f);

    /// <summary>True when the face carries TrueType bytecode (<c>fpgm</c>, <c>prep</c> or glyph instructions).</summary>
    public bool HasBytecode { get; }

    /// <summary>True when <c>head.flags</c> bit 3 asks for integer ppem.</summary>
    public bool IntegerPpemPreferred { get; }

    /// <summary>
    /// True when the <c>Normal</c> hinter fits and renders the face at <c>round(ppem)</c>:
    /// it has bytecode and that bytecode was hinted for integer ppem.
    /// </summary>
    public bool PrefersIntegerPpem => HasBytecode && IntegerPpemPreferred;

    /// <summary>The face's device advances, or null when it has no usable <c>hdmx</c>.</summary>
    public HdmxTable? Hdmx { get; }

    /// <summary>The face's grid-fitting ranges, or null when it has no usable <c>gasp</c>.</summary>
    public GaspTable? Gasp { get; }

    /// <summary>The <c>OS/2</c> x-height as a fraction of the em; 0 when the table does not give one.</summary>
    public float XHeightRatio { get; }

    /// <summary>Reads a face's profile. Malformed optional tables are ignored, never an error.</summary>
    public static HintingProfile For(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);

        var tables = face.Tables;
        bool hasBytecode = face.HasGlyfOutlines
            && (tables.HasTable("fpgm") || tables.HasTable("prep") || face.Maxp.MaxSizeOfInstructions > 0);
        var hdmx = OptionalTable(tables, "hdmx") is { } hdmxBytes ? HdmxTable.TryParse(hdmxBytes, face.Maxp.NumGlyphs) : null;
        var gasp = OptionalTable(tables, "gasp") is { } gaspBytes ? GaspTable.TryParse(gaspBytes.Span) : null;
        float xHeightRatio = face.Os2.XHeight > 0 ? face.Os2.XHeight / (float)face.Head.UnitsPerEm : 0f;
        return new HintingProfile(hasBytecode, face.Head.IntegerPpemPreferred, hdmx, gasp, xHeightRatio);
    }

    /// <summary>A profile from its parts, for tests and tools.</summary>
    public static HintingProfile From(bool hasBytecode, bool integerPpemPreferred, HdmxTable? hdmx = null, GaspTable? gasp = null, float xHeightRatio = 0f) =>
        new(hasBytecode, integerPpemPreferred, hdmx, gasp, xHeightRatio);

    /// <summary>
    /// The ppem the <c>Normal</c> hinter fits and renders at for a requested one:
    /// <c>round(ppem)</c>, at least 1, for a face that <see cref="PrefersIntegerPpem"/>, else the
    /// requested ppem unchanged. The glyph sits in the cell core sized at the fractional ppem,
    /// so it can come out up to half a pixel per em (±4% at 12 ppem) larger or smaller than the
    /// cell's nominal size — as it does on FreeType and GDI for the same font.
    /// </summary>
    public float HintPpem(float ppem) => PrefersIntegerPpem ? MathF.Max(1f, GridMath.Round(ppem)) : ppem;

    /// <summary>
    /// The <c>hdmx</c> advance of a glyph at an integer ppem, in pixels, or null when the face
    /// does not prefer integer ppem (the record would not describe a glyph drawn at that ppem),
    /// has no record for that ppem, or the glyph is outside the font. Not an advance the
    /// pipeline uses: the table holds the advances that go with the bytecode's own outlines
    /// (Georgia's instructions widen its letters a pixel at text sizes), and Native does not run
    /// the bytecode, so with Native's narrower ink those advances set a line loose in both
    /// anti-alias modes. Only valid together with a TrueType interpreter; a diagnostic until then.
    /// </summary>
    public int? HdmxAdvance(int ppem, int glyphIndex) =>
        PrefersIntegerPpem && Hdmx is not null && Hdmx.TryGetAdvance(ppem, glyphIndex, out int advance) ? advance : null;

    /// <summary>
    /// False when the face's <c>gasp</c> asks for no grid-fitting at the integer ppem nearest
    /// the given one — the <c>Normal</c> hinter then leaves the outline unfitted at that size.
    /// True without a <c>gasp</c>, or where its ranges do not reach the ppem.
    /// </summary>
    public bool GridFitsAt(float ppem) => Gasp?.GridFitsAt((int)MathF.Max(1f, GridMath.Round(ppem))) ?? true;

    /// <summary>
    /// How far a fractional x-height is pushed up before it is truncated to a row: to the
    /// nearest row for a face that <see cref="PrefersIntegerPpem"/>, whose designers chose
    /// their x-height rows per integer ppem (Verdana's and Tahoma's match nearest at every
    /// size, where the push-up was a row high at a third of them); the tuning's push-up
    /// (<see cref="HintTuning.XHeightRoundUp"/>) for an autohint face.
    /// </summary>
    public float XHeightRoundUp(HintTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        return PrefersIntegerPpem ? NearestRow : tuning.XHeightRoundUp;
    }

    /// <summary>An optional table's bytes, or null when absent or its directory record is out of bounds.</summary>
    private static ReadOnlyMemory<byte>? OptionalTable(TableProvider tables, string tag)
    {
        try
        {
            return tables.TryGetTable(tag);
        }
        catch (FontFormatException)
        {
            return null;
        }
    }
}
