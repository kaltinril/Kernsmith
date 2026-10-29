using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Tests.Fuzzing;

/// <summary>How a mutator damages one region of a font.</summary>
internal enum MutationKind
{
    /// <summary>Shortens the region (its declared length, or the data itself).</summary>
    Truncate,

    /// <summary>Flips one to eight random bits.</summary>
    BitFlips,

    /// <summary>Overwrites an aligned 16- or 32-bit field with an extreme or out-of-range offset.</summary>
    OffsetCorruption,

    /// <summary>Inflates a 16-bit field (a count, typically) to a much larger value.</summary>
    CountInflation,

    /// <summary>Overwrites a run of 4-32 bytes with random data.</summary>
    ByteRun,
}

/// <summary>
/// The in-place damage the mutators share. Each takes the region being damaged and a position
/// picker (so callers can weight headers and hot ranges), and returns a description that,
/// with the case's seed, reproduces the mutation.
/// </summary>
internal static class ByteDamage
{
    /// <summary>Every in-place kind (everything but <see cref="MutationKind.Truncate"/>, which callers own).</summary>
    public static string Apply(byte[] data, MutationKind kind, int regionStart, int regionLength, Random rng, Func<int, int> pick)
    {
        switch (kind)
        {
            case MutationKind.BitFlips:
            {
                int flips = rng.Next(1, 9);
                var positions = new List<int>(flips);
                for (int i = 0; i < flips; i++)
                {
                    int pos = pick(1);
                    data[pos] ^= (byte)(1 << rng.Next(8));
                    positions.Add(pos);
                }

                return $"bit flips at {string.Join(',', positions)}";
            }

            case MutationKind.OffsetCorruption:
            {
                bool wide = rng.Next(2) == 0 && regionLength >= 4 && data.Length >= 4;
                if (wide)
                {
                    int pos = Math.Min(pick(4) & ~1, data.Length - 4);
                    uint[] values = [0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, (uint)regionLength,
                        (uint)regionLength - 1, (uint)regionLength + 1, (uint)rng.Next(), (uint)data.Length];
                    uint value = values[rng.Next(values.Length)];
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), value);
                    return $"u32 at {pos} = 0x{value:X8}";
                }
                else
                {
                    int pos = Math.Min(pick(2) & ~1, data.Length - 2);
                    ushort[] values = [0, 1, 0x7FFF, 0x8000, 0xFFFF, (ushort)regionLength,
                        (ushort)(regionLength - 1), (ushort)(regionLength + 1), (ushort)rng.Next(0x10000)];
                    ushort value = values[rng.Next(values.Length)];
                    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), value);
                    return $"u16 at {pos} = 0x{value:X4}";
                }
            }

            case MutationKind.CountInflation:
            {
                int pos = Math.Min(pick(2) & ~1, data.Length - 2);
                ushort old = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
                ushort[] values = [(ushort)Math.Min(0xFFFF, (old * 2) + 1), (ushort)Math.Min(0xFFFF, old + 1),
                    (ushort)Math.Min(0xFFFF, old + 16), 0x7FFF, 0xFFFF];
                ushort value = values[rng.Next(values.Length)];
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), value);
                return $"count at {pos}: {old} -> {value}";
            }

            case MutationKind.ByteRun:
            {
                int run = rng.Next(4, 33);
                int pos = pick(1);
                int end = Math.Min(data.Length, Math.Min(pos + run, regionStart + regionLength));
                for (int i = pos; i < end; i++)
                    data[i] = (byte)rng.Next(256);
                return $"random bytes [{pos}, {end})";
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Truncation is region-specific.");
        }
    }

    /// <summary>
    /// A position in a region: a quarter of picks go to <paramref name="hot"/> ranges when there
    /// are any, a quarter to the region's first 64 bytes (headers: counts, offsets, formats), the
    /// rest anywhere.
    /// </summary>
    public static int Pick(int regionStart, int regionLength, int width, Random rng, IReadOnlyList<(int Offset, int Length)>? hot)
    {
        int lastStart = Math.Max(0, regionLength - width);
        int choice = rng.Next(4);

        if (choice == 0 && hot is { Count: > 0 })
        {
            var (offset, length) = hot[rng.Next(hot.Count)];
            // Glyph headers carry the counts and flags, so their first bytes get extra weight.
            int within = rng.Next(2) == 0 ? rng.Next(Math.Min(length, 16)) : rng.Next(length);
            return Math.Min(offset + within, regionStart + lastStart);
        }

        if (choice <= 1)
            return regionStart + rng.Next(Math.Min(lastStart + 1, 64));

        return regionStart + rng.Next(lastStart + 1);
    }
}
