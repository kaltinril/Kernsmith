using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// What a simple glyph's header and contour end points say, read without decoding its points.
/// </summary>
/// <param name="NumberOfContours">Contours in the glyph; 0 when it has no outline (no data, or no contours).</param>
/// <param name="XMin">The header box, as stored; all zero for a glyph with no data.</param>
/// <param name="YMin">The header box, as stored.</param>
/// <param name="XMax">The header box, as stored.</param>
/// <param name="YMax">The header box, as stored.</param>
/// <param name="LargestContour">Points in the largest contour. A contour of two or more points
/// always draws something; a one-point contour draws only if that point is off-curve (it gains
/// an implicit on-curve partner), which the end points alone cannot tell.</param>
internal readonly record struct SimpleGlyphHeader(
    int NumberOfContours, short XMin, short YMin, short XMax, short YMax, int LargestContour);

/// <summary>
/// The default-instance fast path for simple glyphs: a header-only read for metrics, and a
/// decode straight into outline commands through reusable buffers instead of a
/// <see cref="ParsedGlyph"/>. Both produce exactly what the general path
/// (<see cref="GetGlyph(int)"/> then <see cref="OutlineExtractor.Extract"/>) does.
/// </summary>
internal sealed partial class GlyfTable
{
    /// <summary>
    /// Reads a glyph's header and contour end points. False for a composite glyph, which only
    /// the general path resolves.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the glyph index is outside the font.</exception>
    /// <exception cref="FontFormatException">If the header or end points are malformed or truncated.</exception>
    public bool TryReadSimpleHeader(int glyphIndex, out SimpleGlyphHeader header)
    {
        var data = SimpleGlyphData(glyphIndex);
        if (data.IsEmpty)
        {
            header = default;
            return true;
        }

        var reader = new FontReader(data);
        short numberOfContours = reader.ReadInt16();
        short xMin = reader.ReadInt16();
        short yMin = reader.ReadInt16();
        short xMax = reader.ReadInt16();
        short yMax = reader.ReadInt16();

        if (numberOfContours < 0)
        {
            header = default;
            return false;
        }

        int largest = 0;
        int previousEnd = -1;
        for (int i = 0; i < numberOfContours; i++)
        {
            int end = ReadEndPoint(ref reader, glyphIndex, i, previousEnd);
            largest = Math.Max(largest, end - previousEnd);
            previousEnd = end;
        }

        header = new SimpleGlyphHeader(numberOfContours, xMin, yMin, xMax, yMax, largest);
        return true;
    }

    /// <summary>
    /// Decodes a simple glyph with at least one contour (see <see cref="TryReadSimpleHeader"/>)
    /// into its outline: implicit on-curve midpoints materialised exactly as
    /// <see cref="GetGlyph(int)"/> does, then walked by <see cref="OutlineExtractor"/>. Only the
    /// returned outline is allocated; every intermediate lives in <paramref name="scratch"/>.
    /// </summary>
    /// <param name="glyphIndex">A simple glyph with at least one contour.</param>
    /// <param name="offsetX">Added to every X, as <see cref="OutlineExtractor.Extract"/> adds it.</param>
    /// <param name="scratch">Buffers reused across calls; one per thread.</param>
    /// <param name="controlBox">The bounds of the glyph's stored points, before
    /// <paramref name="offsetX"/>: what <see cref="ReadSimpleControlBox"/> returns.</param>
    /// <exception cref="FontFormatException">If the glyph's data is malformed or truncated.</exception>
    public GlyphOutline DecodeSimpleOutline(int glyphIndex, float offsetX, GlyfScratch scratch, out OutlineBounds controlBox)
    {
        int numberOfContours = ReadSimplePoints(glyphIndex, scratch, out int pointCount);
        var endPoints = scratch.EndPoints(numberOfContours);
        var flags = scratch.Flags(pointCount);
        var xs = scratch.Xs(pointCount);
        var ys = scratch.Ys(pointCount);
        controlBox = ControlBox(xs, ys);

        // Materialising implicit points at most doubles a contour.
        var commands = scratch.Commands(OutlineExtractor.MaxCommands(2 * pointCount, numberOfContours));
        int commandCount = 0;
        int start = 0;
        for (int c = 0; c < numberOfContours; c++)
        {
            int end = endPoints[c];
            var contour = Materialise(flags, xs, ys, start, end - start + 1, scratch.Points(2 * (end - start + 1)));
            commandCount = OutlineExtractor.AppendContour(commands, commandCount, contour, offsetX);
            start = end + 1;
        }

        return OutlineExtractor.ToOutline(commands[..commandCount]);
    }

    /// <summary>
    /// The bounds of a simple glyph's stored points — on- and off-curve, so they contain the
    /// outline — read without building the outline or keeping the coordinates. For a glyph with
    /// at least one contour whose end points <see cref="TryReadSimpleHeader"/> has validated.
    /// </summary>
    /// <exception cref="FontFormatException">If the glyph's data is malformed or truncated.</exception>
    public OutlineBounds ReadSimpleControlBox(int glyphIndex, GlyfScratch scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);

        var reader = new FontReader(SimpleGlyphData(glyphIndex));
        int numberOfContours = reader.ReadInt16();
        if (numberOfContours <= 0)
            throw new InvalidOperationException($"Glyph {glyphIndex} is not a simple glyph with contours.");

        // The header box, then every end point but the last, which counts the points.
        reader.Skip(8 + (2 * (numberOfContours - 1)));
        int pointCount = reader.ReadUInt16() + 1;

        // Hinting bytecode: present, but never executed.
        reader.Skip(reader.ReadUInt16());

        var flags = scratch.Flags(pointCount);
        ReadFlags(ref reader, glyphIndex, flags);
        var (xMin, xMax) = CoordinateRange(ref reader, flags, XShortVector, XSameOrPositive);
        var (yMin, yMax) = CoordinateRange(ref reader, flags, YShortVector, YSameOrPositive);
        return new OutlineBounds(xMin, yMin, xMax, yMax);
    }

    /// <summary><see cref="ReadCoordinates"/> reduced to the axis's smallest and largest value.</summary>
    private static (int Min, int Max) CoordinateRange(ref FontReader reader, ReadOnlySpan<byte> flags, byte shortFlag, byte sameOrPositiveFlag)
    {
        int value = 0, min = int.MaxValue, max = int.MinValue;
        foreach (byte flag in flags)
        {
            if ((flag & shortFlag) != 0)
            {
                byte delta = reader.ReadUInt8();
                value += (flag & sameOrPositiveFlag) != 0 ? delta : -delta;
            }
            else if ((flag & sameOrPositiveFlag) == 0)
            {
                value += reader.ReadInt16();
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return (min, max);
    }

    /// <summary>
    /// Reads a simple glyph's contour end points, flags and absolute coordinates into
    /// <paramref name="scratch"/>; returns its contour count.
    /// </summary>
    private int ReadSimplePoints(int glyphIndex, GlyfScratch scratch, out int pointCount)
    {
        ArgumentNullException.ThrowIfNull(scratch);

        var reader = new FontReader(SimpleGlyphData(glyphIndex));
        int numberOfContours = reader.ReadInt16();
        if (numberOfContours <= 0)
            throw new InvalidOperationException($"Glyph {glyphIndex} is not a simple glyph with contours.");
        reader.Skip(8); // the header box

        var endPoints = scratch.EndPoints(numberOfContours);
        int previousEnd = -1;
        for (int i = 0; i < numberOfContours; i++)
            previousEnd = endPoints[i] = ReadEndPoint(ref reader, glyphIndex, i, previousEnd);

        pointCount = previousEnd + 1;

        // Hinting bytecode: present, but never executed.
        reader.Skip(reader.ReadUInt16());

        var flags = scratch.Flags(pointCount);
        ReadFlags(ref reader, glyphIndex, flags);
        ReadCoordinates(ref reader, flags, scratch.Xs(pointCount), XShortVector, XSameOrPositive);
        ReadCoordinates(ref reader, flags, scratch.Ys(pointCount), YShortVector, YSameOrPositive);
        return numberOfContours;
    }

    private static OutlineBounds ControlBox(ReadOnlySpan<int> xs, ReadOnlySpan<int> ys)
    {
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        for (int i = 0; i < xs.Length; i++)
        {
            xMin = Math.Min(xMin, xs[i]);
            xMax = Math.Max(xMax, xs[i]);
            yMin = Math.Min(yMin, ys[i]);
            yMax = Math.Max(yMax, ys[i]);
        }

        return new OutlineBounds(xMin, yMin, xMax, yMax);
    }

    /// <summary>
    /// One contour's points with the implicit on-curve midpoint between every two consecutive
    /// off-curve points (wrap-around included) made explicit — the same points, in the same
    /// order and with the same arithmetic, as <see cref="InsertImplicitOnCurvePoints"/>.
    /// </summary>
    private static ReadOnlySpan<GlyphPoint> Materialise(
        ReadOnlySpan<byte> flags, ReadOnlySpan<int> xs, ReadOnlySpan<int> ys, int start, int count, Span<GlyphPoint> destination)
    {
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            int index = start + i;
            int next = start + ((i + 1) % count);
            var current = new GlyphPoint(xs[index], ys[index], (flags[index] & OnCurvePoint) != 0);
            destination[written++] = current;

            if (!current.OnCurve && (flags[next] & OnCurvePoint) == 0)
            {
                destination[written++] = new GlyphPoint(
                    (current.X + xs[next]) / 2f,
                    (current.Y + ys[next]) / 2f,
                    OnCurve: true);
            }
        }

        return destination[..written];
    }

    /// <summary>Reads contour <paramref name="contour"/>'s end point, which must follow the previous one.</summary>
    private static int ReadEndPoint(ref FontReader reader, int glyphIndex, int contour, int previousEnd)
    {
        int end = reader.ReadUInt16();
        if (contour > 0 && end <= previousEnd)
            throw new FontFormatException("glyf", reader.Position,
                $"glyph {glyphIndex} contour {contour} ends at point {end}, not after contour {contour - 1} ({previousEnd}).");
        return end;
    }

    /// <summary>
    /// A glyph's bytes, empty for a glyph with no outline, with the bounds checks
    /// <see cref="ParseGlyph"/> applies before reading.
    /// </summary>
    private ReadOnlySpan<byte> SimpleGlyphData(int glyphIndex)
    {
        var (offset, length) = _loca.GetGlyphRange(glyphIndex);
        if (length == 0)
            return [];

        if (length < GlyphHeaderLength)
            throw new FontFormatException("glyf", offset,
                $"glyph {glyphIndex} is {length} bytes, too short for a {GlyphHeaderLength}-byte header.");
        if ((long)offset + length > _data.Length)
            throw new FontFormatException("glyf", offset,
                $"glyph {glyphIndex} extends to {offset + length} bytes but the table is only {_data.Length}.");

        return _data.Span.Slice(offset, length);
    }
}
