using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Cff;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Decodes PostScript outlines from a <c>CFF </c> or <c>CFF2</c> table by running each glyph's
/// Type 2 charstring. Declares no bounding box — CFF has none, so the pipeline measures the
/// outline's tight bounds.
/// </summary>
/// <remarks>
/// The advance comes from <c>hmtx</c>, not the charstring width, exactly as FreeType does for
/// an OpenType-wrapped CFF: the two normally agree, and <c>hmtx</c> is what every other backend
/// and the shared metrics code use. CFF2 renders the default instance; variation (Phase 171)
/// plugs in by supplying <see cref="CharstringContext.BlendScalars"/> from the request's axes.
/// </remarks>
internal sealed class CffOutlineDecoder : IOutlineDecoder
{
    private readonly CffFont _font;
    private readonly HmtxTable _hmtx;
    private readonly Dictionary<CffPrivate, CharstringContext> _contexts = [];

    /// <summary>Parses the face's <c>CFF2</c> table, or its <c>CFF </c> table when it has no <c>CFF2</c>.</summary>
    /// <exception cref="ArgumentException">If the face has neither table.</exception>
    /// <exception cref="FontFormatException">If the table is malformed.</exception>
    public CffOutlineDecoder(NativeFontFace face)
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
    }

    /// <summary>The parsed table. Internal access for tests.</summary>
    internal CffFont Font => _font;

    /// <inheritdoc />
    public DecodedGlyph Decode(int glyphIndex, RasterOptions options)
    {
        if ((uint)glyphIndex >= (uint)_font.CharStrings.Count)
            throw new FontFormatException($"Glyph {glyphIndex} has no charstring (the font has {_font.CharStrings.Count}).");

        var privateData = _font.PrivateFor(glyphIndex);
        var outline = Type2Charstring.Interpret(_font.CharStrings[glyphIndex], ContextFor(privateData));

        var commands = outline.Commands;
        if (CffMatrix.ToFontUnits(privateData.Matrix) is { } matrix)
            commands = Transform(commands, matrix);

        return new DecodedGlyph(GlyphOutline.FromCommands(commands), _hmtx.GetAdvanceWidth(glyphIndex));
    }

    /// <summary>The charstring width, which <see cref="Decode"/> deliberately does not use. Internal for tests.</summary>
    internal double CharstringWidth(int glyphIndex)
    {
        var privateData = _font.PrivateFor(glyphIndex);
        return Type2Charstring.Interpret(_font.CharStrings[glyphIndex], ContextFor(privateData)).Width;
    }

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
