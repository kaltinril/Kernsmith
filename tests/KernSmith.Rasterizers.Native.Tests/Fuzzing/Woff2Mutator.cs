using System.Buffers.Binary;
using System.IO.Compression;

namespace KernSmith.Rasterizers.Native.Tests.Fuzzing;

/// <summary>
/// Damages a WOFF2 file at two levels. The container — header, table directory, the Brotli
/// stream itself — exercises the WOFF2 reader and the Brotli decoder. The decompressed table
/// data — including the transformed <c>glyf</c>/<c>loca</c> and <c>hmtx</c> streams — is
/// damaged and re-compressed (with the BCL's Brotli encoder, test-side only), so the damage
/// gets past the Brotli stage and reaches the WOFF2 transforms, core's font parser and the
/// rasterizer.
/// </summary>
/// <remarks>Handles single-font WOFF2 files without metadata or private blocks, as the fixtures are.</remarks>
internal sealed class Woff2Mutator
{
    /// <summary>Pseudo-tag for the 48-byte header and the table directory.</summary>
    public const string Container = "<container>";

    /// <summary>Pseudo-tag for the compressed bytes, damaged without re-compression.</summary>
    public const string CompressedStream = "<brotli>";

    private const int HeaderSize = 48;

    private readonly byte[] _file;
    private readonly byte[] _stream;
    private readonly int _directoryEnd;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);

    public Woff2Mutator(byte[] woff2)
    {
        _file = woff2;
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(woff2.AsSpan(12));
        int compressedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(woff2.AsSpan(20));

        int pos = HeaderSize;
        int streamOffset = 0;
        for (int i = 0; i < numTables; i++)
        {
            byte flags = woff2[pos++];
            string tag;
            if ((flags & 0x3F) == 63)
            {
                tag = System.Text.Encoding.ASCII.GetString(woff2, pos, 4);
                pos += 4;
            }
            else
            {
                tag = KnownTags[flags & 0x3F];
            }

            int origLength = ReadBase128(woff2, ref pos);
            int version = flags >> 6;
            bool transformed = tag is "glyf" or "loca" ? version == 0 : version != 0;
            int length = transformed ? ReadBase128(woff2, ref pos) : origLength;
            _tables[tag] = (streamOffset, length);
            streamOffset += length;
        }

        _directoryEnd = pos;
        _stream = new byte[streamOffset];
        if (!BrotliDecoder.TryDecompress(woff2.AsSpan(_directoryEnd, compressedLength), _stream, out int written) || written != streamOffset)
            throw new InvalidOperationException("The WOFF2 fixture did not decompress to its directory's total length.");

        Regions = [Container, CompressedStream, .. _tables.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>The regions this mutator targets, in a stable order.</summary>
    public IReadOnlyList<string> Regions { get; }

    /// <summary>Returns a damaged copy of the file and a description that reproduces it.</summary>
    public byte[] Apply(string region, MutationKind kind, Random rng, out string description)
    {
        if (region == Container)
            return DamageInPlace(0, _directoryEnd, kind, rng, out description);

        if (region == CompressedStream)
            return DamageInPlace(_directoryEnd, _file.Length - _directoryEnd, kind, rng, out description);

        var (offset, length) = _tables[region];
        var stream = (byte[])_stream.Clone();

        if (kind == MutationKind.Truncate)
        {
            // The directory still promises the full stream; the decoder must notice it is short.
            int cut = offset + rng.Next(Math.Max(1, length));
            description = $"stream cut at {cut} of {stream.Length}";
            return Rebuild(stream[..cut]);
        }

        description = "stream " + ByteDamage.Apply(stream, kind, offset, length, rng,
            width => ByteDamage.Pick(offset, length, width, rng, hot: null));
        return Rebuild(stream);
    }

    private byte[] DamageInPlace(int start, int length, MutationKind kind, Random rng, out string description)
    {
        var file = (byte[])_file.Clone();
        if (kind == MutationKind.Truncate)
        {
            int cut = start + rng.Next(length);
            description = $"file cut at {cut}";
            return file[..cut];
        }

        description = ByteDamage.Apply(file, kind, start, length, rng,
            width => ByteDamage.Pick(start, length, width, rng, hot: null));
        return file;
    }

    /// <summary>The original header and directory around a re-compressed stream, lengths updated.</summary>
    private byte[] Rebuild(byte[] stream)
    {
        var compressed = new byte[BrotliEncoder.GetMaxCompressedLength(stream.Length)];
        if (!BrotliEncoder.TryCompress(stream, compressed, out int compressedLength, quality: 1, window: 22))
            throw new InvalidOperationException("Brotli re-compression failed.");

        var file = new byte[_directoryEnd + compressedLength];
        _file.AsSpan(0, _directoryEnd).CopyTo(file);
        compressed.AsSpan(0, compressedLength).CopyTo(file.AsSpan(_directoryEnd));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), (uint)file.Length);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), (uint)compressedLength);
        return file;
    }

    private static int ReadBase128(byte[] data, ref int pos)
    {
        int value = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b = data[pos++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
                return value;
        }

        throw new InvalidOperationException("Bad UIntBase128 in the fixture.");
    }

    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep",
        "CFF ", "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE",
        "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt",
        "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar",
        "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    ];
}
