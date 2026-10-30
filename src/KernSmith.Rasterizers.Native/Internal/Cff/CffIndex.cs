namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>
/// A CFF INDEX: a counted array of variable-length byte objects (Adobe TN#5176 §5; CFF2 widens
/// the count to 32 bits). Every offset is validated when the INDEX is parsed, so
/// <see cref="this[int]"/> can never slice outside the table.
/// </summary>
internal sealed class CffIndex
{
    /// <summary>An INDEX with no objects.</summary>
    public static readonly CffIndex Empty = new(ReadOnlyMemory<byte>.Empty, [0]);

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int[] _offsets;

    private CffIndex(ReadOnlyMemory<byte> data, int[] offsets)
    {
        _data = data;
        _offsets = offsets;
    }

    /// <summary>The number of objects.</summary>
    public int Count => _offsets.Length - 1;

    /// <summary>
    /// The subroutine number bias for an INDEX of this size (Type 2 charstrings): a
    /// <c>callsubr</c>/<c>callgsubr</c> operand is added to it to give the object index.
    /// </summary>
    public int Bias => SubroutineBias(Count);

    /// <summary>The bytes of object <paramref name="index"/>.</summary>
    /// <exception cref="FontFormatException">If the index is outside the INDEX.</exception>
    public ReadOnlyMemory<byte> this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new FontFormatException($"CFF INDEX entry {index} is out of range (count {Count}).");

            return _data.Slice(_offsets[index], _offsets[index + 1] - _offsets[index]);
        }
    }

    /// <summary>
    /// The Type 2 subroutine bias: 107 below 1240 subroutines, 1131 below 33900, else 32768.
    /// </summary>
    public static int SubroutineBias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    /// <summary>Parses the INDEX starting at <paramref name="offset"/> in <paramref name="table"/>.</summary>
    /// <param name="table">The whole <c>CFF </c> or <c>CFF2</c> table.</param>
    /// <param name="offset">Where the INDEX starts, from the start of the table.</param>
    /// <param name="cff2">True for a CFF2 INDEX (32-bit count), false for CFF (16-bit).</param>
    /// <param name="end">Receives the offset of the first byte after the INDEX.</param>
    /// <exception cref="FontFormatException">If the INDEX is truncated or its offsets are inconsistent.</exception>
    public static CffIndex Parse(ReadOnlyMemory<byte> table, int offset, bool cff2, out int end)
    {
        if (offset < 0 || offset > table.Length)
            throw new FontFormatException($"CFF INDEX offset {offset} is outside the table (length {table.Length}).");

        var reader = new FontReader(table.Span);
        reader.Seek(offset);

        long count = cff2 ? reader.ReadUInt32() : reader.ReadUInt16();
        if (count == 0)
        {
            end = reader.Position;
            return Empty;
        }

        int offSize = reader.ReadUInt8();
        if (offSize is < 1 or > 4)
            throw new FontFormatException($"CFF INDEX at {offset} has an invalid offSize {offSize}.");

        // Checked against the bytes actually present before anything is allocated, so a hostile
        // count cannot request a huge offset array.
        long offsetBytes = (count + 1) * offSize;
        if (offsetBytes > reader.Remaining)
            throw new FontFormatException(
                $"CFF INDEX at {offset} declares {count} objects but the table ends {reader.Remaining} bytes later.");

        var offsets = new int[count + 1];
        int dataStart = reader.Position + (int)offsetBytes;
        long available = table.Length - dataStart;

        for (long i = 0; i <= count; i++)
        {
            uint value = ReadOffset(ref reader, offSize);
            if (value == 0 || value - 1 > available)
                throw new FontFormatException($"CFF INDEX at {offset} has offset {value} outside its data.");

            offsets[i] = (int)(value - 1);
            if (i > 0 && offsets[i] < offsets[i - 1])
                throw new FontFormatException($"CFF INDEX at {offset} has decreasing offsets.");
        }

        if (offsets[0] != 0)
            throw new FontFormatException($"CFF INDEX at {offset} does not start its data at offset 1.");

        end = dataStart + offsets[count];
        return new CffIndex(table.Slice(dataStart, offsets[count]), offsets);
    }

    private static uint ReadOffset(ref FontReader reader, int offSize)
    {
        uint value = 0;
        for (int i = 0; i < offSize; i++)
            value = (value << 8) | reader.ReadUInt8();

        return value;
    }
}
