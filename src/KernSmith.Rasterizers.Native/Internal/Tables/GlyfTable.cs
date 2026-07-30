namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The <c>glyf</c> (glyph outline) table. Decodes TrueType glyph outlines on demand:
/// simple glyphs from their delta-encoded point stream, composite glyphs by recursively
/// resolving and transforming their components.
/// </summary>
/// <remarks>
/// Hinting instructions are skipped — the native rasterizer does not run the TrueType
/// interpreter. Implicit on-curve midpoints are materialised once the glyph is fully
/// assembled, so <see cref="GetGlyph"/> never returns two adjacent off-curve points.
/// </remarks>
internal sealed class GlyfTable
{
    /// <summary>numberOfContours(2) + xMin/yMin/xMax/yMax(2 each).</summary>
    private const int GlyphHeaderLength = 10;

    // Simple glyph point flags.
    private const byte OnCurvePoint = 0x01;
    private const byte XShortVector = 0x02;
    private const byte YShortVector = 0x04;
    private const byte RepeatFlag = 0x08;
    private const byte XSameOrPositive = 0x10;
    private const byte YSameOrPositive = 0x20;

    // Composite component flags.
    private const ushort Arg1And2AreWords = 0x0001;
    private const ushort ArgsAreXyValues = 0x0002;
    private const ushort WeHaveAScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveAnXAndYScale = 0x0040;
    private const ushort WeHaveATwoByTwo = 0x0080;
    private const ushort ScaledComponentOffset = 0x0800;
    private const ushort UnscaledComponentOffset = 0x1000;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly LocaTable _loca;
    private readonly int _maxComponentDepth;

    /// <summary>Creates a reader over the raw <c>glyf</c> bytes.</summary>
    /// <param name="data">The raw <c>glyf</c> table bytes.</param>
    /// <param name="loca">The parsed <c>loca</c> table locating each glyph within <paramref name="data"/>.</param>
    /// <param name="maxComponentDepth">Composite recursion limit, from <c>maxp</c>.</param>
    public GlyfTable(ReadOnlyMemory<byte> data, LocaTable loca, int maxComponentDepth)
    {
        ArgumentNullException.ThrowIfNull(loca);

        _data = data;
        _loca = loca;
        _maxComponentDepth = maxComponentDepth > 0 ? maxComponentDepth : MaxpTable.MaxAllowedComponentDepth;
    }

    /// <summary>Number of glyphs addressable through this table.</summary>
    public int GlyphCount => _loca.GlyphCount;

    /// <summary>
    /// Decodes a glyph's outline, resolving composites and inserting implicit on-curve points.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the glyph index is outside the font.</exception>
    /// <exception cref="FontFormatException">If the glyph's data is malformed or truncated.</exception>
    public ParsedGlyph GetGlyph(int glyphIndex)
    {
        var glyph = ParseGlyph(glyphIndex, depth: 0);
        return InsertImplicitOnCurvePoints(glyph);
    }

    /// <summary>
    /// Parses a glyph into raw contours — no implicit midpoints. Composite point-matching
    /// arguments index the raw point stream, so assembly must happen before midpoints are
    /// inserted or the indices would shift.
    /// </summary>
    private ParsedGlyph ParseGlyph(int glyphIndex, int depth)
    {
        if (depth > _maxComponentDepth)
            throw new FontFormatException("glyf", 0,
                $"composite recursion at glyph {glyphIndex} exceeded the depth limit of {_maxComponentDepth}.");

        var (offset, length) = _loca.GetGlyphRange(glyphIndex);

        // Equal consecutive loca offsets mean "no outline" — a space, for example.
        if (length == 0)
            return ParsedGlyph.Empty(glyphIndex);

        if (length < GlyphHeaderLength)
            throw new FontFormatException("glyf", offset,
                $"glyph {glyphIndex} is {length} bytes, too short for a {GlyphHeaderLength}-byte header.");
        if ((long)offset + length > _data.Length)
            throw new FontFormatException("glyf", offset,
                $"glyph {glyphIndex} extends to {offset + length} bytes but the table is only {_data.Length}.");

        var reader = new FontReader(_data.Span.Slice(offset, length));

        short numberOfContours = reader.ReadInt16();
        short xMin = reader.ReadInt16();
        short yMin = reader.ReadInt16();
        short xMax = reader.ReadInt16();
        short yMax = reader.ReadInt16();

        GlyphContour[] contours = numberOfContours >= 0
            ? ParseSimpleContours(ref reader, glyphIndex, numberOfContours)
            : ParseCompositeContours(ref reader, glyphIndex, depth);

        return new ParsedGlyph(glyphIndex, contours, xMin, yMin, xMax, yMax, numberOfContours < 0);
    }

    private static GlyphContour[] ParseSimpleContours(ref FontReader reader, int glyphIndex, int numberOfContours)
    {
        if (numberOfContours == 0)
            return [];

        var endPoints = new int[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPoints[i] = reader.ReadUInt16();
            if (i > 0 && endPoints[i] <= endPoints[i - 1])
                throw new FontFormatException("glyf", reader.Position,
                    $"glyph {glyphIndex} contour {i} ends at point {endPoints[i]}, not after contour {i - 1} ({endPoints[i - 1]}).");
        }

        int pointCount = endPoints[numberOfContours - 1] + 1;

        // Hinting bytecode: present, but never executed.
        ushort instructionLength = reader.ReadUInt16();
        reader.Skip(instructionLength);

        byte[] flags = ReadFlags(ref reader, glyphIndex, pointCount);
        int[] xs = ReadCoordinates(ref reader, flags, XShortVector, XSameOrPositive);
        int[] ys = ReadCoordinates(ref reader, flags, YShortVector, YSameOrPositive);

        var contours = new GlyphContour[numberOfContours];
        int start = 0;
        for (int c = 0; c < numberOfContours; c++)
        {
            int end = endPoints[c];
            var points = new GlyphPoint[end - start + 1];
            for (int i = 0; i < points.Length; i++)
            {
                int index = start + i;
                points[i] = new GlyphPoint(xs[index], ys[index], (flags[index] & OnCurvePoint) != 0);
            }

            contours[c] = new GlyphContour(points);
            start = end + 1;
        }

        return contours;
    }

    /// <summary>Reads the run-length-encoded point flags.</summary>
    private static byte[] ReadFlags(ref FontReader reader, int glyphIndex, int pointCount)
    {
        var flags = new byte[pointCount];

        for (int i = 0; i < pointCount;)
        {
            byte flag = reader.ReadUInt8();
            flags[i++] = flag;

            if ((flag & RepeatFlag) == 0)
                continue;

            int repeat = reader.ReadUInt8();
            if (i + repeat > pointCount)
                throw new FontFormatException("glyf", reader.Position,
                    $"glyph {glyphIndex} flag repeat count overruns its {pointCount} points.");

            for (int r = 0; r < repeat; r++)
                flags[i++] = flag;
        }

        return flags;
    }

    /// <summary>
    /// Reads one delta-encoded coordinate axis. A short flag means the value is a single
    /// unsigned byte whose sign comes from the "same or positive" flag; without the short
    /// flag, "same or positive" means a zero delta and anything else is a signed 16-bit delta.
    /// </summary>
    private static int[] ReadCoordinates(ref FontReader reader, byte[] flags, byte shortFlag, byte sameOrPositiveFlag)
    {
        var coordinates = new int[flags.Length];
        int value = 0;

        for (int i = 0; i < flags.Length; i++)
        {
            byte flag = flags[i];

            if ((flag & shortFlag) != 0)
            {
                byte delta = reader.ReadUInt8();
                value += (flag & sameOrPositiveFlag) != 0 ? delta : -delta;
            }
            else if ((flag & sameOrPositiveFlag) == 0)
            {
                value += reader.ReadInt16();
            }

            coordinates[i] = value;
        }

        return coordinates;
    }

    private GlyphContour[] ParseCompositeContours(ref FontReader reader, int glyphIndex, int depth)
    {
        var contours = new List<GlyphContour>();
        ushort flags;

        do
        {
            flags = reader.ReadUInt16();
            int componentIndex = reader.ReadUInt16();

            int arg1, arg2;
            if ((flags & Arg1And2AreWords) != 0)
            {
                arg1 = reader.ReadInt16();
                arg2 = reader.ReadInt16();
            }
            else
            {
                arg1 = reader.ReadInt8();
                arg2 = reader.ReadInt8();
            }

            // 2x2 transform, row-major [a b; c d], identity unless the component says otherwise.
            float a = 1f, b = 0f, c = 0f, d = 1f;
            if ((flags & WeHaveAScale) != 0)
            {
                a = d = reader.ReadF2Dot14();
            }
            else if ((flags & WeHaveAnXAndYScale) != 0)
            {
                a = reader.ReadF2Dot14();
                d = reader.ReadF2Dot14();
            }
            else if ((flags & WeHaveATwoByTwo) != 0)
            {
                a = reader.ReadF2Dot14();
                b = reader.ReadF2Dot14();
                c = reader.ReadF2Dot14();
                d = reader.ReadF2Dot14();
            }

            // Cyclic references (direct or indirect) are caught by the depth limit.
            var component = ParseGlyph(componentIndex, depth + 1);
            var transformed = ApplyLinearTransform(component.Contours, a, b, c, d);

            float dx, dy;
            if ((flags & ArgsAreXyValues) != 0)
            {
                dx = arg1;
                dy = arg2;

                // Offsets are in the parent's units unless the component explicitly asks for
                // them to be scaled by its own transform. Unscaled is the near-universal default.
                if ((flags & ScaledComponentOffset) != 0 && (flags & UnscaledComponentOffset) == 0)
                {
                    (dx, dy) = (a * dx + c * dy, b * dx + d * dy);
                }
            }
            else
            {
                // Point matching: align point arg1 of the glyph assembled so far with point
                // arg2 of this component. Rare, but part of the format.
                var anchor = GetPointAt(contours, arg1, glyphIndex, "composite");
                var target = GetPointAt(transformed, arg2, componentIndex, "component");
                dx = anchor.X - target.X;
                dy = anchor.Y - target.Y;
            }

            // ROUND_XY_TO_GRID is deliberately ignored: grid fitting is a ppem-dependent
            // hinting concern and these coordinates are still in font design units.
            Translate(transformed, dx, dy);
            contours.AddRange(transformed);
        }
        while ((flags & MoreComponents) != 0);

        return contours.ToArray();
    }

    private static GlyphContour[] ApplyLinearTransform(GlyphContour[] contours, float a, float b, float c, float d)
    {
        bool isIdentity = a == 1f && b == 0f && c == 0f && d == 1f;
        var result = new GlyphContour[contours.Length];

        for (int i = 0; i < contours.Length; i++)
        {
            var source = contours[i].Points;
            var points = new GlyphPoint[source.Length];

            for (int p = 0; p < source.Length; p++)
            {
                var point = source[p];
                points[p] = isIdentity
                    ? point
                    : new GlyphPoint(
                        (a * point.X) + (c * point.Y),
                        (b * point.X) + (d * point.Y),
                        point.OnCurve);
            }

            result[i] = new GlyphContour(points);
        }

        return result;
    }

    private static void Translate(GlyphContour[] contours, float dx, float dy)
    {
        if (dx == 0f && dy == 0f)
            return;

        foreach (var contour in contours)
        {
            var points = contour.Points;
            for (int i = 0; i < points.Length; i++)
                points[i] = new GlyphPoint(points[i].X + dx, points[i].Y + dy, points[i].OnCurve);
        }
    }

    private static GlyphPoint GetPointAt(IReadOnlyList<GlyphContour> contours, int pointIndex, int glyphIndex, string role)
    {
        if (pointIndex >= 0)
        {
            int remaining = pointIndex;
            foreach (var contour in contours)
            {
                if (remaining < contour.Points.Length)
                    return contour.Points[remaining];
                remaining -= contour.Points.Length;
            }
        }

        throw new FontFormatException("glyf", 0,
            $"point index {pointIndex} is outside the {role} glyph {glyphIndex}.");
    }

    /// <summary>
    /// Materialises the on-curve midpoint that TrueType leaves implicit between two
    /// consecutive off-curve control points, including across the closing wrap-around.
    /// </summary>
    private static ParsedGlyph InsertImplicitOnCurvePoints(ParsedGlyph glyph)
    {
        if (glyph.IsEmpty)
            return glyph;

        GlyphContour[]? rebuilt = null;

        for (int c = 0; c < glyph.Contours.Length; c++)
        {
            var points = glyph.Contours[c].Points;
            if (points.Length == 0 || !HasAdjacentOffCurvePoints(points))
                continue;

            rebuilt ??= (GlyphContour[])glyph.Contours.Clone();

            var expanded = new List<GlyphPoint>(points.Length + 8);
            for (int i = 0; i < points.Length; i++)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Length];
                expanded.Add(current);

                if (!current.OnCurve && !next.OnCurve)
                {
                    expanded.Add(new GlyphPoint(
                        (current.X + next.X) / 2f,
                        (current.Y + next.Y) / 2f,
                        OnCurve: true));
                }
            }

            rebuilt[c] = new GlyphContour(expanded.ToArray());
        }

        return rebuilt is null
            ? glyph
            : new ParsedGlyph(glyph.GlyphIndex, rebuilt, glyph.XMin, glyph.YMin, glyph.XMax, glyph.YMax, glyph.IsComposite);
    }

    private static bool HasAdjacentOffCurvePoints(GlyphPoint[] points)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (!points[i].OnCurve && !points[(i + 1) % points.Length].OnCurve)
                return true;
        }

        return false;
    }
}
