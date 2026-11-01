namespace KernSmith.Font;

/// <summary>
/// The character codes of a symbol-encoded font — one whose only usable <c>cmap</c> subtable is
/// the Windows symbol one (platform 3, encoding 0), such as Wingdings, Webdings and Symbol. Such
/// a font maps its glyphs at U+F020-U+F0FF, one per byte of an 8-bit code; KernSmith also
/// exposes each glyph at that byte decoded as Windows-1252, as BMFont's SYMBOL charset does
/// (byte 0x41 is U+0041, byte 0x80 is U+20AC). Backends use it to reach a symbol glyph from
/// either code.
/// </summary>
public static class SymbolEncoding
{
    /// <summary>First of the codes a symbol <c>cmap</c> maps its glyphs at (byte 0x00).</summary>
    public const int SymbolBase = 0xF000;

    // Windows-1252 bytes 0x80-0x9F. The five the code page leaves undefined (0x81, 0x8D, 0x8F,
    // 0x90, 0x9D) decode to themselves, as Windows' MultiByteToWideChar decodes them.
    private static readonly ushort[] Cp1252High =
    [
        0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
        0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178,
    ];

    private static readonly Dictionary<int, int> Cp1252Bytes = BuildReverse();

    /// <summary>
    /// The Unicode character a symbol code (U+F000-U+F0FF) also appears at: its byte decoded as
    /// Windows-1252. Other values are returned unchanged.
    /// </summary>
    /// <param name="symbolCode">A code from a symbol <c>cmap</c>.</param>
    public static int ToUnicode(int symbolCode)
    {
        if (symbolCode is < SymbolBase or > SymbolBase + 0xFF)
            return symbolCode;
        int b = symbolCode - SymbolBase;
        return b is >= 0x80 and <= 0x9F ? Cp1252High[b - 0x80] : b;
    }

    /// <summary>
    /// The symbol code (U+F000-U+F0FF) a symbol font maps <paramref name="codepoint"/>'s glyph
    /// at — its Windows-1252 byte plus 0xF000 — or null when Windows-1252 has no byte for it.
    /// </summary>
    /// <param name="codepoint">A Unicode codepoint.</param>
    public static int? ToSymbolCode(int codepoint) =>
        Cp1252Bytes.TryGetValue(codepoint, out var b) ? SymbolBase + b : null;

    /// <summary>
    /// True when the font's <c>cmap</c> has a Windows symbol subtable (3,0) and no Unicode one
    /// (platform 0, or platform 3 encoding 1 or 10) — the fonts whose glyphs sit at U+F0xx. Any
    /// structure it cannot read gives false.
    /// </summary>
    /// <param name="fontData">TTF/OTF (or TTC) bytes.</param>
    /// <param name="faceIndex">The face of a collection.</param>
    public static bool IsSymbolFont(ReadOnlySpan<byte> fontData, int faceIndex = 0)
    {
        int face = 0;
        if (fontData.Length >= 12 && U32(fontData, 0) == 0x74746366) // 'ttcf'
        {
            if (faceIndex < 0 || 12L + (4L * (faceIndex + 1)) > fontData.Length)
                return false;
            long offset = U32(fontData, 12 + (4 * faceIndex));
            if (offset > fontData.Length - 12)
                return false;
            face = (int)offset;
        }

        if (fontData.Length < face + 12)
            return false;
        int numTables = U16(fontData, face + 4);
        for (int i = 0; i < numTables; i++)
        {
            int record = face + 12 + (16 * i);
            if (record + 16 > fontData.Length)
                return false;
            if (U32(fontData, record) != 0x636D6170) // 'cmap'
                continue;

            long cmap = U32(fontData, record + 8);
            long length = U32(fontData, record + 12);
            if (cmap + length > fontData.Length || length < 4)
                return false;
            int numEncodings = U16(fontData, (int)cmap + 2);
            if (4L + (8L * numEncodings) > length)
                return false;

            bool symbol = false, unicode = false;
            for (int e = 0; e < numEncodings; e++)
            {
                int platform = U16(fontData, (int)cmap + 4 + (8 * e));
                int encoding = U16(fontData, (int)cmap + 6 + (8 * e));
                unicode |= platform == 0 || (platform == 3 && encoding is 1 or 10);
                symbol |= platform == 3 && encoding == 0;
            }

            return symbol && !unicode;
        }

        return false;
    }

    private static int U16(ReadOnlySpan<byte> d, int at) => (d[at] << 8) | d[at + 1];

    private static uint U32(ReadOnlySpan<byte> d, int at) => ((uint)U16(d, at) << 16) | (uint)U16(d, at + 2);

    private static Dictionary<int, int> BuildReverse()
    {
        var map = new Dictionary<int, int>(256);
        for (int b = 0; b < 0x100; b++)
            map[ToUnicode(SymbolBase + b)] = b;
        return map;
    }
}
