namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The <c>gvar</c> (glyph variations) table: per-glyph tuple variation stores giving point
/// deltas for regions of the variation space.
/// </summary>
/// <remarks>
/// Glyph data is decoded on demand for one glyph at one instance; nothing is cached. Every
/// glyph's point numbering ends with four phantom points (left side bearing, advance, top side
/// bearing, advance height); for a composite the numbers before them address components.
/// </remarks>
internal sealed class GvarTable
{
    private const int PhantomPointCount = 4;

    // GlyphVariationData.tupleVariationCount
    private const ushort SharedPointNumbers = 0x8000;
    private const ushort CountMask = 0x0FFF;

    // TupleVariationHeader.tupleIndex
    private const ushort EmbeddedPeakTuple = 0x8000;
    private const ushort IntermediateRegion = 0x4000;
    private const ushort PrivatePointNumbers = 0x2000;
    private const ushort TupleIndexMask = 0x0FFF;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly float[] _sharedTuples;
    private readonly int _sharedTupleCount;
    private readonly uint[] _glyphOffsets;

    private GvarTable(ReadOnlyMemory<byte> data, int axisCount, float[] sharedTuples, int sharedTupleCount, uint[] glyphOffsets)
    {
        _data = data;
        AxisCount = axisCount;
        _sharedTuples = sharedTuples;
        _sharedTupleCount = sharedTupleCount;
        _glyphOffsets = glyphOffsets;
    }

    /// <summary>Number of variation axes each tuple spans; equals the <c>fvar</c> axis count.</summary>
    public int AxisCount { get; }

    /// <summary>Parses the header, shared tuples and per-glyph offsets.</summary>
    /// <exception cref="FontFormatException">If the header or its arrays are truncated.</exception>
    public static GvarTable Parse(ReadOnlyMemory<byte> data)
    {
        var reader = new FontReader(data.Span);
        reader.Skip(4);                         // majorVersion, minorVersion
        int axisCount = reader.ReadUInt16();
        int sharedTupleCount = reader.ReadUInt16();
        uint sharedTuplesOffset = reader.ReadUInt32();
        int glyphCount = reader.ReadUInt16();
        bool longOffsets = (reader.ReadUInt16() & 1) != 0;
        uint dataArrayOffset = reader.ReadUInt32();

        var offsets = new uint[glyphCount + 1];
        for (int i = 0; i <= glyphCount; i++)
        {
            uint offset = longOffsets ? reader.ReadUInt32() : reader.ReadUInt16() * 2u;
            offsets[i] = dataArrayOffset + offset;
        }

        reader.Seek(checked((int)sharedTuplesOffset));
        var shared = new float[sharedTupleCount * axisCount];
        for (int i = 0; i < shared.Length; i++)
            shared[i] = reader.ReadF2Dot14();

        return new GvarTable(data, axisCount, shared, sharedTupleCount, offsets);
    }

    /// <summary>
    /// Computes the net deltas for one glyph at <paramref name="coordinates"/>: every applicable
    /// tuple's deltas — explicit, or inferred for unreferenced points of a simple glyph — scaled
    /// by the tuple's region scalar and summed.
    /// </summary>
    /// <param name="glyphIndex">The glyph.</param>
    /// <param name="coordinates">Normalized instance coordinates, one per axis.</param>
    /// <param name="pointCount">Outline points (simple glyph) or components (composite), not
    /// counting the four phantom points.</param>
    /// <param name="xs">Default X of each outline point, for inference; empty for a composite.</param>
    /// <param name="ys">Default Y of each outline point, for inference; empty for a composite.</param>
    /// <param name="contourEnds">Last point of each contour; empty for a composite, whose
    /// unreferenced components are never inferred.</param>
    /// <param name="dx">Net X deltas, <paramref name="pointCount"/> + 4 long.</param>
    /// <param name="dy">Net Y deltas, <paramref name="pointCount"/> + 4 long.</param>
    /// <returns>False when the glyph has no data or no tuple applies at this instance.</returns>
    /// <exception cref="FontFormatException">If the glyph's variation data is malformed.</exception>
    public bool TryGetDeltas(
        int glyphIndex,
        ReadOnlySpan<float> coordinates,
        int pointCount,
        ReadOnlySpan<int> xs,
        ReadOnlySpan<int> ys,
        ReadOnlySpan<int> contourEnds,
        out float[] dx,
        out float[] dy)
    {
        dx = dy = [];
        if ((uint)glyphIndex >= (uint)(_glyphOffsets.Length - 1))
            return false;

        uint begin = _glyphOffsets[glyphIndex];
        uint finish = _glyphOffsets[glyphIndex + 1];
        if (finish <= begin)
            return false;
        if (finish > _data.Length)
            throw new FontFormatException("gvar", (int)begin,
                $"glyph {glyphIndex}'s variation data runs past the end of the table.");

        var glyphData = _data.Span.Slice((int)begin, (int)(finish - begin));
        var header = new FontReader(glyphData);
        ushort tupleVariationCount = header.ReadUInt16();
        int dataOffset = header.ReadUInt16();
        int tupleCount = tupleVariationCount & CountMask;

        var serialized = new FontReader(glyphData);
        serialized.Seek(dataOffset);
        ushort[]? sharedPoints = (tupleVariationCount & SharedPointNumbers) != 0
            ? PackedData.ReadPointNumbers(ref serialized)
            : null;

        int totalPoints = pointCount + PhantomPointCount;
        float[]? netX = null;
        float[]? netY = null;
        var peak = new float[AxisCount];
        var start = new float[AxisCount];
        var end = new float[AxisCount];

        for (int t = 0; t < tupleCount; t++)
        {
            int dataSize = header.ReadUInt16();
            ushort tupleIndex = header.ReadUInt16();

            if ((tupleIndex & EmbeddedPeakTuple) != 0)
            {
                ReadTuple(ref header, peak);
            }
            else
            {
                int shared = tupleIndex & TupleIndexMask;
                if (shared >= _sharedTupleCount)
                    throw new FontFormatException("gvar", header.Position,
                        $"glyph {glyphIndex} references shared tuple {shared} of {_sharedTupleCount}.");
                _sharedTuples.AsSpan(shared * AxisCount, AxisCount).CopyTo(peak);
            }

            if ((tupleIndex & IntermediateRegion) != 0)
            {
                ReadTuple(ref header, start);
                ReadTuple(ref header, end);
            }
            else
            {
                for (int axis = 0; axis < AxisCount; axis++)
                {
                    start[axis] = Math.Min(peak[axis], 0f);
                    end[axis] = Math.Max(peak[axis], 0f);
                }
            }

            int tupleStart = serialized.Position;
            float scalar = RegionScalar.ForRegion(start, peak, end, coordinates);
            if (scalar.Equals(0f))
            {
                serialized.Skip(dataSize);
                continue;
            }

            ushort[]? points = (tupleIndex & PrivatePointNumbers) != 0
                ? PackedData.ReadPointNumbers(ref serialized)
                : sharedPoints;

            int deltaCount = points?.Length ?? totalPoints;
            var rawX = new int[deltaCount];
            var rawY = new int[deltaCount];
            PackedData.ReadDeltas(ref serialized, rawX);
            PackedData.ReadDeltas(ref serialized, rawY);

            // The header's size is authoritative: it is where the next tuple's data starts.
            serialized.Seek(tupleStart);
            serialized.Skip(dataSize);

            netX ??= new float[totalPoints];
            netY ??= new float[totalPoints];
            Accumulate(points, rawX, rawY, scalar, totalPoints, pointCount, xs, ys, contourEnds, netX, netY);
        }

        if (netX is null || netY is null)
            return false;

        dx = netX;
        dy = netY;
        return true;
    }

    private static void ReadTuple(ref FontReader reader, Span<float> tuple)
    {
        for (int axis = 0; axis < tuple.Length; axis++)
            tuple[axis] = reader.ReadF2Dot14();
    }

    /// <summary>Adds one tuple's scaled deltas, inferring unreferenced outline points first.</summary>
    private static void Accumulate(
        ushort[]? points,
        int[] rawX,
        int[] rawY,
        float scalar,
        int totalPoints,
        int pointCount,
        ReadOnlySpan<int> xs,
        ReadOnlySpan<int> ys,
        ReadOnlySpan<int> contourEnds,
        float[] netX,
        float[] netY)
    {
        if (points is null)
        {
            for (int i = 0; i < totalPoints; i++)
            {
                netX[i] += rawX[i] * scalar;
                netY[i] += rawY[i] * scalar;
            }

            return;
        }

        var tupleX = new float[totalPoints];
        var tupleY = new float[totalPoints];
        var touched = new bool[totalPoints];
        for (int i = 0; i < points.Length; i++)
        {
            int point = points[i];
            if (point >= totalPoints)
                continue;   // a number past the glyph is ignored rather than failing the glyph

            // A repeated point number carries several deltas, all of which apply.
            tupleX[point] += rawX[i];
            tupleY[point] += rawY[i];
            touched[point] = true;
        }

        if (contourEnds.Length > 0 && xs.Length >= pointCount && ys.Length >= pointCount)
        {
            int contourStart = 0;
            foreach (int contourEnd in contourEnds)
            {
                InferredDeltas.InferContour(xs, tupleX, touched, contourStart, contourEnd);
                InferredDeltas.InferContour(ys, tupleY, touched, contourStart, contourEnd);
                contourStart = contourEnd + 1;
            }
        }

        for (int i = 0; i < totalPoints; i++)
        {
            netX[i] += tupleX[i] * scalar;
            netY[i] += tupleY[i] * scalar;
        }
    }
}
