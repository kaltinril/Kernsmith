using System.IO.Compression;

namespace KernSmith.Font.Brotli;

/// <summary>
/// The RFC 7932 static dictionary (Appendix A) and its 121 word transforms (Appendix B).
/// </summary>
/// <remarks>
/// The 122,784-byte dictionary ships as the embedded resource <c>BrotliDictionary.bin</c>,
/// raw-deflate compressed to roughly half its size, and is inflated once on first use.
/// <c>ManagedBrotliDecoderTests</c> pins the inflated bytes and the transform table to the
/// CRC-32 values the RFC publishes, so a corrupted resource or a mistyped transform fails a test.
/// </remarks>
internal static class BrotliStaticDictionary
{
    private const string ResourceName = "KernSmith.Font.Brotli.BrotliDictionary.bin";
    private const int DictionarySize = 122_784;

    /// <summary>Smallest and largest base-word length (RFC 7932 section 8).</summary>
    public const int MinWordLength = 4;

    /// <inheritdoc cref="MinWordLength"/>
    public const int MaxWordLength = 24;

    /// <summary>Number of word transforms.</summary>
    public const int TransformCount = 121;

    private const int TransformIdentity = 0;
    private const int TransformFermentFirst = 1;
    private const int TransformFermentAll = 2;
    private const int TransformOmitFirstBase = 2; // OmitFirstK = 2 + K, K = 1..9
    private const int TransformOmitLastBase = 11; // OmitLastK = 11 + K, K = 1..9

    /// <summary>NDBITS: log2 of the number of words of each length (RFC 7932 Appendix A).</summary>
    private static ReadOnlySpan<byte> SizeBitsByLength =>
    [
        0, 0, 0, 0, 10, 10, 11, 11, 10, 10,
        10, 10, 10, 9, 9, 8, 7, 7, 8, 7,
        7, 6, 6, 5, 5,
    ];

    private static readonly int[] s_offsetsByLength = BuildOffsets();
    private static byte[]? s_data;

    /// <summary>The 122,784 dictionary bytes.</summary>
    public static ReadOnlySpan<byte> Data => s_data ??= Load();

    /// <summary>NDBITS for a word length.</summary>
    public static int SizeBits(int length) => SizeBitsByLength[length];

    /// <summary>
    /// Writes the transformed dictionary word for a static-dictionary reference into
    /// <paramref name="destination"/> and returns the number of bytes written (at most 38).
    /// </summary>
    public static int TransformWord(Span<byte> destination, int length, int wordIndex, int transformId)
    {
        var word = Data.Slice(s_offsetsByLength[length] + wordIndex * length, length);
        var (prefix, type, suffix) = Transforms[transformId];

        var written = 0;
        prefix.CopyTo(destination);
        written += prefix.Length;

        var start = 0;
        var count = length;
        if (type > TransformOmitLastBase)
            count = Math.Max(0, length - (type - TransformOmitLastBase));
        else if (type > TransformOmitFirstBase)
        {
            var skip = Math.Min(length, type - TransformOmitFirstBase);
            start = skip;
            count = length - skip;
        }

        var body = destination.Slice(written, count);
        word.Slice(start, count).CopyTo(body);
        if (type == TransformFermentFirst)
        {
            if (count > 0)
                Ferment(body, 0);
        }
        else if (type == TransformFermentAll)
        {
            for (var i = 0; i < count;)
                i += Ferment(body, i);
        }

        written += count;
        suffix.CopyTo(destination.Slice(written));
        return written + suffix.Length;
    }

    /// <summary>
    /// Serializes the transform table the way RFC 7932 Appendix B defines its check value:
    /// prefix bytes, NUL, transform type, suffix bytes, NUL, for each transform in order.
    /// </summary>
    public static byte[] SerializeTransforms()
    {
        var bytes = new List<byte>();
        foreach (var (prefix, type, suffix) in Transforms)
        {
            bytes.AddRange(prefix);
            bytes.Add(0);
            bytes.Add((byte)type);
            bytes.AddRange(suffix);
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    /// <summary>The RFC's Ferment step: upper-cases one UTF-8 sequence, returns its length.</summary>
    private static int Ferment(Span<byte> word, int pos)
    {
        if (word[pos] < 192)
        {
            if (word[pos] >= 97 && word[pos] <= 122)
                word[pos] ^= 32;
            return 1;
        }

        if (word[pos] < 224)
        {
            if (pos + 1 < word.Length)
                word[pos + 1] ^= 32;
            return 2;
        }

        if (pos + 2 < word.Length)
            word[pos + 2] ^= 5;
        return 3;
    }

    private static int[] BuildOffsets()
    {
        var offsets = new int[MaxWordLength + 1];
        for (var length = 0; length < MaxWordLength; length++)
        {
            var words = length < MinWordLength ? 0 : 1 << SizeBitsByLength[length];
            offsets[length + 1] = offsets[length] + length * words;
        }

        return offsets;
    }

    private static byte[] Load()
    {
        using var resource = typeof(BrotliStaticDictionary).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing from the KernSmith assembly.");
        using var inflater = new DeflateStream(resource, CompressionMode.Decompress);

        var data = new byte[DictionarySize];
        var read = 0;
        int chunk;
        while (read < data.Length && (chunk = inflater.Read(data, read, data.Length - read)) > 0)
            read += chunk;

        if (read != DictionarySize)
            throw new InvalidOperationException($"Embedded Brotli dictionary inflated to {read} bytes, expected {DictionarySize}.");

        return data;
    }

    /// <summary>
    /// RFC 7932 Appendix B, in order. Type: 0 Identity, 1 FermentFirst, 2 FermentAll,
    /// 3..11 OmitFirst1..9, 12..20 OmitLast1..9.
    /// </summary>
    private static readonly (byte[] Prefix, int Type, byte[] Suffix)[] Transforms = BuildTransforms(
    [
        ("", 0, ""), // 0
        ("", 0, " "), // 1
        (" ", 0, " "), // 2
        ("", 3, ""), // 3
        ("", 1, " "), // 4
        ("", 0, " the "), // 5
        (" ", 0, ""), // 6
        ("s ", 0, " "), // 7
        ("", 0, " of "), // 8
        ("", 1, ""), // 9
        ("", 0, " and "), // 10
        ("", 4, ""), // 11
        ("", 12, ""), // 12
        (", ", 0, " "), // 13
        ("", 0, ", "), // 14
        (" ", 1, " "), // 15
        ("", 0, " in "), // 16
        ("", 0, " to "), // 17
        ("e ", 0, " "), // 18
        ("", 0, "\""), // 19
        ("", 0, "."), // 20
        ("", 0, "\">"), // 21
        ("", 0, "\n"), // 22
        ("", 14, ""), // 23
        ("", 0, "]"), // 24
        ("", 0, " for "), // 25
        ("", 5, ""), // 26
        ("", 13, ""), // 27
        ("", 0, " a "), // 28
        ("", 0, " that "), // 29
        (" ", 1, ""), // 30
        ("", 0, ". "), // 31
        (".", 0, ""), // 32
        (" ", 0, ", "), // 33
        ("", 6, ""), // 34
        ("", 0, " with "), // 35
        ("", 0, "'"), // 36
        ("", 0, " from "), // 37
        ("", 0, " by "), // 38
        ("", 7, ""), // 39
        ("", 8, ""), // 40
        (" the ", 0, ""), // 41
        ("", 15, ""), // 42
        ("", 0, ". The "), // 43
        ("", 2, ""), // 44
        ("", 0, " on "), // 45
        ("", 0, " as "), // 46
        ("", 0, " is "), // 47
        ("", 18, ""), // 48
        ("", 12, "ing "), // 49
        ("", 0, "\n\t"), // 50
        ("", 0, ":"), // 51
        (" ", 0, ". "), // 52
        ("", 0, "ed "), // 53
        ("", 11, ""), // 54
        ("", 9, ""), // 55
        ("", 17, ""), // 56
        ("", 0, "("), // 57
        ("", 1, ", "), // 58
        ("", 19, ""), // 59
        ("", 0, " at "), // 60
        ("", 0, "ly "), // 61
        (" the ", 0, " of "), // 62
        ("", 16, ""), // 63
        ("", 20, ""), // 64
        (" ", 1, ", "), // 65
        ("", 1, "\""), // 66
        (".", 0, "("), // 67
        ("", 2, " "), // 68
        ("", 1, "\">"), // 69
        ("", 0, "=\""), // 70
        (" ", 0, "."), // 71
        (".com/", 0, ""), // 72
        (" the ", 0, " of the "), // 73
        ("", 1, "'"), // 74
        ("", 0, ". This "), // 75
        ("", 0, ","), // 76
        (".", 0, " "), // 77
        ("", 1, "("), // 78
        ("", 1, "."), // 79
        ("", 0, " not "), // 80
        (" ", 0, "=\""), // 81
        ("", 0, "er "), // 82
        (" ", 2, " "), // 83
        ("", 0, "al "), // 84
        (" ", 2, ""), // 85
        ("", 0, "='"), // 86
        ("", 2, "\""), // 87
        ("", 1, ". "), // 88
        (" ", 0, "("), // 89
        ("", 0, "ful "), // 90
        (" ", 1, ". "), // 91
        ("", 0, "ive "), // 92
        ("", 0, "less "), // 93
        ("", 2, "'"), // 94
        ("", 0, "est "), // 95
        (" ", 1, "."), // 96
        ("", 2, "\">"), // 97
        (" ", 0, "='"), // 98
        ("", 1, ","), // 99
        ("", 0, "ize "), // 100
        ("", 2, "."), // 101
        (" ", 0, ""), // 102 — UTF-8 C2 A0 (no-break space)
        (" ", 0, ","), // 103
        ("", 1, "=\""), // 104
        ("", 2, "=\""), // 105
        ("", 0, "ous "), // 106
        ("", 2, ", "), // 107
        ("", 1, "='"), // 108
        (" ", 1, ","), // 109
        (" ", 2, "=\""), // 110
        (" ", 2, ", "), // 111
        ("", 2, ","), // 112
        ("", 2, "("), // 113
        ("", 2, ". "), // 114
        (" ", 2, "."), // 115
        ("", 2, "='"), // 116
        (" ", 2, ". "), // 117
        (" ", 1, "=\""), // 118
        (" ", 2, "='"), // 119
        (" ", 1, "='"), // 120
    ]);

    private static (byte[], int, byte[])[] BuildTransforms((string Prefix, int Type, string Suffix)[] rows)
    {
        var result = new (byte[], int, byte[])[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            result[i] = (System.Text.Encoding.UTF8.GetBytes(rows[i].Prefix), rows[i].Type, System.Text.Encoding.UTF8.GetBytes(rows[i].Suffix));
        return result;
    }
}
