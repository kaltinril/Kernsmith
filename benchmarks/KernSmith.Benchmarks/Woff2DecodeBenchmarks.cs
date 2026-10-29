using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using KernSmith;

// WOFF2's Brotli stage: KernSmith's managed decoder against the BCL's native one on the same
// stream (Roboto-Regular.woff2), plus the whole WOFF2 read next to the plain TTF read so the
// rest of the WOFF2 rebuild is visible too (Phase 177).
// Run: dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *Woff2Decode*

[MemoryDiagnoser]
[BenchmarkCategory("Woff2")]
public class Woff2DecodeBenchmarks
{
    private delegate byte[] BrotliDecompress(ReadOnlySpan<byte> input, int maxOutputLength);

    private byte[] _woff2 = null!;
    private byte[] _ttf = null!;
    private byte[] _stream = null!;
    private byte[] _bclOutput = null!;
    private int _decodedLength;
    private BrotliDecompress _managed = null!;

    [GlobalSetup]
    public void Setup()
    {
        _woff2 = BenchmarkFixtures.ReadFont("Roboto-Regular.woff2");
        _ttf = BenchmarkFixtures.ReadFont("Roboto-Regular.ttf");
        _stream = BrotliStream(_woff2);

        // The decoder is internal to core; bind it once rather than widen its visibility.
        var decoder = typeof(BmFont).Assembly.GetType("KernSmith.Font.Brotli.ManagedBrotliDecoder", throwOnError: true)!;
        _managed = decoder.GetMethod("Decompress", BindingFlags.Public | BindingFlags.Static)!.CreateDelegate<BrotliDecompress>();

        _decodedLength = _managed(_stream, int.MaxValue).Length;
        _bclOutput = new byte[_decodedLength];
        if (!BrotliDecoder.TryDecompress(_stream, _bclOutput, out int written) || written != _decodedLength)
            throw new InvalidOperationException("The BCL decoder disagrees with the managed decoder on the stream length.");
    }

    [Benchmark(Description = "Brotli: managed decoder")]
    public int ManagedBrotli() => _managed(_stream, _decodedLength).Length;

    [Benchmark(Description = "Brotli: BCL (native) decoder", Baseline = true)]
    public int BclBrotli()
    {
        BrotliDecoder.TryDecompress(_stream, _bclOutput, out int written);
        return written;
    }

    [Benchmark(Description = "ReadFontInfo WOFF2")]
    public int ReadWoff2() => BmFont.ReadFontInfo(_woff2).AvailableCodepoints.Count;

    [Benchmark(Description = "ReadFontInfo TTF")]
    public int ReadTtf() => BmFont.ReadFontInfo(_ttf).AvailableCodepoints.Count;

    /// <summary>
    /// The compressed table stream of a single-font WOFF2: it starts right after the table
    /// directory and runs for the header's totalCompressedSize (W3C WOFF2 sections 4-5).
    /// </summary>
    private static byte[] BrotliStream(byte[] woff2)
    {
        const int HeaderSize = 48;
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(woff2.AsSpan(12));
        int compressedSize = (int)BinaryPrimitives.ReadUInt32BigEndian(woff2.AsSpan(20));

        int position = HeaderSize;
        for (int i = 0; i < numTables; i++)
        {
            byte flags = woff2[position++];
            int tagIndex = flags & 0x3F;
            if (tagIndex == 0x3F)
                position += 4; // explicit tag

            ReadBase128(woff2, ref position); // origLength

            // glyf (10) and loca (11) are transformed at version 0; every other table at a non-zero version.
            int version = flags >> 6;
            bool transformed = tagIndex is 10 or 11 ? version == 0 : version != 0;
            if (transformed)
                ReadBase128(woff2, ref position); // transformLength
        }

        return woff2.AsSpan(position, compressedSize).ToArray();
    }

    private static uint ReadBase128(byte[] data, ref int position)
    {
        uint value = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b = data[position++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return value;
        }

        throw new InvalidOperationException("Malformed UIntBase128.");
    }
}
