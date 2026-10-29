using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>
/// Reads a base glyph's COLR version 1 paint graph into resolved <see cref="ColrPaint"/> nodes:
/// palette colors looked up, variation deltas applied, every transform format reduced to a
/// matrix and <c>PaintColrGlyph</c> references inlined.
/// </summary>
/// <remarks>
/// Error handling follows the spec's "Color glyphs as a directed acyclic graph" section: a paint
/// that is malformed, of an unknown format, names a glyph past <c>maxp</c>, a palette entry past
/// the palette, a missing base glyph record, or that closes a cycle is ignored — that sub-graph
/// draws nothing — and the rest of the graph still renders.
///
/// Two limits guard against hostile graphs; exceeding either rejects the whole glyph (the spec
/// lets an application that cannot recover render the plain outline instead):
/// <see cref="MaxDepth"/> nested paints, and <see cref="MaxPaints"/> paints read in total — a
/// DAG that reuses sub-graphs is counted as expanded, which is what rendering would cost.
/// </remarks>
internal sealed class ColrPaintReader
{
    /// <summary>The deepest paint nesting read before the glyph is rejected.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most paints (counted as expanded) read before the glyph is rejected.</summary>
    public const int MaxPaints = 10_000;

    private const int ForegroundIndex = 0xFFFF;

    private readonly ColrV1Table _table;
    private readonly CpalTable _cpal;
    private readonly int _palette;
    private readonly PaletteColor _foreground;
    private readonly int _glyphCount;
    private readonly float[]? _regionScalars;
    private readonly HashSet<long> _path = [];
    private int _paintCount;

    /// <param name="table">The face's COLR version 1 data.</param>
    /// <param name="cpal">The face's palettes.</param>
    /// <param name="palette">The selected palette.</param>
    /// <param name="foreground">The color palette entry 0xFFFF stands for.</param>
    /// <param name="glyphCount">The face's <c>maxp.numGlyphs</c>.</param>
    /// <param name="regionScalars">The variation store's region scalars at the current instance,
    /// or null to read every value at its default.</param>
    public ColrPaintReader(ColrV1Table table, CpalTable cpal, int palette, PaletteColor foreground, int glyphCount, float[]? regionScalars)
    {
        _table = table;
        _cpal = cpal;
        _palette = palette;
        _foreground = foreground;
        _glyphCount = glyphCount;
        _regionScalars = table.VariationStore is null ? null : regionScalars;
    }

    /// <summary>Reads a base glyph's graph.</summary>
    /// <returns>False when the glyph has no version 1 record, or its graph exceeds the limits.</returns>
    public bool TryRead(int glyphIndex, out ColrGlyphGraph graph)
    {
        graph = null!;
        if (!_table.TryGetBasePaint(glyphIndex, out long offset))
            return false;

        try
        {
            var root = ReadPaint(offset, 0);
            var clip = ClipBox(glyphIndex);
            if (root is not null && clip is { } box)
                root = new RectClipPaint(box.XMin, box.YMin, box.XMax, box.YMax, root);
            graph = new ColrGlyphGraph(root, clip);
            return true;
        }
        catch (GraphLimitException)
        {
            return false;
        }
    }

    private (double XMin, double YMin, double XMax, double YMax)? ClipBox(int glyphIndex)
    {
        if (!_table.TryGetClipBox(glyphIndex, out var box))
            return null;

        return (
            box.XMin + Delta(box.VarIndexBase, 0),
            box.YMin + Delta(box.VarIndexBase, 1),
            box.XMax + Delta(box.VarIndexBase, 2),
            box.YMax + Delta(box.VarIndexBase, 3));
    }

    private ColrPaint? ReadPaint(long offset, int depth)
    {
        if (++_paintCount > MaxPaints || depth > MaxDepth)
            throw new GraphLimitException();

        if (offset < 0 || offset >= _table.Data.Length || !_path.Add(offset))
            return null; // out of the table, or a cycle

        try
        {
            return ReadPaintCore((int)offset, depth);
        }
        catch (Exception ex) when (ex is FontFormatException or InvalidPaintException)
        {
            return null;
        }
        finally
        {
            _path.Remove(offset);
        }
    }

    private ColrPaint? ReadPaintCore(int at, int depth)
    {
        var reader = new FontReader(_table.Data.Span);
        reader.Seek(at);
        int format = reader.ReadUInt8();
        bool variable = format is >= 3 and <= 31 && (format & 1) == 1;

        switch (format)
        {
            case 1:
            {
                int count = reader.ReadUInt8();
                long first = reader.ReadUInt32();
                if (first + count > _table.LayerCount)
                    return null;

                var layers = new List<ColrPaint>(count);
                for (int i = 0; i < count; i++)
                {
                    if (ReadPaint(_table.GetLayerPaint(first + i), depth + 1) is { } layer)
                        layers.Add(layer);
                }

                return new LayersPaint([.. layers]);
            }

            case 2 or 3:
            {
                int paletteIndex = reader.ReadUInt16();
                int alpha = reader.ReadInt16();
                uint varBase = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;
                return new SolidPaint(Resolve(paletteIndex, (alpha + Delta(varBase, 0)) / 16384f));
            }

            case 4 or 5:
            {
                long line = at + ColrV1Table.ReadUInt24(ref reader);
                int x0 = reader.ReadInt16(), y0 = reader.ReadInt16();
                int x1 = reader.ReadInt16(), y1 = reader.ReadInt16();
                int x2 = reader.ReadInt16(), y2 = reader.ReadInt16();
                uint v = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;
                var (stops, extend) = ReadColorLine(line, variable);
                return new LinearGradientPaint(stops, extend,
                    x0 + Delta(v, 0), y0 + Delta(v, 1), x1 + Delta(v, 2), y1 + Delta(v, 3), x2 + Delta(v, 4), y2 + Delta(v, 5));
            }

            case 6 or 7:
            {
                long line = at + ColrV1Table.ReadUInt24(ref reader);
                int x0 = reader.ReadInt16(), y0 = reader.ReadInt16(), r0 = reader.ReadUInt16();
                int x1 = reader.ReadInt16(), y1 = reader.ReadInt16(), r1 = reader.ReadUInt16();
                uint v = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;
                var (stops, extend) = ReadColorLine(line, variable);
                return new RadialGradientPaint(stops, extend,
                    x0 + Delta(v, 0), y0 + Delta(v, 1), r0 + Delta(v, 2), x1 + Delta(v, 3), y1 + Delta(v, 4), r1 + Delta(v, 5));
            }

            case 8 or 9:
            {
                long line = at + ColrV1Table.ReadUInt24(ref reader);
                int cx = reader.ReadInt16(), cy = reader.ReadInt16();
                int start = reader.ReadInt16(), end = reader.ReadInt16();
                uint v = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;
                var (stops, extend) = ReadColorLine(line, variable);

                // Stored biased: degrees = (value + 1) * 180.
                double startDegrees = (((start + Delta(v, 2)) / 16384.0) + 1) * 180;
                double endDegrees = (((end + Delta(v, 3)) / 16384.0) + 1) * 180;
                return new SweepGradientPaint(stops, extend, cx + Delta(v, 0), cy + Delta(v, 1), startDegrees, endDegrees);
            }

            case 10:
            {
                long child = at + ColrV1Table.ReadUInt24(ref reader);
                int glyph = reader.ReadUInt16();
                if (glyph >= _glyphCount)
                    return null;
                return ReadPaint(child, depth + 1) is { } paint ? new GlyphClipPaint(glyph, paint) : null;
            }

            case 11:
            {
                int glyph = reader.ReadUInt16();
                if (!_table.TryGetBasePaint(glyph, out long root) || ReadPaint(root, depth + 1) is not { } paint)
                    return null;
                return ClipBox(glyph) is { } box ? new RectClipPaint(box.XMin, box.YMin, box.XMax, box.YMax, paint) : paint;
            }

            case 12 or 13:
            {
                long child = at + ColrV1Table.ReadUInt24(ref reader);
                long affine = at + ColrV1Table.ReadUInt24(ref reader);
                var matrix = ReadAffine(affine, format == 13);
                return ReadPaint(child, depth + 1) is { } paint ? new TransformPaint(matrix, paint) : null;
            }

            case >= 14 and <= 31:
            {
                long child = at + ColrV1Table.ReadUInt24(ref reader);
                var matrix = ReadTransform(ref reader, format);
                return ReadPaint(child, depth + 1) is { } paint ? new TransformPaint(matrix, paint) : null;
            }

            case 32:
            {
                long source = at + ColrV1Table.ReadUInt24(ref reader);
                int mode = reader.ReadUInt8();
                long backdrop = at + ColrV1Table.ReadUInt24(ref reader);

                // An unrecognized mode is COMPOSITE_CLEAR.
                var composite = mode <= (int)CompositeMode.Luminosity ? (CompositeMode)mode : CompositeMode.Clear;
                return new CompositePaint(composite, ReadPaint(source, depth + 1), ReadPaint(backdrop, depth + 1));
            }

            default:
                return null;
        }
    }

    /// <summary>Formats 14–31: the int16 fields after the child offset, as a matrix.</summary>
    private Affine2D ReadTransform(ref FontReader reader, int format)
    {
        int baseFormat = format & ~1;
        bool variable = (format & 1) == 1;
        int fieldCount = baseFormat switch
        {
            14 => 2,        // dx, dy
            16 => 2,        // scaleX, scaleY
            18 => 4,        // scaleX, scaleY, centerX, centerY
            20 => 1,        // scale
            22 => 3,        // scale, centerX, centerY
            24 => 1,        // angle
            26 => 3,        // angle, centerX, centerY
            28 => 2,        // xSkewAngle, ySkewAngle
            _ => 4,         // 30: xSkewAngle, ySkewAngle, centerX, centerY
        };

        var raw = new int[4];
        for (int i = 0; i < fieldCount; i++)
            raw[i] = reader.ReadInt16();
        uint v = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;

        // FWORD fields are font units; F2DOT14 fields (scales, angles in half-turns) divide by 2^14.
        double Field(int i) => raw[i] + Delta(v, i);
        double F2(int i) => Field(i) / 16384.0;

        return baseFormat switch
        {
            14 => Affine2D.Translation(Field(0), Field(1)),
            16 => Affine2D.Scaling(F2(0), F2(1)),
            18 => Affine2D.AroundCenter(Affine2D.Scaling(F2(0), F2(1)), Field(2), Field(3)),
            20 => Affine2D.Scaling(F2(0), F2(0)),
            22 => Affine2D.AroundCenter(Affine2D.Scaling(F2(0), F2(0)), Field(1), Field(2)),
            24 => Affine2D.Rotation(F2(0) * Math.PI),
            26 => Affine2D.AroundCenter(Affine2D.Rotation(F2(0) * Math.PI), Field(1), Field(2)),
            28 => Affine2D.Skewing(F2(0) * Math.PI, F2(1) * Math.PI),
            _ => Affine2D.AroundCenter(Affine2D.Skewing(F2(0) * Math.PI, F2(1) * Math.PI), Field(2), Field(3)),
        };
    }

    /// <summary>An Affine2x3 / VarAffine2x3 table: six 16.16 Fixed values.</summary>
    private Affine2D ReadAffine(long offset, bool variable)
    {
        var reader = new FontReader(_table.Data.Span);
        reader.Seek(checked((int)offset));
        var raw = new int[6];
        for (int i = 0; i < 6; i++)
            raw[i] = reader.ReadInt32();
        uint v = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;

        double Fixed(int i) => (raw[i] + (double)Delta(v, i)) / 65536.0;
        return new Affine2D(Fixed(0), Fixed(1), Fixed(2), Fixed(3), Fixed(4), Fixed(5));
    }

    /// <summary>A ColorLine / VarColorLine, its stops resolved and stably sorted by offset.</summary>
    private (ColorStop[] Stops, ColorLineExtend Extend) ReadColorLine(long offset, bool variable)
    {
        var reader = new FontReader(_table.Data.Span);
        reader.Seek(checked((int)offset));
        int extend = reader.ReadUInt8();
        int count = reader.ReadUInt16();

        var stops = new ColorStop[count];
        for (int i = 0; i < count; i++)
        {
            int stopOffset = reader.ReadInt16();
            int paletteIndex = reader.ReadUInt16();
            int alpha = reader.ReadInt16();
            uint v = variable ? reader.ReadUInt32() : ColrV1Table.NoVariation;
            stops[i] = new ColorStop(
                (stopOffset + Delta(v, 0)) / 16384f,
                Resolve(paletteIndex, (alpha + Delta(v, 1)) / 16384f));
        }

        // Stable: stops sharing an offset keep their font order, which decides which one
        // applies below and which at-or-above that offset.
        var sorted = stops.Select((stop, index) => (stop, index))
            .OrderBy(s => s.stop.Offset).ThenBy(s => s.index)
            .Select(s => s.stop).ToArray();

        // An unrecognized extend mode is EXTEND_PAD.
        var mode = extend <= (int)ColorLineExtend.Reflect ? (ColorLineExtend)extend : ColorLineExtend.Pad;
        return (sorted, mode);
    }

    private LinearColor Resolve(int paletteIndex, float alpha)
    {
        PaletteColor color;
        if (paletteIndex == ForegroundIndex)
            color = _foreground;
        else if (paletteIndex < _cpal.EntryCount)
            color = _cpal.GetColor(_palette, paletteIndex);
        else
            throw new InvalidPaintException();

        return SrgbTransfer.ToLinearPremultiplied(color, alpha);
    }

    /// <summary>
    /// The variation delta for field <paramref name="field"/> of a record whose first variation
    /// index is <paramref name="varIndexBase"/>, in the field's raw (integer) units.
    /// </summary>
    private float Delta(uint varIndexBase, int field)
    {
        if (_regionScalars is null || varIndexBase == ColrV1Table.NoVariation)
            return 0f;

        // The sequence must not run past 0xFFFFFFFF; there is no wrap-around.
        ulong index = varIndexBase + (ulong)field;
        if (index >= ColrV1Table.NoVariation)
            return 0f;

        // No DeltaSetIndexMap: the index itself is the delta-set index, outer in the high word.
        var (outer, inner) = _table.VarIndexMap is { } map
            ? map.Map((int)Math.Min(index, int.MaxValue))
            : ((ushort)(index >> 16), (ushort)index);
        return _table.VariationStore!.GetDelta(outer, inner, _regionScalars);
    }

    /// <summary>A paint that must be ignored (it names something the font does not have).</summary>
    private sealed class InvalidPaintException : Exception
    {
    }

    /// <summary>The graph exceeded <see cref="MaxDepth"/> or <see cref="MaxPaints"/>.</summary>
    private sealed class GraphLimitException : Exception
    {
    }
}
