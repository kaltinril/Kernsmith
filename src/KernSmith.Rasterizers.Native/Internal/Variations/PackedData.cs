namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The run-length encodings of the tuple variation store ("Packed point numbers" and "Packed
/// deltas" in the OpenType Font Variations Common Table Formats chapter).
/// </summary>
internal static class PackedData
{
    private const byte PointsAreWords = 0x80;
    private const byte PointRunCountMask = 0x7F;

    private const byte DeltasAreZero = 0x80;
    private const byte DeltasAreWords = 0x40;
    private const byte DeltaRunCountMask = 0x3F;

    /// <summary>
    /// Reads packed point numbers.
    /// </summary>
    /// <returns>The point numbers in stored order (ascending; repeats allowed), or null for the
    /// special count of zero, which means "every point, phantom points included".</returns>
    /// <exception cref="FontFormatException">If the data is truncated.</exception>
    public static ushort[]? ReadPointNumbers(ref FontReader reader)
    {
        int count = reader.ReadUInt8();
        if (count == 0)
            return null;

        if ((count & 0x80) != 0)
            count = ((count & 0x7F) << 8) | reader.ReadUInt8();

        var points = new ushort[count];
        int point = 0;
        int read = 0;
        while (read < count)
        {
            byte control = reader.ReadUInt8();
            int run = (control & PointRunCountMask) + 1;
            if (read + run > count)
                throw new FontFormatException("gvar", reader.Position,
                    $"a point-number run of {run} overruns the declared count of {count}.");

            bool words = (control & PointsAreWords) != 0;
            for (int i = 0; i < run; i++)
            {
                // Each number is a difference from the previous one, across runs too.
                point += words ? reader.ReadUInt16() : reader.ReadUInt8();
                points[read++] = (ushort)point;
            }
        }

        return points;
    }

    /// <summary>Reads exactly <paramref name="destination"/>.Length packed deltas.</summary>
    /// <exception cref="FontFormatException">If the data is truncated or a run overruns the
    /// logical count.</exception>
    public static void ReadDeltas(ref FontReader reader, Span<int> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            byte control = reader.ReadUInt8();
            int run = (control & DeltaRunCountMask) + 1;
            if (read + run > destination.Length)
                throw new FontFormatException("gvar", reader.Position,
                    $"a delta run of {run} overruns the {destination.Length} deltas expected.");

            if ((control & DeltasAreZero) != 0)
            {
                destination.Slice(read, run).Clear();
            }
            else if ((control & DeltasAreWords) != 0)
            {
                for (int i = 0; i < run; i++)
                    destination[read + i] = reader.ReadInt16();
            }
            else
            {
                for (int i = 0; i < run; i++)
                    destination[read + i] = reader.ReadInt8();
            }

            read += run;
        }
    }
}
