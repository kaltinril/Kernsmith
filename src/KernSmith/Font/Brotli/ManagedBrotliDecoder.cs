using System.Runtime.CompilerServices;

namespace KernSmith.Font.Brotli;

/// <summary>
/// Pure managed Brotli decoder (RFC 7932), written from the RFC for WOFF2.
/// </summary>
/// <remarks>
/// <see cref="System.IO.Compression.BrotliDecoder"/> is deliberately not used: it wraps the
/// runtime's native Brotli library, which browser-wasm does not ship (see
/// <c>project_decisions.md</c>, Native Rasterizer). The whole output is kept in one buffer,
/// so the sliding window is simply the tail of that buffer. Every malformed-input path throws
/// <see cref="FontParsingException"/>; every command must consume input or produce output (one
/// that does neither is rejected), so decoding always terminates.
/// </remarks>
internal static class ManagedBrotliDecoder
{
    private const int NumLiteralSymbols = 256;
    private const int NumCommandSymbols = 704;
    private const int NumBlockCountSymbols = 26;
    private const int LiteralContextBits = 6;
    private const int DistanceContextBits = 2;

    private static ReadOnlySpan<int> BlockCountBase =>
    [
        1, 5, 9, 13, 17, 25, 33, 41, 49, 65, 81, 97, 113, 145, 177, 209, 241, 305, 369, 497,
        753, 1265, 2289, 4337, 8433, 16625,
    ];

    private static ReadOnlySpan<byte> BlockCountExtraBits =>
    [
        2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 7, 8, 9, 10, 11, 12, 13, 24,
    ];

    private static ReadOnlySpan<int> InsertLengthBase =>
    [
        0, 1, 2, 3, 4, 5, 6, 8, 10, 14, 18, 26, 34, 50, 66, 98, 130, 194, 322, 578, 1090, 2114,
        6210, 22594,
    ];

    private static ReadOnlySpan<byte> InsertLengthExtraBits =>
    [
        0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 12, 14, 24,
    ];

    private static ReadOnlySpan<int> CopyLengthBase =>
    [
        2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 18, 22, 30, 38, 54, 70, 102, 134, 198, 326, 582,
        1094, 2118,
    ];

    private static ReadOnlySpan<byte> CopyLengthExtraBits =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 24,
    ];

    /// <summary>Insert length code base for each 64-symbol cell of the command alphabet (RFC 7932 section 5).</summary>
    private static ReadOnlySpan<byte> CommandCellInsertBase => [0, 0, 0, 0, 8, 8, 0, 16, 8, 16, 16];

    /// <summary>Copy length code base for each 64-symbol cell of the command alphabet.</summary>
    private static ReadOnlySpan<byte> CommandCellCopyBase => [0, 8, 0, 8, 0, 8, 16, 0, 16, 8, 16];

    /// <summary>Every insert-and-copy symbol's lengths, worked out once from the tables above.</summary>
    private static readonly CommandLengths[] s_commandLengths = BuildCommandLengths();

    /// <summary>Distance codes 0..15: which ring entry (0 = last) each one starts from...</summary>
    private static ReadOnlySpan<byte> ShortCodeRingIndex => [0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1];

    /// <summary>...and the delta added to it (RFC 7932 section 4).</summary>
    private static ReadOnlySpan<sbyte> ShortCodeDelta => [0, 0, 0, 0, -1, 1, -2, 2, -3, 3, -1, 1, -2, 2, -3, 3];

    /// <summary>
    /// Decompresses a complete Brotli stream. Throws <see cref="FontParsingException"/> if the
    /// stream is malformed, truncated, or would produce more than <paramref name="maxOutputLength"/> bytes.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int maxOutputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputLength);

        var reader = new BrotliBitReader(input);
        var output = new Output(maxOutputLength, input.Length);
        var windowSize = (1 << ReadWindowBits(ref reader)) - 16;
        var distances = new DistanceRing();

        while (true)
        {
            var isLast = reader.Read(1) == 1;
            if (isLast && reader.Read(1) == 1)
                break; // ISLASTEMPTY

            var nibbles = (int)reader.Read(2);
            if (nibbles == 3)
            {
                SkipMetadata(ref reader);
                if (isLast)
                    break;
                continue;
            }

            nibbles += 4;
            var metaLength = (int)reader.Read(nibbles * 4);
            if (nibbles > 4 && (metaLength >> ((nibbles - 1) * 4)) == 0)
                throw Invalid("meta-block length has a redundant zero nibble");
            metaLength++;
            output.Reserve(metaLength);

            if (!isLast && reader.Read(1) == 1)
            {
                reader.AlignToByte();
                reader.ReadBytes(output.Buffer.AsSpan(output.Position, metaLength));
                output.Position += metaLength;
                continue;
            }

            DecodeCompressedMetaBlock(ref reader, output, metaLength, windowSize, distances);

            if (isLast)
                break;
        }

        return output.ToArray();
    }

    /// <summary>Builds the exception every malformed-stream path throws.</summary>
    internal static FontParsingException Invalid(string reason) => new($"Invalid Brotli stream: {reason}.");

    private static int ReadWindowBits(ref BrotliBitReader reader)
    {
        if (reader.Read(1) == 0)
            return 16;

        var n = (int)reader.Read(3);
        if (n != 0)
            return 17 + n;

        n = (int)reader.Read(3);
        if (n == 1)
            throw Invalid("reserved window size code");
        return n == 0 ? 17 : 8 + n;
    }

    private static void SkipMetadata(ref BrotliBitReader reader)
    {
        if (reader.Read(1) != 0)
            throw Invalid("metadata block reserved bit is set");

        var sizeBytes = (int)reader.Read(2);
        var skipLength = 0;
        if (sizeBytes > 0)
        {
            skipLength = (int)reader.Read(sizeBytes * 8);
            if (sizeBytes > 1 && (skipLength >> ((sizeBytes - 1) * 8)) == 0)
                throw Invalid("metadata length has a redundant zero byte");
            skipLength++;
        }

        reader.AlignToByte();
        reader.SkipBytes(skipLength);
    }

    private static void DecodeCompressedMetaBlock(
        ref BrotliBitReader reader, Output output, int metaLength, int windowSize, DistanceRing distances)
    {
        var literalBlocks = BlockSwitcher.Read(ref reader);
        var commandBlocks = BlockSwitcher.Read(ref reader);
        var distanceBlocks = BlockSwitcher.Read(ref reader);

        var postfixBits = (int)reader.Read(2);
        var directCodes = (int)reader.Read(4) << postfixBits;
        var distanceAlphabetSize = 16 + directCodes + (48 << postfixBits);

        var contextModes = new byte[literalBlocks.TypeCount];
        for (var i = 0; i < contextModes.Length; i++)
            contextModes[i] = (byte)reader.Read(2);

        var literalTreeCount = ReadCount256(ref reader);
        var literalContextMap = ReadContextMap(ref reader, literalBlocks.TypeCount << LiteralContextBits, literalTreeCount);
        var distanceTreeCount = ReadCount256(ref reader);
        var distanceContextMap = ReadContextMap(ref reader, distanceBlocks.TypeCount << DistanceContextBits, distanceTreeCount);

        var literalCodes = ReadCodes(ref reader, literalTreeCount, NumLiteralSymbols);
        var commandCodes = ReadCodes(ref reader, commandBlocks.TypeCount, NumCommandSymbols);
        var distanceCodes = ReadCodes(ref reader, distanceTreeCount, distanceAlphabetSize);

        // The literal table for every (block type, context ID), resolved through the context map once.
        var literalTables = new int[literalBlocks.TypeCount << LiteralContextBits][];
        for (var i = 0; i < literalTables.Length; i++)
            literalTables[i] = literalCodes[literalContextMap[i]].Table;

        var buffer = output.Buffer;
        var pos = output.Position;
        var end = pos + metaLength;
        var postfixMask = (1 << postfixBits) - 1;
        var contextLookup = BrotliContext.SplitLookup;
        Span<byte> word = stackalloc byte[40];

        while (pos < end)
        {
            var bitsBefore = reader.BitsConsumed;
            var posBefore = pos;

            commandBlocks.Advance(ref reader);
            var lengths = s_commandLengths[commandCodes[commandBlocks.CurrentType].Decode(ref reader)];
            var insertLength = lengths.InsertBase + (int)reader.Read(lengths.InsertExtraBits);
            var copyLength = lengths.CopyBase + (int)reader.Read(lengths.CopyExtraBits);

            if (insertLength > end - pos)
                throw Invalid("literal insertion runs past the end of the meta-block");

            if (insertLength > 0)
            {
                // The last two output bytes, carried in locals rather than re-read per literal.
                var p1 = pos > 0 ? buffer[pos - 1] : 0;
                var p2 = pos > 1 ? buffer[pos - 2] : 0;
                var insertEnd = pos + insertLength;
                while (pos < insertEnd)
                {
                    // One run per literal block: its type fixes the context mode and tables.
                    literalBlocks.SwitchIfDue(ref reader);
                    var type = literalBlocks.CurrentType;
                    var lookup = contextLookup.AsSpan(contextModes[type] << 9, 512);
                    var tables = literalTables.AsSpan(type << LiteralContextBits, 1 << LiteralContextBits);
                    var stop = pos + Math.Min(insertEnd - pos, literalBlocks.Remaining);
                    var reached = reader.DecodeLiterals(buffer, pos, stop, lookup, tables, ref p1, ref p2);
                    if (reached == pos)
                    {
                        // Near the end of the input: one literal through the padding-aware path.
                        var literal = BrotliPrefixCode.Decode(ref reader, tables[lookup[p1] | lookup[256 + p2]]);
                        buffer[reached++] = (byte)literal;
                        p2 = p1;
                        p1 = literal;
                    }

                    literalBlocks.Consume(reached - pos);
                    pos = reached;
                }
            }

            if (pos == end)
                break; // the copy length of a meta-block's final command is ignored

            var maxDistance = Math.Min(windowSize, pos);
            int distance;
            if (lengths.ImplicitDistance)
            {
                // Implicit distance code 0: the last distance, not pushed again.
                distance = distances.Get(0);
            }
            else
            {
                distanceBlocks.Advance(ref reader);
                var contextId = copyLength > 4 ? 3 : copyLength - 2;
                var tree = distanceContextMap[(distanceBlocks.CurrentType << DistanceContextBits) + contextId];
                var code = distanceCodes[tree].Decode(ref reader);
                distance = DecodeDistance(ref reader, code, directCodes, postfixBits, postfixMask, distances);
                if (distance <= 0)
                    throw Invalid("distance code resolves to a non-positive distance");

                // Code 0 and static-dictionary references never enter the ring.
                if (code != 0 && distance <= maxDistance)
                    distances.Push(distance);
            }

            if (distance <= maxDistance)
            {
                if (copyLength > end - pos)
                    throw Invalid("copy runs past the end of the meta-block");

                var from = pos - distance;
                if (distance >= copyLength)
                {
                    buffer.AsSpan(from, copyLength).CopyTo(buffer.AsSpan(pos));
                    pos += copyLength;
                }
                else
                {
                    // Overlapping copy: the output repeats the last `distance` bytes. Each pass
                    // copies the whole pattern written so far, from `from` up to `pos`, which
                    // ends where the destination begins, so the halves never overlap and the
                    // run doubles every pass.
                    var remaining = copyLength;
                    while (remaining > 0)
                    {
                        var chunk = Math.Min(pos - from, remaining);
                        buffer.AsSpan(from, chunk).CopyTo(buffer.AsSpan(pos, chunk));
                        pos += chunk;
                        remaining -= chunk;
                    }
                }
            }
            else
            {
                if (copyLength < BrotliStaticDictionary.MinWordLength || copyLength > BrotliStaticDictionary.MaxWordLength)
                    throw Invalid($"static dictionary reference with invalid length {copyLength}");

                var wordId = distance - maxDistance - 1;
                var sizeBits = BrotliStaticDictionary.SizeBits(copyLength);
                var transformId = wordId >> sizeBits;
                if (transformId >= BrotliStaticDictionary.TransformCount)
                    throw Invalid($"static dictionary transform {transformId} does not exist");

                var length = BrotliStaticDictionary.TransformWord(word, copyLength, wordId & ((1 << sizeBits) - 1), transformId);
                if (length > end - pos)
                    throw Invalid("dictionary word runs past the end of the meta-block");
                word.Slice(0, length).CopyTo(buffer.AsSpan(pos));
                pos += length;
            }

            if (pos == posBefore && reader.BitsConsumed == bitsBefore)
                throw Invalid("a command neither consumed input nor produced output");
        }

        output.Position = pos;
    }

    private static int DecodeDistance(
        ref BrotliBitReader reader, int code, int directCodes, int postfixBits, int postfixMask, DistanceRing ring)
    {
        if (code < 16)
            return ring.Get(ShortCodeRingIndex[code]) + ShortCodeDelta[code];

        if (code < 16 + directCodes)
            return code - 15;

        var offsetCode = code - directCodes - 16;
        var extraBits = 1 + (offsetCode >> (postfixBits + 1));
        var high = offsetCode >> postfixBits;
        var low = offsetCode & postfixMask;
        var offset = ((2 + (high & 1)) << extraBits) - 4;
        return ((offset + (int)reader.Read(extraBits)) << postfixBits) + low + directCodes + 1;
    }

    /// <summary>The 1..256 variable-length code used for NBLTYPESx and NTREESx (RFC 7932 section 9.2).</summary>
    private static int ReadCount256(ref BrotliBitReader reader)
    {
        if (reader.Read(1) == 0)
            return 1;
        var n = (int)reader.Read(3);
        if (n == 0)
            return 2;
        return (1 << n) + (int)reader.Read(n) + 1;
    }

    private static int ReadBlockCount(ref BrotliBitReader reader, BrotliPrefixCode code)
    {
        var symbol = code.Decode(ref reader);
        return BlockCountBase[symbol] + (int)reader.Read(BlockCountExtraBits[symbol]);
    }

    private static byte[] ReadContextMap(ref BrotliBitReader reader, int size, int treeCount)
    {
        var map = new byte[size];
        if (treeCount < 2)
            return map;

        var maxRunLengthPrefix = 0;
        if (reader.Read(1) == 1)
            maxRunLengthPrefix = (int)reader.Read(4) + 1;

        var code = BrotliPrefixCode.Read(ref reader, treeCount + maxRunLengthPrefix);
        var i = 0;
        while (i < size)
        {
            var symbol = code.Decode(ref reader);
            if (symbol == 0)
            {
                map[i++] = 0;
            }
            else if (symbol <= maxRunLengthPrefix)
            {
                var run = (1 << symbol) + (int)reader.Read(symbol);
                if (run > size - i)
                    throw Invalid("context map zero run is longer than the map");
                i += run; // the map is already zero-filled
            }
            else
            {
                map[i++] = (byte)(symbol - maxRunLengthPrefix);
            }
        }

        if (reader.Read(1) == 1)
            InverseMoveToFront(map);

        // Inverse move-to-front keeps values below treeCount, but a hostile map could still name
        // a tree that was never declared without it, so check once here instead of per lookup.
        foreach (var value in map)
        {
            if (value >= treeCount)
                throw Invalid("context map refers to a prefix code that does not exist");
        }

        return map;
    }

    private static void InverseMoveToFront(byte[] values)
    {
        Span<byte> mtf = stackalloc byte[256];
        for (var i = 0; i < 256; i++)
            mtf[i] = (byte)i;

        for (var i = 0; i < values.Length; i++)
        {
            int index = values[i];
            var value = mtf[index];
            values[i] = value;
            for (; index > 0; index--)
                mtf[index] = mtf[index - 1];
            mtf[0] = value;
        }
    }

    private static BrotliPrefixCode[] ReadCodes(ref BrotliBitReader reader, int count, int alphabetSize)
    {
        var codes = new BrotliPrefixCode[count];
        for (var i = 0; i < count; i++)
            codes[i] = BrotliPrefixCode.Read(ref reader, alphabetSize);
        return codes;
    }

    private static CommandLengths[] BuildCommandLengths()
    {
        var table = new CommandLengths[NumCommandSymbols];
        for (var command = 0; command < table.Length; command++)
        {
            var cell = command >> 6;
            var insertCode = CommandCellInsertBase[cell] + ((command >> 3) & 7);
            var copyCode = CommandCellCopyBase[cell] + (command & 7);
            table[command] = new CommandLengths(
                InsertLengthBase[insertCode], InsertLengthExtraBits[insertCode],
                CopyLengthBase[copyCode], CopyLengthExtraBits[copyCode],
                ImplicitDistance: cell < 2);
        }

        return table;
    }

    /// <summary>Block-type and block-count state for one category (literal, command or distance).</summary>
    private sealed class BlockSwitcher
    {
        private readonly BrotliPrefixCode? _typeCode;
        private readonly BrotliPrefixCode? _countCode;
        private int _previousType = 1;
        private int _remaining;

        private BlockSwitcher(int typeCount, BrotliPrefixCode? typeCode, BrotliPrefixCode? countCode, int firstCount)
        {
            TypeCount = typeCount;
            _typeCode = typeCode;
            _countCode = countCode;
            _remaining = firstCount;
        }

        public int TypeCount { get; }

        public int CurrentType { get; private set; }

        public static BlockSwitcher Read(ref BrotliBitReader reader)
        {
            var typeCount = ReadCount256(ref reader);
            // A single block type never switches: its count starts beyond any meta-block's
            // symbols (at most 2^24), so Advance never reaches zero.
            if (typeCount < 2)
                return new BlockSwitcher(1, null, null, int.MaxValue);

            var typeCode = BrotliPrefixCode.Read(ref reader, typeCount + 2);
            var countCode = BrotliPrefixCode.Read(ref reader, NumBlockCountSymbols);
            var firstCount = ReadBlockCount(ref reader, countCode);
            return new BlockSwitcher(typeCount, typeCode, countCode, firstCount);
        }

        /// <summary>Symbols left in the current block.</summary>
        public int Remaining => _remaining;

        /// <summary>Accounts for one more symbol of this category, switching blocks first when the current one is used up.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(ref BrotliBitReader reader)
        {
            SwitchIfDue(ref reader);
            _remaining--;
        }

        /// <summary>Starts the next block if the current one is used up.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SwitchIfDue(ref BrotliBitReader reader)
        {
            if (_remaining == 0)
                Switch(ref reader);
        }

        /// <summary>Accounts for <paramref name="count"/> symbols of the current block (at most <see cref="Remaining"/>).</summary>
        public void Consume(int count) => _remaining -= count;

        /// <summary>Starts the next block: reads its type and its symbol count.</summary>
        private void Switch(ref BrotliBitReader reader)
        {
            var symbol = _typeCode!.Decode(ref reader);
            var type = symbol switch
            {
                0 => _previousType,
                1 => CurrentType + 1,
                _ => symbol - 2,
            };

            // Symbol 1 wraps from the last type to 0; an explicit type beyond the count is corrupt.
            if (type == TypeCount && symbol == 1)
                type = 0;
            if (type >= TypeCount)
                throw Invalid("block type out of range");

            _previousType = CurrentType;
            CurrentType = type;
            _remaining = ReadBlockCount(ref reader, _countCode!);
        }
    }

    /// <summary>The last four distances, initialised once per stream to 4, 11, 15, 16 (RFC 7932 section 4).</summary>
    private sealed class DistanceRing
    {
        private readonly int[] _values = [4, 11, 15, 16];

        /// <summary>0 = last, 1 = second-to-last, ...</summary>
        public int Get(int index) => _values[index];

        public void Push(int distance)
        {
            _values[3] = _values[2];
            _values[2] = _values[1];
            _values[1] = _values[0];
            _values[0] = distance;
        }
    }

    /// <summary>The growing output buffer, capped at the caller's limit.</summary>
    private sealed class Output
    {
        private readonly int _limit;

        public Output(int limit, int inputLength)
        {
            _limit = limit;
            Buffer = new byte[(int)Math.Min(limit, Math.Max(256L, inputLength * 4L))];
        }

        public byte[] Buffer { get; private set; }

        public int Position { get; set; }

        /// <summary>Ensures room for <paramref name="count"/> more bytes, enforcing the limit.</summary>
        public void Reserve(int count)
        {
            if (count > _limit - Position)
                throw Invalid($"output exceeds the limit of {_limit} bytes");

            var needed = Position + count;
            if (needed <= Buffer.Length)
                return;

            var newSize = (int)Math.Min(_limit, Math.Max(needed, Buffer.Length * 2L));
            var grown = new byte[newSize];
            Buffer.AsSpan(0, Position).CopyTo(grown);
            Buffer = grown;
        }

        public byte[] ToArray() => Position == Buffer.Length ? Buffer : Buffer.AsSpan(0, Position).ToArray();
    }
}
