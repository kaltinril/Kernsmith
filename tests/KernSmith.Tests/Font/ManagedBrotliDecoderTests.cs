using System.IO.Compression;
using System.Text;
using KernSmith.Font.Brotli;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// The managed RFC 7932 Brotli decoder that backs WOFF2. Vectors are produced at test time by
/// the BCL <see cref="BrotliEncoder"/> (the reference C encoder underneath), so every quality
/// level, window size and data shape exercises a real encoder's choices — and the decoder must
/// reproduce the input byte for byte.
/// </summary>
public class ManagedBrotliDecoderTests
{
    // ──────────────────────────────────────────────
    // Spec data integrity (RFC 7932 check values)
    // ──────────────────────────────────────────────

    [Fact]
    public void StaticDictionary_MatchesRfcLengthAndCrc32()
    {
        var dictionary = BrotliStaticDictionary.Data;

        dictionary.Length.ShouldBe(122_784);
        Crc32(dictionary).ShouldBe(0x5136cb04u, "RFC 7932 Appendix A gives the dictionary CRC-32 as 0x5136cb04");
    }

    [Fact]
    public void WordTransforms_SerializeToRfcLengthAndCrc32()
    {
        var serialized = BrotliStaticDictionary.SerializeTransforms();

        serialized.Length.ShouldBe(648);
        Crc32(serialized).ShouldBe(0x3d965f81u, "RFC 7932 Appendix B gives the transform list CRC-32 as 0x3d965f81");
    }

    [Fact]
    public void ContextLookupTables_MatchRfcCrc32()
    {
        BrotliContext.Lut0.Length.ShouldBe(256);
        BrotliContext.Lut1.Length.ShouldBe(256);
        BrotliContext.Lut2.Length.ShouldBe(256);
        Crc32(BrotliContext.Lut0).ShouldBe(0x8e91efb7u);
        Crc32(BrotliContext.Lut1).ShouldBe(0xd01a32f4u);
        Crc32(BrotliContext.Lut2).ShouldBe(0x0dd7a0d6u);
    }

    [Fact]
    public void ContextSplitLookup_AgreesWithLiteralContextId_ForEveryModeAndByte()
    {
        // The decode loop reads the context ID as two independent lookups ORed together;
        // they must reproduce the RFC formula for every mode and every pair of previous bytes.
        var split = BrotliContext.SplitLookup;
        split.Length.ShouldBe(4 * 512);
        for (var mode = 0; mode < 4; mode++)
        {
            for (var p1 = 0; p1 < 256; p1++)
            {
                for (var p2 = 0; p2 < 256; p2++)
                {
                    var expected = BrotliContext.LiteralContextId(mode, (byte)p1, (byte)p2);
                    var actual = split[(mode << 9) + p1] | split[(mode << 9) + 256 + p2];
                    if (actual != expected)
                        actual.ShouldBe(expected, $"mode {mode}, p1 {p1}, p2 {p2}");
                }
            }
        }
    }

    // ──────────────────────────────────────────────
    // Hand-built streams
    // ──────────────────────────────────────────────

    [Fact]
    public void Decompress_EmptyStream_ReturnsNoBytes()
    {
        // WBITS=16 ('0'), ISLAST=1, ISLASTEMPTY=1 -> 0b110.
        ManagedBrotliDecoder.Decompress(new byte[] { 0x06 }, 100).ShouldBeEmpty();
    }

    [Fact]
    public void Decompress_UncompressedMetaBlock_CopiesBytes()
    {
        // WBITS=16, ISLAST=0, MNIBBLES=4, MLEN-1=2, ISUNCOMPRESSED=1, pad, "abc", then an
        // empty last meta-block.
        var stream = new byte[] { 0x20, 0x00, 0x10, (byte)'a', (byte)'b', (byte)'c', 0x03 };

        Encoding.ASCII.GetString(ManagedBrotliDecoder.Decompress(stream, 100)).ShouldBe("abc");
    }

    [Fact]
    public void Decompress_MetadataMetaBlock_IsSkipped()
    {
        // WBITS=16, ISLAST=0, MNIBBLES=0 (metadata), reserved 0, MSKIPBYTES=1, MSKIPLEN-1=2,
        // pad, three metadata bytes, then an empty last meta-block.
        var stream = new byte[] { 0x2C, 0x01, (byte)'x', (byte)'y', (byte)'z', 0x03 };

        ManagedBrotliDecoder.Decompress(stream, 100).ShouldBeEmpty();
    }

    [Fact]
    public void Decompress_LastMetaBlockIsMetadata_EndsTheStream()
    {
        // WBITS=16, ISLAST=1, ISLASTEMPTY=0, MNIBBLES=0 (metadata), reserved 0, MSKIPBYTES=0.
        // RFC 7932 section 10: a metadata block "continues" to the loop test, which ends on ISLAST.
        ManagedBrotliDecoder.Decompress(new byte[] { 0x1A }, 100).ShouldBeEmpty();
    }

    // ──────────────────────────────────────────────
    // Round trips against the BCL encoder
    // ──────────────────────────────────────────────

    public static TheoryData<string, int, int> RoundTripCases()
    {
        var data = new TheoryData<string, int, int>();
        int[] qualities = [0, 1, 2, 4, 5, 9, 11];
        int[] windows = [10, 16, 22, 24];
        foreach (var name in SampleNames)
        {
            foreach (var quality in qualities)
            {
                foreach (var window in windows)
                {
                    // Quality 10/11 on the multi-megabyte samples takes seconds per case;
                    // the big samples exist to cover window wrap and multi-meta-block streams,
                    // which the faster qualities exercise just as well.
                    if (quality >= 10 && name is "mixed-3mb" or "zeros-1mb")
                        continue;
                    data.Add(name, quality, window);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Decompress_BclEncodedSample_RoundTripsExactly(string sample, int quality, int window)
    {
        var original = Sample(sample);
        var compressed = BclCompress(original, quality, window);

        var decoded = ManagedBrotliDecoder.Decompress(compressed, original.Length);

        decoded.Length.ShouldBe(original.Length);
        decoded.AsSpan().SequenceEqual(original).ShouldBeTrue(
            $"{sample} q{quality} w{window}: first difference at {FirstDifference(decoded, original)}");
    }

    [Fact]
    public void Decompress_MoreThan16MiB_SpansSeveralMetaBlocks()
    {
        // MLEN is at most 2^24 per meta-block, so a 17 MiB payload cannot fit in one.
        var original = new byte[17 * 1024 * 1024];
        var random = new Random(178);
        for (var i = 0; i < original.Length; i += 4096)
        {
            var chunk = original.AsSpan(i, Math.Min(4096, original.Length - i));
            if ((i / 4096) % 3 == 0)
                random.NextBytes(chunk);
            else
                chunk.Fill((byte)(i / 4096));
        }

        var compressed = BclCompress(original, 1, 24);
        var decoded = ManagedBrotliDecoder.Decompress(compressed, original.Length);

        decoded.AsSpan().SequenceEqual(original).ShouldBeTrue(
            $"first difference at {FirstDifference(decoded, original)}");
    }

    [Fact]
    public void Decompress_StaticDictionaryText_UsesDictionaryAndRoundTrips()
    {
        // Short, dictionary-heavy text: at quality 11 the encoder has no earlier occurrence
        // to copy from, so a compressed size well under the input proves dictionary
        // references (with transforms such as capitalisation and suffixes) are in play.
        var original = Encoding.UTF8.GetBytes(
            "The quick brown fox jumps over the lazy dog. Information about the University " +
            "of California, and the International Organization for Standardization.");
        var compressed = BclCompress(original, 11, 22);
        compressed.Length.ShouldBeLessThan(original.Length * 3 / 4);

        ManagedBrotliDecoder.Decompress(compressed, original.Length).ShouldBe(original);
    }

    // ──────────────────────────────────────────────
    // Malformed / hostile input
    // ──────────────────────────────────────────────

    [Fact]
    public void Decompress_EmptyInput_Throws()
    {
        Should.Throw<FontParsingException>(() => ManagedBrotliDecoder.Decompress(ReadOnlySpan<byte>.Empty, 100));
    }

    [Fact]
    public void Decompress_InvalidWindowBitsPattern_Throws()
    {
        // 0010001 is the one reserved WBITS pattern (RFC 7932 section 9.1).
        var ex = Should.Throw<FontParsingException>(() => ManagedBrotliDecoder.Decompress(new byte[] { 0x11, 0x00 }, 100));
        ex.Message.ShouldContain("window", Case.Insensitive);
    }

    [Fact]
    public void Decompress_OutputLargerThanLimit_Throws()
    {
        var compressed = BclCompress(new byte[1_000_000], 5, 22);

        var ex = Should.Throw<FontParsingException>(() => ManagedBrotliDecoder.Decompress(compressed, 1000));
        ex.Message.ShouldContain("limit", Case.Insensitive);
    }

    [Fact]
    public void Decompress_EveryTruncation_ThrowsFontParsingException()
    {
        var original = Sample("text");
        var compressed = BclCompress(original.AsSpan(0, 4000).ToArray(), 11, 22);

        for (var length = 0; length < compressed.Length; length++)
        {
            var truncated = compressed.AsSpan(0, length).ToArray();
            Should.Throw<FontParsingException>(
                () => ManagedBrotliDecoder.Decompress(truncated, original.Length),
                $"a stream truncated to {length} of {compressed.Length} bytes must be rejected");
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Decompress_BitFlippedStreams_NeverThrowAnythingButFontParsingException()
    {
        await Task.Run(() =>
        {
            var original = Sample("text").AsSpan(0, 3000).ToArray();
            var compressed = BclCompress(original, 9, 18);

            for (var bit = 0; bit < compressed.Length * 8; bit++)
            {
                var corrupted = (byte[])compressed.Clone();
                corrupted[bit / 8] ^= (byte)(1 << (bit % 8));
                AssertDecodesOrRejectsCleanly(corrupted, original.Length * 4, $"bit {bit} flipped");
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 60_000)]
    public async Task Decompress_RandomGarbage_NeverThrowsAnythingButFontParsingException()
    {
        await Task.Run(() =>
        {
            var random = new Random(7932);
            for (var i = 0; i < 20_000; i++)
            {
                var garbage = new byte[random.Next(1, 200)];
                random.NextBytes(garbage);
                AssertDecodesOrRejectsCleanly(garbage, 1 << 20, $"garbage case {i}");
            }
        }, TestContext.Current.CancellationToken);
    }

    private static void AssertDecodesOrRejectsCleanly(byte[] input, int limit, string what)
    {
        try
        {
            ManagedBrotliDecoder.Decompress(input, limit).Length.ShouldBeLessThanOrEqualTo(limit, what);
        }
        catch (FontParsingException)
        {
            // Clean rejection is the expected outcome for corrupt input.
        }
        catch (Exception ex)
        {
            throw new ShouldAssertException($"{what}: expected FontParsingException, got {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static readonly string[] SampleNames =
        ["empty", "one-byte", "all-bytes", "random-64k", "zeros-1mb", "pattern", "text", "font", "mixed-3mb"];

    private static readonly Dictionary<string, byte[]> s_samples = new();

    private static byte[] Sample(string name)
    {
        lock (s_samples)
        {
            if (!s_samples.TryGetValue(name, out var bytes))
            {
                bytes = BuildSample(name);
                s_samples[name] = bytes;
            }

            return bytes;
        }
    }

    private static byte[] BuildSample(string name)
    {
        var random = new Random(name.Length * 31 + name[0]);
        switch (name)
        {
            case "empty":
                return [];
            case "one-byte":
                return [0x5A];
            case "all-bytes":
                return Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
            case "random-64k":
                {
                    var bytes = new byte[65_536];
                    random.NextBytes(bytes);
                    return bytes;
                }
            case "zeros-1mb":
                return new byte[1 << 20];
            case "pattern":
                return Enumerable.Range(0, 200_000).Select(i => (byte)((i * 7) % 13 + (i / 1000))).ToArray();
            case "text":
                {
                    string[] words =
                    [
                        "the", "of", "and", "to", "in", "for", "is", "on", "that", "by", "this", "with",
                    "you", "it", "not", "or", "be", "are", "from", "at", "as", "your", "all", "have",
                    "new", "more", "an", "was", "we", "will", "home", "can", "us", "about", "if",
                    "page", "my", "has", "search", "free", "but", "our", "one", "other", "do", "no",
                    "information", "time", "they", "site", "he", "up", "may", "what", "which",
                    "their", "news", "out", "use", "any", "there", "see", "only", "so", "his",
                    "when", "contact", "here", "business", "who", "web", "also", "now", "help",
                    "get", "view", "online", "first", "been", "would", "how", "were", "me",
                    "services", "some", "these", "click", "its", "like", "service", "than",
                    "find", "price", "date", "back", "top", "people", "had", "list", "name",
                    "université", "международный", "<div class=\"", "\">", "http://www.", ".com/",
                ];
                    var sb = new StringBuilder();
                    while (sb.Length < 150_000)
                    {
                        var word = words[random.Next(words.Length)];
                        if (random.Next(5) == 0)
                            word = char.ToUpperInvariant(word[0]) + word[1..];
                        else if (random.Next(9) == 0)
                            word = word.ToUpperInvariant();
                        sb.Append(word);
                        sb.Append(random.Next(12) switch { 0 => ", ", 1 => ". ", 2 => "\n", 3 => "ing ", _ => " " });
                    }

                    return Encoding.UTF8.GetBytes(sb.ToString());
                }
            case "font":
                return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));
            case "mixed-3mb":
                {
                    var bytes = new byte[3 * 1024 * 1024 + 123];
                    var text = Sample("text");
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        var length = Math.Min(random.Next(1, 70_000), bytes.Length - offset);
                        var chunk = bytes.AsSpan(offset, length);
                        switch (random.Next(4))
                        {
                            case 0: random.NextBytes(chunk); break;
                            case 1: chunk.Fill((byte)random.Next(256)); break;
                            case 2: text.AsSpan(0, Math.Min(length, text.Length)).CopyTo(chunk); break;
                            default:
                                // Re-use earlier output from far back so distances exceed small windows.
                                if (offset > length)
                                    bytes.AsSpan(random.Next(offset - length), length).CopyTo(chunk);
                                break;
                        }

                        offset += length;
                    }

                    return bytes;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    private static byte[] BclCompress(byte[] data, int quality, int window)
    {
        var destination = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length) + 64];
        BrotliEncoder.TryCompress(data, destination, out var written, quality, window)
            .ShouldBeTrue($"BCL Brotli encoder failed for q{quality} w{window}");
        return destination.AsSpan(0, written).ToArray();
    }

    private static int FirstDifference(byte[] actual, byte[] expected)
    {
        var length = Math.Min(actual.Length, expected.Length);
        for (var i = 0; i < length; i++)
        {
            if (actual[i] != expected[i])
                return i;
        }

        return actual.Length == expected.Length ? -1 : length;
    }

    /// <summary>CRC-32 exactly as RFC 7932 Appendix C defines it.</summary>
    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        const uint Poly = 0xedb88320u;
        var crc = 0xffffffffu;
        foreach (var b in data)
        {
            var c = (crc ^ b) & 0xff;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? Poly ^ (c >> 1) : c >> 1;
            crc = c ^ (crc >> 8);
        }

        return crc ^ 0xffffffffu;
    }
}
