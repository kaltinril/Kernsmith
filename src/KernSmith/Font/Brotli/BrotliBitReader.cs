namespace KernSmith.Font.Brotli;

/// <summary>
/// LSB-first bit reader over a Brotli stream (RFC 7932 section 1.5.1).
/// </summary>
/// <remarks>
/// Peeking past the end of the input yields zero bits so a prefix-code lookup can always peek
/// its full table width near the end of a stream; <em>consuming</em> any of those padding bits
/// throws. That is the single truncation check every read goes through, so a cut-off stream can
/// never decode as if it ended in zeros.
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
    public uint Peek(int count)
    {
        if (_bitCount < count)
            Fill();
        return (uint)(_buffer & ((1UL << count) - 1));
    }

    /// <summary>Consumes <paramref name="count"/> bits.</summary>
    public void Skip(int count)
    {
        if (_bitCount < count)
            Fill();
        _buffer >>= count;
        _bitCount -= count;
        if (_bitCount < _paddingBits)
            throw ManagedBrotliDecoder.Invalid("the stream is truncated");
    }

    /// <summary>Reads and consumes <paramref name="count"/> bits (0..32).</summary>
    public uint Read(int count)
    {
        if (count == 0)
            return 0;
        var value = Peek(count);
        Skip(count);
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

    private void Fill()
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
