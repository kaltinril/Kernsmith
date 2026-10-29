namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The <c>glyf</c> (glyph outline) table. Decodes TrueType glyph outlines on demand:
/// simple glyphs from their delta-encoded point stream, composite glyphs by recursively
/// resolving and transforming their components.
/// </summary>
/// <remarks>
/// Hinting instructions are skipped — the native rasterizer does not run the TrueType
/// interpreter. Implicit on-curve midpoints are materialised once the glyph is fully
/// assembled, so <see cref="GetGlyph(int)"/> never returns two adjacent off-curve points.
/// </remarks>
internal sealed class GlyfTable
{
    /// <summary>numberOfContours(2) + xMin/yMin/xMax/yMax(2 each).</summary>
    private const int GlyphHeaderLength = 10;

    /// <summary>
    /// Ceiling on how many components a single top-level glyph may resolve in total, across
    /// every level rather than per level.
    /// </summary>
    /// <remarks>
    /// The depth limit alone does not bound the work. In a chain where every glyph carries two
    /// components pointing at the next one, every path is exactly as deep as the chain, so the
    /// depth guard never fires — yet the call tree is 2^depth nodes, and a table of a few dozen
    /// 22-byte glyphs pins a core indefinitely. Fonts can arrive from an untrusted source, so
    /// the total is capped as well. Real composites resolve a handful of components; accented
    /// Latin is two.
    /// </remarks>
    public const int MaxComponentsPerGlyph = 4096;

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
    public ParsedGlyph GetGlyph(int glyphIndex) => GetGlyph(glyphIndex, variations: null, out _);

    /// <summary>
    /// Decodes a glyph at a variation instance: every glyph of a composite tree is varied at its
    /// own level — outline points for simple glyphs, placement offsets for components — before
    /// it is assembled, and the outline is moved so the varied horizontal origin stays at x = 0.
    /// </summary>
    /// <param name="glyphIndex">The glyph to decode.</param>
    /// <param name="variations">Delta source for the current instance; null decodes the static glyph.</param>
    /// <param name="phantom">How the glyph's horizontal phantom points moved.</param>
    /// <exception cref="ArgumentOutOfRangeException">If the glyph index is outside the font.</exception>
    /// <exception cref="FontFormatException">If the glyph's data is malformed or truncated.</exception>
    public ParsedGlyph GetGlyph(int glyphIndex, IGlyphVariationProvider? variations, out PhantomDeltas phantom)
    {
        int componentBudget = MaxComponentsPerGlyph;
        var glyph = ParseGlyph(glyphIndex, depth: 0, ref componentBudget, variations, out phantom);

        // The left side bearing phantom point is the horizontal origin; the pen sits on it.
        if (!phantom.Left.Equals(0f))
            Translate(glyph.Contours, -phantom.Left, 0f);

        return InsertImplicitOnCurvePoints(glyph);
    }

    /// <summary>
    /// Parses a glyph into raw contours — no implicit midpoints. Composite point-matching
    /// arguments index the raw point stream, so assembly must happen before midpoints are
    /// inserted or the indices would shift.
    /// </summary>
    /// <param name="glyphIndex">Index of the glyph to parse.</param>
    /// <param name="depth">Current composite recursion depth; 0 for a top-level glyph.</param>
    /// <param name="componentBudget">
    /// Remaining components this top-level glyph may still resolve; see
    /// <see cref="MaxComponentsPerGlyph"/>. Carried by reference so it counts the whole
    /// expansion rather than resetting down each branch.
    /// </param>
    /// <param name="variations">Delta source for the current instance, or null.</param>
    /// <param name="phantom">How this glyph's horizontal phantom points moved.</param>
    private ParsedGlyph ParseGlyph(
        int glyphIndex, int depth, ref int componentBudget, IGlyphVariationProvider? variations, out PhantomDeltas phantom)
    {
        if (depth > _maxComponentDepth)
            throw new FontFormatException("glyf", 0,
                $"composite recursion at glyph {glyphIndex} exceeded the depth limit of {_maxComponentDepth}.");

        var (offset, length) = _loca.GetGlyphRange(glyphIndex);

        // Equal consecutive loca offsets mean "no outline" — a space, for example. Its phantom
        // points can still vary, which is how a space's advance follows the instance.
        if (length == 0)
        {
            phantom = PhantomOnly(glyphIndex, variations);
            return ParsedGlyph.Empty(glyphIndex);
        }

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
            ? ParseSimpleContours(ref reader, glyphIndex, numberOfContours, variations, out phantom)
            : ParseCompositeContours(ref reader, glyphIndex, depth, ref componentBudget, variations, out phantom);

        return new ParsedGlyph(glyphIndex, contours, xMin, yMin, xMax, yMax, numberOfContours < 0);
    }

    private static GlyphContour[] ParseSimpleContours(
        ref FontReader reader, int glyphIndex, int numberOfContours, IGlyphVariationProvider? variations, out PhantomDeltas phantom)
    {
        if (numberOfContours == 0)
        {
            phantom = PhantomOnly(glyphIndex, variations);
            return [];
        }

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

        float[]? dx = null, dy = null;
        phantom = default;
        if (variations is not null && variations.TryGetDeltas(glyphIndex, pointCount, xs, ys, endPoints, out var vx, out var vy))
        {
            (dx, dy) = (vx, vy);
            phantom = new PhantomDeltas(vx[pointCount], vx[pointCount + 1], AnyNonZero(vx, vy, pointCount));
        }

        var contours = new GlyphContour[numberOfContours];
        int start = 0;
        for (int c = 0; c < numberOfContours; c++)
        {
            int end = endPoints[c];
            var points = new GlyphPoint[end - start + 1];
            for (int i = 0; i < points.Length; i++)
            {
                int index = start + i;
                bool onCurve = (flags[index] & OnCurvePoint) != 0;
                points[i] = dx is null || dy is null
                    ? new GlyphPoint(xs[index], ys[index], onCurve)
                    : new GlyphPoint(xs[index] + dx[index], ys[index] + dy[index], onCurve);
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

    private GlyphContour[] ParseCompositeContours(
        ref FontReader reader, int glyphIndex, int depth, ref int componentBudget,
        IGlyphVariationProvider? variations, out PhantomDeltas phantom)
    {
        // Read every component record before resolving any: the composite's variation data
        // addresses its components by position and ends with phantom points after the last one,
        // so the component count must be known first.
        var components = new List<ComponentRecord>();
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

            components.Add(new ComponentRecord(flags, componentIndex, arg1, arg2, a, b, c, d, reader.Position));
        }
        while ((flags & MoreComponents) != 0);

        float[]? offsetX = null, offsetY = null;
        phantom = default;
        if (variations is not null
            && variations.TryGetDeltas(glyphIndex, components.Count, [], [], [], out var vx, out var vy))
        {
            (offsetX, offsetY) = (vx, vy);
            phantom = new PhantomDeltas(vx[components.Count], vx[components.Count + 1], AnyNonZero(vx, vy, components.Count));
        }

        var contours = new List<GlyphContour>();
        for (int i = 0; i < components.Count; i++)
        {
            var record = components[i];
            float a = record.A, b = record.B, c = record.C, d = record.D;

            // Cyclic references (direct or indirect) are caught by the depth limit; a wide but
            // legal-depth expansion is caught by the budget.
            if (--componentBudget < 0)
                throw new FontFormatException("glyf", record.Position,
                    $"glyph {glyphIndex} expands to more than {MaxComponentsPerGlyph} components.");

            var component = ParseGlyph(record.GlyphIndex, depth + 1, ref componentBudget, variations, out var componentPhantom);
            var transformed = ApplyLinearTransform(component.Contours, a, b, c, d);

            // USE_MY_METRICS is not followed: it hands the composite a component's hinted
            // metrics, and unhinted the composite's own hmtx entry and phantom deltas apply —
            // the same advance HVAR gives, and the one the static path already uses.
            if (componentPhantom.OutlineVaried)
                phantom = phantom with { OutlineVaried = true };

            float dx, dy;
            if ((record.Flags & ArgsAreXyValues) != 0)
            {
                // Deltas move the offset itself, so a scaled offset scales the varied value.
                dx = record.Arg1 + (offsetX?[i] ?? 0f);
                dy = record.Arg2 + (offsetY?[i] ?? 0f);

                // Offsets are in the parent's units unless the component explicitly asks for
                // them to be scaled by its own transform. Unscaled is the near-universal default.
                if ((record.Flags & ScaledComponentOffset) != 0 && (record.Flags & UnscaledComponentOffset) == 0)
                {
                    (dx, dy) = (a * dx + c * dy, b * dx + d * dy);
                }
            }
            else
            {
                // Point matching: align point arg1 of the glyph assembled so far with point
                // arg2 of this component. Rare, but part of the format. Both points are already
                // varied, so a delta stored for this component is ignored, as the spec directs.
                var anchor = GetPointAt(contours, record.Arg1, glyphIndex, "composite");
                var target = GetPointAt(transformed, record.Arg2, record.GlyphIndex, "component");
                dx = anchor.X - target.X;
                dy = anchor.Y - target.Y;
            }

            // ROUND_XY_TO_GRID is deliberately ignored: grid fitting is a ppem-dependent
            // hinting concern and these coordinates are still in font design units.
            Translate(transformed, dx, dy);
            contours.AddRange(transformed);
        }

        return contours.ToArray();
    }

    /// <summary>Phantom deltas for a glyph with no outline points of its own.</summary>
    private static PhantomDeltas PhantomOnly(int glyphIndex, IGlyphVariationProvider? variations) =>
        variations is not null && variations.TryGetDeltas(glyphIndex, 0, [], [], [], out var dx, out _)
            ? new PhantomDeltas(dx[0], dx[1], OutlineVaried: false)
            : default;

    /// <summary>Whether any of the first <paramref name="count"/> X or Y deltas is non-zero.</summary>
    private static bool AnyNonZero(float[] dx, float[] dy, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!dx[i].Equals(0f) || !dy[i].Equals(0f))
                return true;
        }

        return false;
    }

    /// <summary>A composite component as stored: what to place, and how.</summary>
    private readonly record struct ComponentRecord(
        ushort Flags, int GlyphIndex, int Arg1, int Arg2, float A, float B, float C, float D, int Position);

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
