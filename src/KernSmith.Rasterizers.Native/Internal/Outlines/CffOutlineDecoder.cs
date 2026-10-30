using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Cff;
using KernSmith.Rasterizers.Native.Internal.Tables;
using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Decodes PostScript outlines from a <c>CFF </c> or <c>CFF2</c> table by running each glyph's
/// Type 2 charstring. Declares no bounding box — CFF has none, so the pipeline measures the
/// outline's tight bounds.
/// </summary>
/// <remarks>
/// The advance comes from <c>hmtx</c>, not the charstring width, exactly as FreeType does for
/// an OpenType-wrapped CFF: the two normally agree, and <c>hmtx</c> is what every other backend
/// and the shared metrics code use. At a non-default instance of a CFF2 variable font, every
/// <c>blend</c> applies its deltas weighted by the scalars of the regions its <c>vsindex</c>
/// selects, and the advance takes its <c>HVAR</c> delta (CFF2 has no phantom points). The
/// default instance takes exactly the static path: <c>blend</c> keeps its default values.
/// </remarks>
internal sealed class CffOutlineDecoder : IOutlineDecoder
{
    private readonly CffFont _font;
    private readonly HmtxTable _hmtx;
    private readonly Dictionary<CffPrivate, CharstringContext> _contexts = [];

    private ItemVariationStore? _store;
    private float[]? _blendCoordinates;   // the instance _blendScalars was computed for
    private double[][]? _blendScalars;    // per vsindex at that instance; null at the default

    /// <summary>Parses the face's <c>CFF2</c> table, or its <c>CFF </c> table when it has no <c>CFF2</c>.</summary>
    /// <param name="face">The loaded face.</param>
    /// <param name="variations">The face's variation state, for a variable font.</param>
    /// <exception cref="ArgumentException">If the face has neither table.</exception>
    /// <exception cref="FontFormatException">If the table is malformed.</exception>
    public CffOutlineDecoder(NativeFontFace face, FontVariations? variations = null)
    {
        ArgumentNullException.ThrowIfNull(face);

        bool isCff2 = face.Tables.HasTable("CFF2");
        string tag = isCff2 ? "CFF2" : "CFF ";
        var table = face.Tables.TryGetTable(tag)
            ?? throw new ArgumentException("The face has neither a 'CFF ' nor a 'CFF2' table.", nameof(face));

        try
        {
            _font = isCff2 ? CffFont.ParseCff2(table) : CffFont.ParseCff(table);
        }
        catch (FontFormatException ex)
        {
            throw new FontFormatException($"Malformed '{tag}' table: {ex.Message}", ex);
        }

        _hmtx = face.Hmtx;
        Variations = variations;
    }

    /// <summary>
    /// The face's variation state (null for a font without <c>fvar</c>); the decoder reads
    /// whatever instance it currently holds.
    /// </summary>
    internal FontVariations? Variations { get; }

    /// <summary>The parsed table. Internal access for tests.</summary>
    internal CffFont Font => _font;

    /// <inheritdoc />
    public DecodedGlyph Decode(int glyphIndex, RasterOptions options)
    {
        if ((uint)glyphIndex >= (uint)_font.CharStrings.Count)
            throw new FontFormatException($"Glyph {glyphIndex} has no charstring (the font has {_font.CharStrings.Count}).");

        var variations = SelectInstance();
        var privateData = _font.PrivateFor(glyphIndex);
        var outline = Type2Charstring.Interpret(_font.CharStrings[glyphIndex], ContextFor(privateData));

        var commands = outline.Commands;
        if (CffMatrix.ToFontUnits(privateData.Matrix) is { } matrix)
            commands = Transform(commands, matrix);

        int advance = _hmtx.GetAdvanceWidth(glyphIndex);
        return variations is null
            ? new DecodedGlyph(GlyphOutline.FromCommands(commands), advance)
            : new DecodedGlyph(GlyphOutline.FromCommands(commands), advance + variations.GetAdvanceDelta(glyphIndex));
    }

    /// <summary>The charstring width, which <see cref="Decode"/> deliberately does not use. Internal for tests.</summary>
    internal double CharstringWidth(int glyphIndex)
    {
        SelectInstance();
        var privateData = _font.PrivateFor(glyphIndex);
        return Type2Charstring.Interpret(_font.CharStrings[glyphIndex], ContextFor(privateData)).Width;
    }

    /// <summary>
    /// Brings the <c>blend</c> scalars up to date with the instance <see cref="Variations"/> holds:
    /// recomputed when the coordinates change, dropped at the default instance.
    /// </summary>
    /// <returns>The variation state when a non-default instance is selected, otherwise null.</returns>
    /// <exception cref="FontFormatException">If the CFF2 VariationStore is malformed.</exception>
    private FontVariations? SelectInstance()
    {
        if (Variations is not { IsDefaultInstance: false } variations)
        {
            _blendScalars = null;
            _blendCoordinates = null;
            return null;
        }

        if (_blendCoordinates is not null && variations.Coordinates.SequenceEqual(_blendCoordinates))
            return variations;

        if (_font.HasVariationStore)
        {
            _store ??= _font.ReadVariationStore()!;
            var regionScalars = _store.ComputeRegionScalars(variations.Coordinates);
            var perVsIndex = new double[_store.SubtableCount][];
            for (int vsIndex = 0; vsIndex < perVsIndex.Length; vsIndex++)
                perVsIndex[vsIndex] = Array.ConvertAll(_store.SubtableRegionScalars(vsIndex, regionScalars), s => (double)s);

            _blendScalars = perVsIndex;
        }

        _blendCoordinates = variations.Coordinates.ToArray();
        return variations;
    }

    /// <summary><see cref="CharstringContext.BlendScalars"/>: null (keep the defaults) at the default instance.</summary>
    private double[]? BlendScalars(int vsIndex) => _blendScalars?[vsIndex];

    private CharstringContext ContextFor(CffPrivate privateData)
    {
        if (_contexts.TryGetValue(privateData, out var context))
            return context;

        context = new CharstringContext
        {
            GlobalSubrs = _font.GlobalSubrs,
            LocalSubrs = privateData.LocalSubrs,
            DefaultWidthX = privateData.DefaultWidthX,
            NominalWidthX = privateData.NominalWidthX,
            IsCff2 = _font.IsCff2,
            VsIndex = privateData.VsIndex,
            RegionCounts = _font.RegionCounts,
            BlendScalars = _font.HasVariationStore ? BlendScalars : null,
            SeacComponent = _font.IsCff2 || _font.IsCidKeyed ? null : SeacComponent,
        };

        _contexts[privateData] = context;
        return context;
    }

    private ReadOnlyMemory<byte>? SeacComponent(int standardCode)
    {
        int glyph = _font.GlyphForStandardCode(standardCode);
        return glyph < 0 ? null : _font.CharStrings[glyph];
    }

    /// <summary>
    /// Applies the FontMatrix, holding the result to the interpreter's coordinate cap: a matrix
    /// that stretches one axis enormously would otherwise smuggle a giant outline past it.
    /// </summary>
    private static OutlineCommand[] Transform(OutlineCommand[] commands, CffMatrix matrix)
    {
        var result = new OutlineCommand[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            var c = commands[i];
            var (x, y) = matrix.Apply(c.EndX, c.EndY);
            var (c1x, c1y) = matrix.Apply(c.Ctrl1X, c.Ctrl1Y);
            var (c2x, c2y) = matrix.Apply(c.Ctrl2X, c.Ctrl2Y);
            result[i] = c.Type == OutlineCommandType.CubicTo
                ? OutlineCommand.Cubic(Bounded(c1x), Bounded(c1y), Bounded(c2x), Bounded(c2y), Bounded(x), Bounded(y))
                : c with { EndX = Bounded(x), EndY = Bounded(y) };
        }

        return result;
    }

    private static float Bounded(double value) =>
        Math.Abs(value) <= Type2Charstring.MaxCoordinate
            ? (float)value
            : throw new FontFormatException(
                $"CFF FontMatrix maps a point to {value}, outside ±{Type2Charstring.MaxCoordinate} font units.");
}
