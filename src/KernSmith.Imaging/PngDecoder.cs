using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using KernSmith.Imaging.Internal;

namespace KernSmith.Imaging;

/// <summary>
/// Decodes PNG images (W3C PNG Specification, Third Edition) to 8-bit straight-alpha RGBA.
/// </summary>
/// <remarks>
/// <para>
/// Every color type and bit depth the format defines is decoded, interlaced (Adam7) or not, with
/// all five scanline filters. <c>PLTE</c> and <c>tRNS</c> are applied; samples are rescaled to
/// 8 bits with the spec's most accurate rule, <c>round(v × 255 / (2^depth − 1))</c>, after the
/// <c>tRNS</c> comparison (which uses the full sample, 16 bits included).
/// </para>
/// <para>
/// No color management is done: <c>gAMA</c>, <c>cHRM</c>, <c>sRGB</c>, <c>iCCP</c>, <c>cICP</c>
/// and every other ancillary chunk are skipped unread (only their CRC is checked), so samples come
/// out as stored. An APNG's animation chunks are ancillary too, so its default image decodes.
/// </para>
/// <para>
/// The input is treated as untrusted. Chunk framing, CRCs, the header, chunk order and palette
/// indices are validated, and any failure is a <see cref="PngFormatException"/> — never an index or
/// overflow exception. The declared size is checked against <see cref="PngDecodeLimits"/> before
/// anything is allocated, and inflation stops at exactly the number of bytes the header implies, so
/// a small file cannot expand into a large allocation. Thread-safe: there is no shared mutable state.
/// </para>
/// <para>
/// Deliberately strict where the spec says "error": unknown critical chunks, CRC mismatches,
/// non-consecutive <c>IDAT</c>s, a missing <c>IEND</c>, out-of-range palette indices, and a
/// <c>PLTE</c> that is malformed, repeated, late, too long for the bit depth, or present in a
/// grayscale image all throw. A malformed or misplaced <c>tRNS</c> is ancillary and is ignored, as
/// the spec allows. Bytes after the end of the image data, and after <c>IEND</c>, are ignored.
/// </para>
/// </remarks>
public static class PngDecoder
{
    private const uint ChunkIhdr = 0x49484452; // IHDR
    private const uint ChunkPlte = 0x504C5445; // PLTE
    private const uint ChunkIdat = 0x49444154; // IDAT
    private const uint ChunkIend = 0x49454E44; // IEND
    private const uint ChunkTrns = 0x74524E53; // tRNS

    /// <summary>Deflate's largest possible expansion: a 258-byte match costs at least two bits.</summary>
    private const long MaxDeflateRatio = 1032;

    private static ReadOnlySpan<byte> SignatureBytes => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>True when <paramref name="data"/> starts with the 8-byte PNG signature.</summary>
    /// <param name="data">The candidate file bytes.</param>
    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= SignatureBytes.Length && data[..SignatureBytes.Length].SequenceEqual(SignatureBytes);

    /// <summary>Reads and validates the signature and <c>IHDR</c> chunk, without decoding pixels.</summary>
    /// <param name="data">The PNG file bytes.</param>
    /// <exception cref="PngFormatException">If the signature or <c>IHDR</c> is missing or invalid.</exception>
    public static PngHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        CheckSignature(data);
        return ReadIhdr(data, out _);
    }

    /// <summary>Decodes a PNG to straight-alpha RGBA.</summary>
    /// <param name="data">The PNG file bytes.</param>
    /// <param name="limits">Size limits; <see cref="PngDecodeLimits.Default"/> when null.</param>
    /// <exception cref="PngFormatException">If the data is not a valid PNG or exceeds the limits.</exception>
    public static PngImage Decode(ReadOnlySpan<byte> data, PngDecodeLimits? limits = null)
    {
        limits ??= PngDecodeLimits.Default;
        CheckSignature(data);
        var header = ReadIhdr(data, out int position);
        CheckLimits(header, limits);

        var pixels = new PixelFormat(header);
        var idat = new List<Range>();
        bool idatEnded = false, sawTrns = false, sawIend = false;
        long compressedLength = 0;

        while (!sawIend)
        {
            var chunk = ReadChunk(data, position);
            position = chunk.Next;
            var body = data.Slice(chunk.DataOffset, chunk.Length);

            switch (chunk.Type)
            {
                case ChunkIhdr:
                    throw new PngFormatException("The PNG has more than one IHDR chunk.");

                case ChunkPlte:
                    if (idat.Count > 0)
                        throw new PngFormatException("The PLTE chunk follows the image data.");
                    pixels.SetPalette(body);
                    break;

                case ChunkTrns:
                    if (idat.Count == 0 && !sawTrns)
                        pixels.TrySetTransparency(body);
                    sawTrns = true;
                    break;

                case ChunkIdat:
                    if (idatEnded)
                        throw new PngFormatException("The IDAT chunks are not consecutive.");
                    if (header.ColorType == PngColorType.Indexed && !pixels.HasPalette)
                        throw new PngFormatException("An indexed-color PNG has no PLTE chunk before its image data.");
                    idat.Add(new Range(chunk.DataOffset, chunk.DataOffset + chunk.Length));
                    compressedLength += chunk.Length;
                    break;

                case ChunkIend:
                    sawIend = true;
                    break;

                default:
                    if (IsCritical(chunk.Type))
                        throw new PngFormatException($"The PNG has an unknown critical chunk '{TypeName(chunk.Type)}'.");
                    break;
            }

            if (chunk.Type != ChunkIdat && idat.Count > 0)
                idatEnded = true;
        }

        if (idat.Count == 0)
            throw new PngFormatException("The PNG has no IDAT image data.");

        // The decompressed scanlines are scratch: pooled, and only their first RawSize bytes are used.
        long rawSize = Adam7.RawSize(header);
        var raw = Inflate(data, idat, compressedLength, rawSize);
        try
        {
            var rgba = new byte[(long)header.Width * header.Height * 4];
            Scanlines.Reconstruct(raw.AsSpan(0, (int)rawSize), header, pixels, rgba);
            return new PngImage(header.Width, header.Height, rgba);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    private static void CheckSignature(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data))
            throw new PngFormatException("The data does not start with the PNG signature.");
    }

    private static PngHeader ReadIhdr(ReadOnlySpan<byte> data, out int next)
    {
        var chunk = ReadChunk(data, SignatureBytes.Length);
        if (chunk.Type != ChunkIhdr)
            throw new PngFormatException("The PNG does not start with an IHDR chunk.");
        if (chunk.Length != 13)
            throw new PngFormatException($"The IHDR chunk is {chunk.Length} bytes; it must be 13.");

        var body = data.Slice(chunk.DataOffset, 13);
        uint width = BinaryPrimitives.ReadUInt32BigEndian(body);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
        int bitDepth = body[8];
        int colorType = body[9];

        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new PngFormatException($"The PNG's dimensions {width}x{height} are invalid.");

        bool depthAllowed = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => bitDepth is 8 or 16,
            _ => throw new PngFormatException($"The PNG's color type {colorType} is invalid."),
        };
        if (!depthAllowed)
            throw new PngFormatException($"Bit depth {bitDepth} is invalid for PNG color type {colorType}.");
        if (body[10] != 0)
            throw new PngFormatException($"The PNG's compression method {body[10]} is unknown.");
        if (body[11] != 0)
            throw new PngFormatException($"The PNG's filter method {body[11]} is unknown.");
        if (body[12] > 1)
            throw new PngFormatException($"The PNG's interlace method {body[12]} is unknown.");

        next = chunk.Next;
        return new PngHeader((int)width, (int)height, bitDepth, (PngColorType)colorType, body[12] == 1);
    }

    private static void CheckLimits(PngHeader header, PngDecodeLimits limits)
    {
        if (header.Width > limits.MaxWidth || header.Height > limits.MaxHeight
            || (long)header.Width * header.Height > limits.MaxPixels)
            throw new PngFormatException(
                $"The PNG is {header.Width}x{header.Height}, over the decode limits ({limits.MaxWidth}x{limits.MaxHeight}, {limits.MaxPixels} pixels).");
        if ((long)header.Width * header.Height * 4 > Array.MaxLength)
            throw new PngFormatException($"The PNG is {header.Width}x{header.Height}, too large for one RGBA array.");
    }

    /// <summary>Reads the chunk at <paramref name="position"/>, verifying its framing, type and CRC.</summary>
    private static Chunk ReadChunk(ReadOnlySpan<byte> data, int position)
    {
        if (data.Length - position < 12)
            throw new PngFormatException($"The PNG is truncated: a chunk header is expected at byte {position}.");

        uint length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
        if (length > int.MaxValue || length > (uint)(data.Length - position - 12))
            throw new PngFormatException($"The PNG is truncated: the chunk at byte {position} claims {length} bytes.");

        uint type = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
        for (int shift = 0; shift < 32; shift += 8)
        {
            uint letter = (type >> shift) & 0xFF;
            if (!(letter is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
                throw new PngFormatException($"The chunk at byte {position} has an invalid type 0x{type:X8}.");
        }

        int dataOffset = position + 8;
        int bodyLength = (int)length;
        uint stored = BinaryPrimitives.ReadUInt32BigEndian(data[(dataOffset + bodyLength)..]);
        if (Crc32.Compute(data.Slice(position + 4, 4 + bodyLength)) != stored)
            throw new PngFormatException($"The '{TypeName(type)}' chunk at byte {position} fails its CRC check.");

        return new Chunk(type, dataOffset, bodyLength, dataOffset + bodyLength + 4);
    }

    /// <summary>
    /// Inflates the concatenated <c>IDAT</c> data to exactly <paramref name="expected"/> bytes.
    /// Anything the stream holds past that is never decompressed.
    /// </summary>
    /// <returns>A pooled array whose first <paramref name="expected"/> bytes hold the data; the
    /// caller returns it to <see cref="ArrayPool{T}.Shared"/>.</returns>
    private static byte[] Inflate(ReadOnlySpan<byte> data, List<Range> idat, long compressedLength, long expected)
    {
        if (expected > Array.MaxLength)
            throw new PngFormatException($"The PNG's image data ({expected} bytes) is too large to decode.");
        // Cheap rejection of a header that the compressed data cannot possibly fill.
        if (expected > (compressedLength * MaxDeflateRatio) + 1024)
            throw new PngFormatException($"The PNG's {compressedLength} bytes of image data cannot hold the {expected} bytes its header declares.");

        int length = (int)compressedLength;
        byte[] compressed = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        byte[] raw = ArrayPool<byte>.Shared.Rent((int)Math.Max(expected, 1));
        bool filledRaw = false;
        try
        {
            int offset = 0;
            foreach (var range in idat)
            {
                var (start, count) = range.GetOffsetAndLength(data.Length);
                var segment = data.Slice(start, count);
                segment.CopyTo(compressed.AsSpan(offset));
                offset += segment.Length;
            }

            using var zlib = new ZLibStream(new MemoryStream(compressed, 0, length, writable: false), CompressionMode.Decompress);
            int filled = 0;
            while (filled < expected)
            {
                int read = zlib.Read(raw, filled, (int)expected - filled);
                if (read == 0)
                    throw new PngFormatException($"The PNG's image data ends after {filled} of {expected} bytes.");
                filled += read;
            }

            // One more read lets the inflater reach and verify the stream's Adler-32 when the
            // stream ends here; data beyond the image is ignored, never decompressed further.
            Span<byte> probe = stackalloc byte[1];
            _ = zlib.Read(probe);
            filledRaw = true;
            return raw;
        }
        catch (InvalidDataException ex)
        {
            throw new PngFormatException($"The PNG's compressed image data is corrupt: {ex.Message}", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
            if (!filledRaw)
                ArrayPool<byte>.Shared.Return(raw);
        }
    }

    private static bool IsCritical(uint type) => (type & 0x2000_0000) == 0;

    private static string TypeName(uint type) =>
        string.Create(4, type, static (chars, t) =>
        {
            for (int i = 0; i < 4; i++)
                chars[i] = (char)((t >> (24 - (8 * i))) & 0xFF);
        });

    private readonly record struct Chunk(uint Type, int DataOffset, int Length, int Next);
}
