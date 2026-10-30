using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace KernSmith.Font.Brotli;

/// <summary>
/// LSB-first bit reader over a Brotli stream (RFC 7932 section 1.5.1).
/// </summary>
/// <remarks>
/// Peeking past the end of the input yields zero bits so a prefix-code lookup can always peek
/// its full table width near the end of a stream; <em>consuming</em> any of those padding bits
/// throws. That is the single truncation check every read goes through, so a cut-off stream can
/// never decode as if it ended in zeros.
///
/// INVARIANT: every bit of the buffer above <c>_bitCount</c> is zero. Refills OR new bytes in
/// at <c>_bitCount</c>, and <see cref="ReadBytes"/>/<see cref="SkipBytes"/> step past the buffer
/// to read the input directly, so a stray high bit would corrupt the stream.
/// </remarks>
internal ref struct BrotliBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _nextByte;
    private ulong _buffer;
    private int _bitCount;
    private int _paddingBits;

    public BrotliBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    /// <summary>Total bits consumed so far.</summary>
    public readonly long BitsConsumed => (long)_nextByte * 8 + _paddingBits - _bitCount;

    /// <summary>Returns the next <paramref name="count"/> bits (0..32) without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Peek(int count)
    {
        if (_bitCount < count)
            Fill();
        return (uint)(_buffer & ((1UL << count) - 1));
    }

    /// <summary>Consumes <paramref name="count"/> bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count)
    {
        if (_bitCount < count)
            Fill();
        SkipPeeked(count);
    }

    /// <summary>
    /// Consumes <paramref name="count"/> bits that a <see cref="Peek"/> of at least as many bits
    /// has just made available, skipping the refill check.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipPeeked(int count)
    {
        _buffer >>= count;
        _bitCount -= count;
        if (_bitCount < _paddingBits)
            throw ManagedBrotliDecoder.Invalid("the stream is truncated");
    }

    /// <summary>Reads and consumes <paramref name="count"/> bits (0..32).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Read(int count)
    {
        var value = Peek(count);
        SkipPeeked(count);
        return value;
    }

    /// <summary>Skips to the next byte boundary; the skipped bits must be zero.</summary>
    public void AlignToByte()
    {
        var pending = _bitCount & 7;
        if (pending != 0 && Read(pending) != 0)
            throw ManagedBrotliDecoder.Invalid("non-zero padding bits before a byte boundary");
    }

    /// <summary>Copies whole bytes from a byte-aligned position into <paramref name="destination"/>.</summary>
    public void ReadBytes(Span<byte> destination)
    {
        var i = 0;
        while (i < destination.Length && _bitCount >= 8)
            destination[i++] = (byte)Read(8);

        var remaining = destination.Length - i;
        if (remaining == 0)
            return;
        if (_nextByte > _data.Length - remaining)
            throw ManagedBrotliDecoder.Invalid("the stream is truncated");

        _data.Slice(_nextByte, remaining).CopyTo(destination.Slice(i));
        _nextByte += remaining;
    }

    /// <summary>Skips whole bytes from a byte-aligned position.</summary>
    public void SkipBytes(int count)
    {
        while (count > 0 && _bitCount >= 8)
        {
            Skip(8);
            count--;
        }

        if (count == 0)
            return;
        if (_nextByte > _data.Length - count)
            throw ManagedBrotliDecoder.Invalid("the stream is truncated");
        _nextByte += count;
    }

    /// <summary>
    /// Decodes literals into <paramref name="output"/> from <paramref name="pos"/> up to
    /// <paramref name="stop"/>, all within one literal block, with the bit buffer held in
    /// locals. Stops early, possibly before the first literal, once fewer than eight input
    /// bytes remain: from there the careful path (<see cref="BrotliPrefixCode.Decode(ref BrotliBitReader, int[])"/>)
    /// takes over, because only it handles the zero padding past the end of the input.
    /// </summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="pos">Where the next literal goes.</param>
    /// <param name="stop">Where this run ends; at most the end of the block and the insert.</param>
    /// <param name="contextLookup">The block type's 512-byte slice of <see cref="BrotliContext.SplitLookup"/>.</param>
    /// <param name="tables">The block type's 64 literal tables, one per context ID.</param>
    /// <param name="p1">The last output byte; updated.</param>
    /// <param name="p2">The byte before it; updated.</param>
    /// <returns>The position after the last literal written.</returns>
    public int DecodeLiterals(
        Span<byte> output, int pos, int stop, ReadOnlySpan<byte> contextLookup, ReadOnlySpan<int[]> tables, ref int p1, ref int p2)
    {
        // With padding in the buffer the end of the input is near; leave it to the careful path.
        if (_paddingBits != 0)
            return pos;

        const int RootMask = (1 << BrotliPrefixCode.RootBits) - 1;
        var data = _data;
        var buffer = _buffer;
        var bitCount = _bitCount;
        var nextByte = _nextByte;
        var last = p1;
        var beforeLast = p2;

        while (pos < stop)
        {
            if (bitCount < BrotliPrefixCode.MaxCodeLength)
            {
                if (nextByte > data.Length - 8)
                    break;

                // Fill's word refill, keeping the bits above bitCount zero.
                var bytes = (63 - bitCount) >> 3;
                var word = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(nextByte));
                buffer |= (word & ((1UL << (bytes * 8)) - 1)) << bitCount;
                nextByte += bytes;
                bitCount += bytes * 8;
            }

            // BrotliPrefixCode.Decode, unrolled onto the locals: bitCount >= 15 covers both levels.
            var table = tables[contextLookup[last] | contextLookup[256 + beforeLast]];
            var entry = table[(int)buffer & RootMask];
            var length = entry >> 16;
            if (length > BrotliPrefixCode.RootBits)
            {
                var subIndex = (int)(buffer >> BrotliPrefixCode.RootBits) & ((1 << (length - BrotliPrefixCode.RootBits)) - 1);
                entry = table[(entry & 0xFFFF) + subIndex];
                length = BrotliPrefixCode.RootBits + (entry >> 16);
            }

            buffer >>= length;
            bitCount -= length;

            var literal = entry & 0xFFFF;
            output[pos++] = (byte)literal;
            beforeLast = last;
            last = literal;
        }

        _buffer = buffer;
        _bitCount = bitCount;
        _nextByte = nextByte;
        p1 = last;
        p2 = beforeLast;
        return pos;
    }

    /// <summary>Tops the buffer up to at least 56 bits.</summary>
    private void Fill()
    {
        // Whole bytes that fit above the bits still buffered: seven or eight.
        var bytes = (63 - _bitCount) >> 3;
        if (_nextByte <= _data.Length - 8)
        {
            // One little-endian word, masked to the bytes taken so nothing lands above _bitCount
            // (bytes * 8 is at most 56, so the mask shift stays below 64).
            var word = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_nextByte));
            _buffer |= (word & ((1UL << (bytes * 8)) - 1)) << _bitCount;
            _nextByte += bytes;
            _bitCount += bytes * 8;
            return;
        }

        FillTail();
    }

    /// <summary>The last few input bytes, one at a time, then zero padding.</summary>
    private void FillTail()
    {
        while (_bitCount <= 56)
        {
            if (_nextByte < _data.Length)
            {
                _buffer |= (ulong)_data[_nextByte++] << _bitCount;
            }
            else
            {
                // Zero padding past the end; Skip rejects any attempt to consume it.
                if (_paddingBits > 64)
                    throw ManagedBrotliDecoder.Invalid("the stream is truncated");
                _paddingBits += 8;
            }

            _bitCount += 8;
        }
    }
}
