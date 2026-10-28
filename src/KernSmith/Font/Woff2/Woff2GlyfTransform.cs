using System.Buffers.Binary;

namespace KernSmith.Font.Woff2;

/// <summary>
/// Reverses the WOFF2 <c>glyf</c>/<c>loca</c> transform (W3C WOFF2 section 5.1, transform
/// version 0): rebuilds TrueType glyph records from the seven substreams and regenerates
/// <c>loca</c>.
/// </summary>
/// <remarks>
/// Simple glyphs are re-encoded with the standard compact flag/coordinate packing, so the
/// bytes can differ from the original font while the outline, bbox, instructions and
/// OVERLAP_SIMPLE flag are identical — which is all the spec promises. Composite records and
/// instructions are copied verbatim. Each glyph is padded to 4 bytes; if that pushes a
/// short-format <c>loca</c> past its 128 KiB reach, the long format is written instead and
/// <see cref="Result.IndexFormat"/> reports it so <c>head.indexToLocFormat</c> can follow.
/// </remarks>
internal static class Woff2GlyfTransform
{
    private const int HeaderSize = 36;

    // TrueType simple-glyph flags.
    private const byte OnCurvePoint = 0x01;
    private const byte XShortVector = 0x02;
    private const byte YShortVector = 0x04;
    private const byte RepeatFlag = 0x08;
    private const byte XIsSameOrPositive = 0x10;
    private const byte YIsSameOrPositive = 0x20;
    private const byte OverlapSimple = 0x40;

    // TrueType composite-glyph flags.
    private const ushort Arg1And2AreWords = 0x0001;
    private const ushort WeHaveAScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveAnXAndYScale = 0x0040;
    private const ushort WeHaveATwoByTwo = 0x0080;
    private const ushort WeHaveInstructions = 0x0100;

    /// <summary>Reconstructed tables.</summary>
    public readonly record struct Result(byte[] Glyf, byte[] Loca, int NumGlyphs, int IndexFormat);

    private static readonly Triplet[] s_triplets = BuildTripletTable();

    public static Result Reconstruct(ReadOnlySpan<byte> transformed)
    {
        var header = new Woff2Reader(transformed, "transformed glyf header");
        header.ReadUInt16(); // reserved
        var optionFlags = header.ReadUInt16();
        int numGlyphs = header.ReadUInt16();
        int indexFormat = header.ReadUInt16();

        Span<int> sizes = stackalloc int[7];
        var offset = (long)HeaderSize;
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = header.ReadUInt32();
            if (size > int.MaxValue)
                throw Woff2Decoder.Invalid("transformed glyf substream size is out of range");
            sizes[i] = (int)size;
            offset += size;
        }

        var hasOverlapBitmap = (optionFlags & 1) != 0;
        var overlapBitmapSize = hasOverlapBitmap ? (numGlyphs + 7) >> 3 : 0;
        if (offset + overlapBitmapSize > transformed.Length)
            throw Woff2Decoder.Invalid("transformed glyf substreams extend past the end of the table");

        var start = HeaderSize;
        var nContourStream = new Woff2Reader(Next(transformed, ref start, sizes[0]), "glyf nContour stream");
        var nPointsStream = new Woff2Reader(Next(transformed, ref start, sizes[1]), "glyf nPoints stream");
        var flagStream = new Woff2Reader(Next(transformed, ref start, sizes[2]), "glyf flag stream");
        var glyphStream = new Woff2Reader(Next(transformed, ref start, sizes[3]), "glyf glyph stream");
        var compositeStream = new Woff2Reader(Next(transformed, ref start, sizes[4]), "glyf composite stream");
        var bboxRegion = Next(transformed, ref start, sizes[5]);
        var instructionStream = new Woff2Reader(Next(transformed, ref start, sizes[6]), "glyf instruction stream");
        var overlapBitmap = Next(transformed, ref start, overlapBitmapSize);

        var bboxBitmapSize = ((numGlyphs + 31) >> 5) << 2;
        if (bboxBitmapSize > bboxRegion.Length)
            throw Woff2Decoder.Invalid("glyf bbox bitmap is truncated");
        var bboxBitmap = bboxRegion.Slice(0, bboxBitmapSize);
        var bboxStream = new Woff2Reader(bboxRegion.Slice(bboxBitmapSize), "glyf bbox stream");

        var glyf = new GrowableBuffer(Math.Max(1024, transformed.Length * 2));
        var offsets = new int[numGlyphs + 1];
        var points = new List<Point>();

        for (var glyphId = 0; glyphId < numGlyphs; glyphId++)
        {
            offsets[glyphId] = glyf.Length;
            var contourCount = nContourStream.ReadInt16();
            var hasExplicitBbox = IsBitSet(bboxBitmap, glyphId);

            if (contourCount == 0)
            {
                if (hasExplicitBbox)
                    throw Woff2Decoder.Invalid($"empty glyph {glyphId} has an explicit bounding box");
                continue;
            }

            if (contourCount > 0)
            {
                WriteSimpleGlyph(
                    glyf, contourCount, hasExplicitBbox, hasOverlapBitmap && IsBitSet(overlapBitmap, glyphId),
                    ref nPointsStream, ref flagStream, ref glyphStream, ref bboxStream, ref instructionStream, points);
            }
            else if (contourCount == -1)
            {
                if (!hasExplicitBbox)
                    throw Woff2Decoder.Invalid($"composite glyph {glyphId} has no explicit bounding box");
                WriteCompositeGlyph(glyf, ref compositeStream, ref glyphStream, ref bboxStream, ref instructionStream);
            }
            else
            {
                throw Woff2Decoder.Invalid($"glyph {glyphId} has an invalid contour count {contourCount}");
            }

            glyf.PadTo4();
        }

        offsets[numGlyphs] = glyf.Length;

        // Short loca stores offset/2 in 16 bits.
        var writtenFormat = indexFormat == 0 && glyf.Length <= 0x1FFFE ? 0 : 1;
        var loca = new byte[(numGlyphs + 1) * (writtenFormat == 0 ? 2 : 4)];
        for (var i = 0; i <= numGlyphs; i++)
        {
            if (writtenFormat == 0)
                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(offsets[i] >> 1));
            else
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)offsets[i]);
        }

        return new Result(glyf.ToArray(), loca, numGlyphs, writtenFormat);
    }

    /// <summary>The header's declared index format, read without reconstructing anything.</summary>
    public static int DeclaredIndexFormat(ReadOnlySpan<byte> transformed)
    {
        var header = new Woff2Reader(transformed, "transformed glyf header");
        header.ReadBytes(6);
        return header.ReadUInt16();
    }

    /// <summary>The header's glyph count.</summary>
    public static int DeclaredNumGlyphs(ReadOnlySpan<byte> transformed)
    {
        var header = new Woff2Reader(transformed, "transformed glyf header");
        header.ReadBytes(4);
        return header.ReadUInt16();
    }

    private static void WriteSimpleGlyph(
        GrowableBuffer glyf,
        int contourCount,
        bool hasExplicitBbox,
        bool overlap,
        ref Woff2Reader nPointsStream,
        ref Woff2Reader flagStream,
        ref Woff2Reader glyphStream,
        ref Woff2Reader bboxStream,
        ref Woff2Reader instructionStream,
        List<Point> points)
    {
        Span<ushort> endPoints = contourCount <= 256 ? stackalloc ushort[contourCount] : new ushort[contourCount];
        var pointCount = 0;
        for (var c = 0; c < contourCount; c++)
        {
            pointCount += nPointsStream.Read255UInt16();
            if (pointCount > ushort.MaxValue + 1 || pointCount == 0)
                throw Woff2Decoder.Invalid("simple glyph has an invalid point count");
            endPoints[c] = (ushort)(pointCount - 1);
        }

        var flags = flagStream.ReadBytes(pointCount);
        points.Clear();
        int x = 0, y = 0;
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        Span<byte> data = stackalloc byte[4];
        for (var i = 0; i < pointCount; i++)
        {
            var flag = flags[i];
            var triplet = s_triplets[flag & 0x7F];
            glyphStream.ReadBytes(triplet.DataBytes).CopyTo(data);
            var (dx, dy) = triplet.Decode(data);
            if (dx is < short.MinValue or > short.MaxValue || dy is < short.MinValue or > short.MaxValue)
                throw Woff2Decoder.Invalid("glyph coordinate delta does not fit in 16 bits");

            x += dx;
            y += dy;
            points.Add(new Point(dx, dy, (flag & 0x80) == 0));
            xMin = Math.Min(xMin, x);
            yMin = Math.Min(yMin, y);
            xMax = Math.Max(xMax, x);
            yMax = Math.Max(yMax, y);
        }

        int instructionLength = glyphStream.Read255UInt16();
        var instructions = instructionStream.ReadBytes(instructionLength);

        glyf.WriteInt16((short)contourCount);
        if (hasExplicitBbox)
        {
            for (var i = 0; i < 4; i++)
                glyf.WriteInt16(bboxStream.ReadInt16());
        }
        else
        {
            glyf.WriteInt16(ClampToInt16(xMin));
            glyf.WriteInt16(ClampToInt16(yMin));
            glyf.WriteInt16(ClampToInt16(xMax));
            glyf.WriteInt16(ClampToInt16(yMax));
        }

        foreach (var endPoint in endPoints)
            glyf.WriteUInt16(endPoint);
        glyf.WriteUInt16((ushort)instructionLength);
        glyf.Write(instructions);

        WriteFlags(glyf, points, overlap);
        foreach (var p in points)
            WriteCoordinate(glyf, p.Dx);
        foreach (var p in points)
            WriteCoordinate(glyf, p.Dy);
    }

    private static void WriteCompositeGlyph(
        GrowableBuffer glyf,
        ref Woff2Reader compositeStream,
        ref Woff2Reader glyphStream,
        ref Woff2Reader bboxStream,
        ref Woff2Reader instructionStream)
    {
        glyf.WriteInt16(-1);
        for (var i = 0; i < 4; i++)
            glyf.WriteInt16(bboxStream.ReadInt16());

        var hasInstructions = false;
        ushort flags;
        do
        {
            flags = compositeStream.ReadUInt16();
            hasInstructions |= (flags & WeHaveInstructions) != 0;
            var argumentBytes = 2 + ((flags & Arg1And2AreWords) != 0 ? 4 : 2);
            if ((flags & WeHaveAScale) != 0)
                argumentBytes += 2;
            else if ((flags & WeHaveAnXAndYScale) != 0)
                argumentBytes += 4;
            else if ((flags & WeHaveATwoByTwo) != 0)
                argumentBytes += 8;

            glyf.WriteUInt16(flags);
            glyf.Write(compositeStream.ReadBytes(argumentBytes));
        }
        while ((flags & MoreComponents) != 0);

        if (hasInstructions)
        {
            int instructionLength = glyphStream.Read255UInt16();
            glyf.WriteUInt16((ushort)instructionLength);
            glyf.Write(instructionStream.ReadBytes(instructionLength));
        }
    }

    private static void WriteFlags(GrowableBuffer glyf, List<Point> points, bool overlap)
    {
        var previous = -1;
        var repeat = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var flag = p.OnCurve ? OnCurvePoint : (byte)0;
            if (i == 0 && overlap)
                flag |= OverlapSimple;

            if (p.Dx == 0)
                flag |= XIsSameOrPositive;
            else if (Math.Abs(p.Dx) < 256)
                flag |= (byte)(XShortVector | (p.Dx > 0 ? XIsSameOrPositive : 0));

            if (p.Dy == 0)
                flag |= YIsSameOrPositive;
            else if (Math.Abs(p.Dy) < 256)
                flag |= (byte)(YShortVector | (p.Dy > 0 ? YIsSameOrPositive : 0));

            if (flag == previous && repeat < 255)
            {
                if (repeat == 0)
                    glyf.SetLastByte((byte)(flag | RepeatFlag));
                repeat++;
                continue;
            }

            if (repeat > 0)
            {
                glyf.WriteByte((byte)repeat);
                repeat = 0;
            }

            glyf.WriteByte(flag);
            previous = flag;
        }

        if (repeat > 0)
            glyf.WriteByte((byte)repeat);
    }

    private static void WriteCoordinate(GrowableBuffer glyf, int delta)
    {
        if (delta == 0)
            return;
        if (Math.Abs(delta) < 256)
            glyf.WriteByte((byte)Math.Abs(delta));
        else
            glyf.WriteInt16((short)delta);
    }

    private static ReadOnlySpan<byte> Next(ReadOnlySpan<byte> data, ref int start, int length)
    {
        var slice = data.Slice(start, length);
        start += length;
        return slice;
    }

    /// <summary>Bit for <paramref name="index"/>, most significant bit of byte 0 first.</summary>
    private static bool IsBitSet(ReadOnlySpan<byte> bitmap, int index) =>
        (bitmap[index >> 3] & (0x80 >> (index & 7))) != 0;

    private static short ClampToInt16(int value) =>
        (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private readonly record struct Point(int Dx, int Dy, bool OnCurve);

    /// <summary>
    /// One row of the WOFF2 triplet encoding table (section 5.2): how many data bytes follow
    /// the flag, how they split into X and Y magnitudes, the base added to each, and the signs.
    /// </summary>
    private readonly record struct Triplet(int DataBytes, int XBits, int YBits, int DeltaX, int DeltaY, bool XPositive, bool YPositive)
    {
        public (int Dx, int Dy) Decode(ReadOnlySpan<byte> b)
        {
            int x, y;
            switch (XBits, YBits)
            {
                case (0, 8): x = 0; y = b[0]; break;
                case (8, 0): x = b[0]; y = 0; break;
                case (4, 4): x = b[0] >> 4; y = b[0] & 0x0F; break;
                case (8, 8): x = b[0]; y = b[1]; break;
                case (12, 12): x = (b[0] << 4) | (b[1] >> 4); y = ((b[1] & 0x0F) << 8) | b[2]; break;
                default: x = (b[0] << 8) | b[1]; y = (b[2] << 8) | b[3]; break;
            }

            var dx = XBits == 0 ? 0 : (XPositive ? 1 : -1) * (x + DeltaX);
            var dy = YBits == 0 ? 0 : (YPositive ? 1 : -1) * (y + DeltaY);
            return (dx, dy);
        }
    }

    /// <summary>Generates the 128-row triplet table from the spec's regular structure.</summary>
    private static Triplet[] BuildTripletTable()
    {
        var table = new Triplet[128];
        for (var i = 0; i < 128; i++)
        {
            if (i < 10)
            {
                // dX = 0, 8-bit dY with base 0/256/512/768/1024, sign alternating -/+.
                table[i] = new Triplet(1, 0, 8, 0, (i >> 1) << 8, false, (i & 1) != 0);
            }
            else if (i < 20)
            {
                var j = i - 10;
                table[i] = new Triplet(1, 8, 0, (j >> 1) << 8, 0, (j & 1) != 0, false);
            }
            else if (i < 84)
            {
                // Four groups of 16: dX base 1/17/33/49; blocks of 4 within: dY base 1/17/33/49.
                var j = i - 20;
                table[i] = new Triplet(1, 4, 4, 1 + 16 * (j >> 4), 1 + 16 * ((j >> 2) & 3), (j & 1) != 0, (j & 2) != 0);
            }
            else if (i < 120)
            {
                // Three groups of 12: dX base 1/257/513; blocks of 4 within: dY base 1/257/513.
                var j = i - 84;
                table[i] = new Triplet(2, 8, 8, 1 + 256 * (j / 12), 1 + 256 * ((j % 12) >> 2), (j & 1) != 0, (j & 2) != 0);
            }
            else if (i < 124)
            {
                var j = i - 120;
                table[i] = new Triplet(3, 12, 12, 0, 0, (j & 1) != 0, (j & 2) != 0);
            }
            else
            {
                var j = i - 124;
                table[i] = new Triplet(4, 16, 16, 0, 0, (j & 1) != 0, (j & 2) != 0);
            }
        }

        return table;
    }
}
