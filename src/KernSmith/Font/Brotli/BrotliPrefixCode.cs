using System.Runtime.CompilerServices;

namespace KernSmith.Font.Brotli;

/// <summary>
/// A canonical prefix code (RFC 7932 sections 3.2-3.5) decoded through a two-level lookup table.
/// </summary>
/// <remarks>
/// The root table is indexed by the next 8 stream bits. Codes of 8 bits or fewer resolve there;
/// longer codes (up to 15 bits) point the root entry at a second-level table indexed by the bits
/// after the first 8. Entries pack <c>(bitLength &lt;&lt; 16) | value</c>; in the root table a
/// bit length above 8 marks a second-level pointer whose value is the sub-table offset.
/// Tables are only built from code lengths already proven complete (Kraft sum exact) or from
/// the fixed simple-code shapes, so every table slot is filled.
/// </remarks>
internal sealed class BrotliPrefixCode
{
    /// <summary>Bits indexing the root table.</summary>
    public const int RootBits = 8;
    private const int RootSize = 1 << RootBits;

    /// <summary>The longest code, in bits: one peek of this many covers any symbol.</summary>
    public const int MaxCodeLength = 15;

    /// <summary>Order in which code-length code lengths are stored (RFC 7932 section 3.5).</summary>
    private static ReadOnlySpan<byte> CodeLengthOrder => [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>
    /// The fixed code for code-length code lengths, indexed by the next 4 stream bits:
    /// <c>(bitsUsed &lt;&lt; 4) | value</c>. Codes as they appear in the stream (read right to
    /// left): 0=00, 1=0111, 2=011, 3=10, 4=01, 5=1111.
    /// </summary>
    private static ReadOnlySpan<byte> CodeLengthCodeLengthTable =>
    [
        0x20, 0x24, 0x23, 0x32, 0x20, 0x24, 0x23, 0x41,
        0x20, 0x24, 0x23, 0x32, 0x20, 0x24, 0x23, 0x45,
    ];

    private readonly int[] _table;

    private BrotliPrefixCode(int[] table)
    {
        _table = table;
    }

    /// <summary>
    /// The lookup table, for decoders that walk it themselves
    /// (<see cref="BrotliBitReader.DecodeLiterals"/>): see the class remarks for its layout.
    /// </summary>
    public int[] Table => _table;

    /// <summary>Decodes one symbol.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ref BrotliBitReader reader) => Decode(ref reader, _table);

    /// <summary>Decodes one symbol through <paramref name="table"/>, a <see cref="Table"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Decode(ref BrotliBitReader reader, int[] table)
    {
        // One peek covers the longest code, so both levels consume without refilling.
        var bits = reader.Peek(MaxCodeLength);
        var entry = table[bits & (RootSize - 1)];
        var length = entry >> 16;
        if (length > RootBits)
            return DecodeSecondLevel(ref reader, table, bits, entry);

        reader.SkipPeeked(length);
        return entry & 0xFFFF;
    }

    private static int DecodeSecondLevel(ref BrotliBitReader reader, int[] table, uint bits, int pointer)
    {
        var subIndex = (int)(bits >> RootBits) & ((1 << ((pointer >> 16) - RootBits)) - 1);
        var entry = table[(pointer & 0xFFFF) + subIndex];
        reader.SkipPeeked(RootBits + (entry >> 16));
        return entry & 0xFFFF;
    }

    /// <summary>Reads a simple or complex prefix code over an alphabet of <paramref name="alphabetSize"/> symbols.</summary>
    public static BrotliPrefixCode Read(ref BrotliBitReader reader, int alphabetSize)
    {
        Span<byte> lengths = stackalloc byte[alphabetSize];
        lengths.Clear();

        var kind = (int)reader.Read(2);
        if (kind == 1)
            ReadSimpleLengths(ref reader, alphabetSize, lengths);
        else
            ReadComplexLengths(ref reader, kind, lengths);

        return FromLengths(lengths);
    }

    private static void ReadSimpleLengths(ref BrotliBitReader reader, int alphabetSize, scoped Span<byte> lengths)
    {
        var alphabetBits = 0;
        while ((1 << alphabetBits) < alphabetSize)
            alphabetBits++;

        var count = (int)reader.Read(2) + 1;
        Span<int> symbols = stackalloc int[4];
        for (var i = 0; i < count; i++)
        {
            var symbol = (int)reader.Read(alphabetBits);
            if (symbol >= alphabetSize)
                throw ManagedBrotliDecoder.Invalid($"simple prefix code symbol {symbol} is outside the alphabet of {alphabetSize}");
            for (var j = 0; j < i; j++)
            {
                if (symbols[j] == symbol)
                    throw ManagedBrotliDecoder.Invalid("simple prefix code repeats a symbol");
            }

            symbols[i] = symbol;
        }

        switch (count)
        {
            case 1:
                // A single symbol is coded with zero bits; FromLengths recognises the lone length.
                lengths[symbols[0]] = 1;
                break;
            case 2:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 1;
                break;
            case 3:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 2;
                lengths[symbols[2]] = 2;
                break;
            default:
                if (reader.Read(1) == 0)
                {
                    lengths[symbols[0]] = 2;
                    lengths[symbols[1]] = 2;
                    lengths[symbols[2]] = 2;
                    lengths[symbols[3]] = 2;
                }
                else
                {
                    lengths[symbols[0]] = 1;
                    lengths[symbols[1]] = 2;
                    lengths[symbols[2]] = 3;
                    lengths[symbols[3]] = 3;
                }

                break;
        }
    }

    private static void ReadComplexLengths(ref BrotliBitReader reader, int skip, scoped Span<byte> lengths)
    {
        // Code lengths of the code-length alphabet (0..17), each 0..5.
        Span<byte> codeLengthLengths = stackalloc byte[18];
        codeLengthLengths.Clear();
        var space = 32;
        var nonZero = 0;
        for (var i = skip; i < CodeLengthOrder.Length; i++)
        {
            var entry = CodeLengthCodeLengthTable[(int)reader.Peek(4)];
            reader.Skip(entry >> 4);
            var length = entry & 0x0F;
            codeLengthLengths[CodeLengthOrder[i]] = (byte)length;
            if (length != 0)
            {
                space -= 32 >> length;
                nonZero++;
                if (space <= 0)
                    break;
            }
        }

        if (nonZero != 1 && space != 0)
            throw ManagedBrotliDecoder.Invalid("code-length code lengths do not form a complete prefix code");

        var codeLengthCode = FromLengths(codeLengthLengths);

        // Symbol code lengths, with run-length codes 16 (repeat previous non-zero) and 17 (zeros).
        const int TotalSpace = 1 << MaxCodeLength;
        var previousNonZero = 8;
        var repeat = 0;
        var repeatLength = 0;
        var symbol = 0;
        space = TotalSpace;
        while (symbol < lengths.Length && space > 0)
        {
            var code = codeLengthCode.Decode(ref reader);
            if (code < 16)
            {
                repeat = 0;
                lengths[symbol++] = (byte)code;
                if (code != 0)
                {
                    previousNonZero = code;
                    space -= TotalSpace >> code;
                }

                continue;
            }

            var extraBits = code == 16 ? 2 : 3;
            var newLength = code == 16 ? previousNonZero : 0;
            if (repeatLength != newLength)
            {
                repeat = 0;
                repeatLength = newLength;
            }

            var oldRepeat = repeat;
            if (repeat > 0)
                repeat = (repeat - 2) << extraBits;
            repeat += (int)reader.Read(extraBits) + 3;

            var delta = repeat - oldRepeat;
            if (delta > lengths.Length - symbol)
                throw ManagedBrotliDecoder.Invalid("code length repeat runs past the end of the alphabet");

            lengths.Slice(symbol, delta).Fill((byte)repeatLength);
            symbol += delta;
            if (repeatLength != 0)
                space -= delta * (TotalSpace >> repeatLength);
        }

        if (space != 0)
            throw ManagedBrotliDecoder.Invalid("symbol code lengths do not form a complete prefix code");
    }

    /// <summary>
    /// Builds the lookup table from canonical code lengths. A single non-zero length means a
    /// one-symbol code that consumes no bits.
    /// </summary>
    private static BrotliPrefixCode FromLengths(ReadOnlySpan<byte> lengths)
    {
        var nonZero = 0;
        var lastSymbol = 0;
        Span<int> countPerLength = stackalloc int[MaxCodeLength + 1];
        countPerLength.Clear();
        for (var s = 0; s < lengths.Length; s++)
        {
            if (lengths[s] == 0)
                continue;
            countPerLength[lengths[s]]++;
            nonZero++;
            lastSymbol = s;
        }

        if (nonZero == 1)
        {
            var single = new int[RootSize];
            Array.Fill(single, lastSymbol);
            return new BrotliPrefixCode(single);
        }

        Span<int> nextCode = stackalloc int[MaxCodeLength + 1];
        var code = 0;
        for (var bits = 1; bits <= MaxCodeLength; bits++)
        {
            code = (code + countPerLength[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Reversed codes (the stream delivers a code's first bit as the least significant bit).
        Span<int> reversed = stackalloc int[lengths.Length];
        Span<int> rootMaxLength = stackalloc int[RootSize];
        rootMaxLength.Clear();
        for (var s = 0; s < lengths.Length; s++)
        {
            int length = lengths[s];
            if (length == 0)
                continue;
            var r = Reverse(nextCode[length]++, length);
            reversed[s] = r;
            if (length > RootBits)
            {
                var root = r & (RootSize - 1);
                rootMaxLength[root] = Math.Max(rootMaxLength[root], length);
            }
        }

        var size = RootSize;
        for (var i = 0; i < RootSize; i++)
        {
            if (rootMaxLength[i] != 0)
                size += 1 << (rootMaxLength[i] - RootBits);
        }

        var table = new int[size];
        var offset = RootSize;
        for (var i = 0; i < RootSize; i++)
        {
            if (rootMaxLength[i] == 0)
                continue;
            table[i] = (rootMaxLength[i] << 16) | offset;
            offset += 1 << (rootMaxLength[i] - RootBits);
        }

        for (var s = 0; s < lengths.Length; s++)
        {
            int length = lengths[s];
            if (length == 0)
                continue;

            var r = reversed[s];
            if (length <= RootBits)
            {
                for (var i = r; i < RootSize; i += 1 << length)
                    table[i] = (length << 16) | s;
            }
            else
            {
                var pointer = table[r & (RootSize - 1)];
                var subBase = pointer & 0xFFFF;
                var subSize = 1 << ((pointer >> 16) - RootBits);
                var subLength = length - RootBits;
                for (var i = r >> RootBits; i < subSize; i += 1 << subLength)
                    table[subBase + i] = (subLength << 16) | s;
            }
        }

        return new BrotliPrefixCode(table);
    }

    /// <summary>The low <paramref name="length"/> bits of <paramref name="code"/> (at most 15), reversed.</summary>
    private static int Reverse(int code, int length) =>
        ((ReversedBytes[code & 0xFF] << 8) | ReversedBytes[(code >> 8) & 0xFF]) >> (16 - length);

    /// <summary>Each byte with its bit order reversed.</summary>
    private static readonly byte[] ReversedBytes = BuildReversedBytes();

    private static byte[] BuildReversedBytes()
    {
        var table = new byte[256];
        for (var value = 0; value < 256; value++)
        {
            var reversed = 0;
            for (var bit = 0; bit < 8; bit++)
                reversed |= ((value >> bit) & 1) << (7 - bit);
            table[value] = (byte)reversed;
        }

        return table;
    }
}
