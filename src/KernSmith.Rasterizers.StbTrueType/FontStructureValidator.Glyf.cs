namespace KernSmith.Rasterizers.StbTrueType;

internal sealed partial class FontStructureValidator
{
    /// <summary>Deepest composite nesting (Native's <c>MaxpTable.MaxAllowedComponentDepth</c>).</summary>
    internal const int MaxComponentDepth = 64;

    /// <summary>Components one glyph may resolve across every level (Native's <c>GlyfTable.MaxComponentsPerGlyph</c>).</summary>
    internal const int MaxComponentsPerGlyph = 4096;

    /// <summary>
    /// Outline points (TrueType) or path commands (CFF) one glyph may produce, components and
    /// subroutines included (Native's <c>Type2Charstring.MaxCommands</c>). stb_truetype copies a
    /// composite's points once per component, so this also bounds that quadratic work.
    /// </summary>
    internal const int MaxGlyphPoints = 1 << 16;

    private const int ArgsAreWords = 0x0001;
    private const int ArgsAreXyValues = 0x0002;
    private const int WeHaveAScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int WeHaveAnXAndYScale = 0x0040;
    private const int WeHaveATwoByTwo = 0x0080;

    /// <summary>What a glyph expands to, components included; computed once per glyph.</summary>
    private struct GlyphTotals
    {
        public byte State; // 0 not visited, 1 on the current path, 2 done
        public int Components;
        public int Points;
        public int Height;
    }

    private GlyphTotals[]? _totals;

    /// <summary>
    /// Checks a glyph's record and, for a composite, every component down the tree; then the
    /// totals across the tree against <see cref="MaxComponentsPerGlyph"/> and
    /// <see cref="MaxGlyphPoints"/>. stb_truetype recurses into components with no depth or
    /// cycle check, so both are enforced here.
    /// </summary>
    private void CheckGlyfTree(int glyph)
    {
        _totals ??= new GlyphTotals[NumGlyphs];
        Summarize(glyph, 0);
    }

    private ref GlyphTotals Summarize(int glyph, int depth)
    {
        ref var totals = ref _totals![glyph];
        if (totals.State == 2)
        {
            if (depth + totals.Height > MaxComponentDepth)
                throw DepthFailure(glyph);
            return ref totals;
        }

        long start = LocaEntry(glyph);
        long end = LocaEntry(glyph + 1);
        int record = _glyf.Offset + (int)start;
        if (totals.State == 1)
            throw Fail("glyf", record, $"glyph {glyph} contains itself through its components.");

        if (end > start)
        {
            if (end - start < 10)
                throw Fail("glyf", record, $"glyph {glyph} record is {end - start} bytes, shorter than the 10-byte glyph header.");

            int contours = S16(record);
            if (contours != 0 && (S16(record + 2) > S16(record + 6) || S16(record + 4) > S16(record + 8)))
                throw Fail("glyf", record + 2, $"glyph {glyph} bounding box is inverted (xMin > xMax or yMin > yMax).");

            int recordEnd = _glyf.Offset + (int)end;
            if (contours >= 0)
            {
                totals.Points = CheckSimpleGlyph(glyph, record, recordEnd, contours);
            }
            else
            {
                totals.State = 1;
                try
                {
                    CheckComposite(glyph, record, recordEnd, depth, ref totals);
                }
                catch
                {
                    totals = default;
                    throw;
                }
            }
        }

        totals.State = 2;
        return ref totals;
    }

    private void CheckComposite(int glyph, int record, int recordEnd, int depth, ref GlyphTotals totals)
    {
        long components = 0;
        long points = 0;
        int height = 0;
        int p = record + 10;
        int flags;
        do
        {
            if (p + 4 > recordEnd)
                throw Fail("glyf", p, $"glyph {glyph} component record runs past the glyph record.");
            flags = U16(p);
            int child = U16(p + 2);

            // stb_truetype reads arguments only as XY offsets; point-matched arguments would
            // leave it reading the following components from the wrong place.
            if ((flags & ArgsAreXyValues) == 0)
                throw Fail("glyf", p, $"glyph {glyph} component uses point matching (ARGS_ARE_XY_VALUES clear), which this backend cannot read.");

            int transforms = flags & (WeHaveAScale | WeHaveAnXAndYScale | WeHaveATwoByTwo);
            if ((transforms & (transforms - 1)) != 0)
                throw Fail("glyf", p, $"glyph {glyph} component sets more than one transform flag (0x{flags:X4}).");
            int size = 4 + ((flags & ArgsAreWords) != 0 ? 4 : 2) + transforms switch
            {
                WeHaveAScale => 2,
                WeHaveAnXAndYScale => 4,
                WeHaveATwoByTwo => 8,
                _ => 0,
            };
            if (p + size > recordEnd)
                throw Fail("glyf", p, $"glyph {glyph} component record runs past the glyph record.");
            if (child >= NumGlyphs)
                throw Fail("glyf", p + 2, $"glyph {glyph} component glyph index {child} is not below numGlyphs {NumGlyphs}.");
            if (depth + 1 > MaxComponentDepth)
                throw DepthFailure(glyph);

            ref var childTotals = ref Summarize(child, depth + 1);
            components += 1 + childTotals.Components;
            points += childTotals.Points;
            height = Math.Max(height, 1 + childTotals.Height);
            if (components > MaxComponentsPerGlyph)
                throw Fail("glyf", record, $"glyph {glyph} resolves more than {MaxComponentsPerGlyph} components in total.");
            if (points > MaxGlyphPoints)
                throw Fail("glyf", record, $"glyph {glyph} resolves more than {MaxGlyphPoints} points in total.");

            p += size;
        }
        while ((flags & MoreComponents) != 0);

        totals.Components = (int)components;
        totals.Points = (int)points;
        totals.Height = height;
    }

    /// <summary>
    /// A simple glyph's endPtsOfContours must strictly increase, and its instructions, flags
    /// (repeat runs included) and both coordinate arrays must fit the record. Returns the point count.
    /// </summary>
    private int CheckSimpleGlyph(int glyph, int record, int recordEnd, int contours)
    {
        if (contours == 0)
            return 0;

        int endPts = record + 10;
        if (endPts + (2L * contours) + 2 > recordEnd)
            throw Fail("glyf", endPts, $"glyph {glyph} endPtsOfContours and instructionLength run past the glyph record.");

        int previous = -1;
        for (int i = 0; i < contours; i++)
        {
            int endPoint = U16(endPts + (2 * i));
            if (endPoint <= previous)
                throw Fail("glyf", endPts + (2 * i), $"glyph {glyph} endPtsOfContours is not strictly increasing at contour {i}.");
            previous = endPoint;
        }

        int points = previous + 1;
        int lastContourStart = contours > 1 ? U16(endPts + (2 * (contours - 2))) + 1 : 0;

        int instructionLength = U16(endPts + (2 * contours));
        long p = endPts + (2L * contours) + 2 + instructionLength;
        if (p > recordEnd)
            throw Fail("glyf", endPts + (2 * contours), $"glyph {glyph} instructions run past the glyph record.");

        long coordinateBytes = 0;
        int lastFlag = 0;
        long lastFlagAt = 0;
        int lastFlagCount = 0;
        for (int i = 0; i < points;)
        {
            lastFlagAt = p;
            if (p >= recordEnd)
                throw Fail("glyf", p, $"glyph {glyph} flags run past the glyph record.");
            int flag = _data[p++];
            int count = 1;
            if ((flag & 0x08) != 0)
            {
                if (p >= recordEnd)
                    throw Fail("glyf", p, $"glyph {glyph} flags run past the glyph record.");
                count += _data[p++];
                if (i + count > points)
                    throw Fail("glyf", p - 1, $"glyph {glyph} flag repeat count runs past the last point.");
            }

            int xBytes = (flag & 0x02) != 0 ? 1 : (flag & 0x10) != 0 ? 0 : 2;
            int yBytes = (flag & 0x04) != 0 ? 1 : (flag & 0x20) != 0 ? 0 : 2;
            coordinateBytes += (long)count * (xBytes + yBytes);
            i += count;
            lastFlag = flag;
            lastFlagCount = count;
        }

        if (p + coordinateBytes > recordEnd)
            throw Fail("glyf", p, $"glyph {glyph} coordinates run past the glyph record.");

        // stb_truetype starts an off-curve contour from the point after it; for a one-point last
        // contour that point lies past its vertex buffer. Such a contour encloses nothing (several
        // Windows fonts draw their space glyphs as one), so the point is made on-curve in the
        // backend's private copy: the outline then has the same zero area and stb reads nothing
        // more. A flag byte a repeat run shares with earlier points cannot be changed alone.
        if (lastContourStart == points - 1 && (lastFlag & 0x01) == 0)
        {
            if (lastFlagCount != 1)
                throw Fail("glyf", lastFlagAt,
                    $"glyph {glyph} last contour is a single off-curve point whose flag a repeat run shares, which stb_truetype reads past.");
            _data[lastFlagAt] |= 0x01;
        }

        return points;
    }

    private FontParsingException DepthFailure(int glyph) =>
        Fail("glyf", _glyf.Offset + (int)LocaEntry(glyph), $"glyph {glyph} components nest deeper than {MaxComponentDepth} levels.");
}
