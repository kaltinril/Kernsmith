using System.Globalization;
using System.Text;

namespace KernSmith.Tests.Rasterizer.StbStructure;

/// <summary>
/// Minimal sfnts built from scratch: just the tables the StbTrueType backend reads
/// (<c>cmap</c>, <c>head</c>, <c>hhea</c>, <c>hmtx</c>, <c>maxp</c> and <c>CFF </c>), so a test
/// controls every byte of a CFF structure — including the CID-keyed FDArray/FDSelect path no
/// committed fixture exercises.
/// </summary>
internal static class SyntheticFonts
{
    /// <summary>A closed 100-unit square.</summary>
    public const string Square = "0 0 rmoveto 100 0 rlineto 0 100 rlineto -100 0 rlineto endchar";

    /// <summary>Glyph 1 is mapped from this codepoint, glyph 2 from the next, and so on.</summary>
    public const int FirstCodepoint = 0x41;

    /// <summary>Absolute positions (from the file start) of the structures a test may damage.</summary>
    internal sealed class CffLayout
    {
        public int Cff;
        public int CffLength;
        public int NameIndex;
        public int TopDictIndex;
        public int TopDict;
        public int StringIndex;
        public int GlobalSubrIndex;
        public int CharStringsIndex;
        public int Private;
        public int LocalSubrIndex;
        public int FdArrayIndex;
        public int FdSelect;
    }

    /// <summary>
    /// An OpenType CFF font. Glyph 0 is <c>endchar</c> (.notdef); <paramref name="charstrings"/>
    /// become glyphs 1.., mapped from <see cref="FirstCodepoint"/> up. With <paramref name="cid"/>
    /// the font is CID-keyed: one Font DICT carries the Private DICT and local subrs, and
    /// FDSelect (format 3 unless <paramref name="fdSelect"/> is given) maps every glyph to it.
    /// </summary>
    public static byte[] Cff(
        string[] charstrings,
        string[]? globalSubrs = null,
        string[]? localSubrs = null,
        bool cid = false,
        byte[]? fdSelect = null,
        int? maxpNumGlyphs = null) =>
        Cff(out _, charstrings, globalSubrs, localSubrs, cid, fdSelect, maxpNumGlyphs);

    public static byte[] Cff(
        out CffLayout layout,
        string[] charstrings,
        string[]? globalSubrs = null,
        string[]? localSubrs = null,
        bool cid = false,
        byte[]? fdSelect = null,
        int? maxpNumGlyphs = null)
    {
        var glyphs = new List<byte[]> { Charstring("endchar") };
        glyphs.AddRange(charstrings.Select(Charstring));
        int numGlyphs = glyphs.Count;

        byte[] cff = CffTable(glyphs, (globalSubrs ?? []).Select(Charstring).ToList(),
            localSubrs?.Select(Charstring).ToList(), cid,
            fdSelect ?? [3, .. Be.U16(1), .. Be.U16(0), 0, .. Be.U16(numGlyphs)], out layout);

        int maxpGlyphs = maxpNumGlyphs ?? numGlyphs;
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["CFF "] = cff,
            ["cmap"] = Cmap4(numGlyphs - 1),
            ["head"] = Head(),
            ["hhea"] = Hhea(numberOfHMetrics: Math.Min(numGlyphs, maxpGlyphs)),
            ["hmtx"] = new byte[4 * Math.Max(numGlyphs, maxpGlyphs)],
            ["maxp"] = Be.Cat(Be.U32(0x00005000), Be.U16(maxpGlyphs)),
        };

        byte[] font = Sfnt(0x4F54544F, tables, out var offsets); // 'OTTO'
        int shift = offsets["CFF "];
        layout.Cff += shift;
        layout.NameIndex += shift;
        layout.TopDictIndex += shift;
        layout.TopDict += shift;
        layout.StringIndex += shift;
        layout.GlobalSubrIndex += shift;
        layout.CharStringsIndex += shift;
        layout.Private += shift;
        layout.LocalSubrIndex += shift;
        layout.FdArrayIndex += shift;
        layout.FdSelect += shift;
        return font;
    }

    /// <summary>An sfnt from whole tables, 4-byte aligned, checksums left at zero.</summary>
    public static byte[] Sfnt(uint version, IDictionary<string, byte[]> tables, out Dictionary<string, int> offsets)
    {
        int numTables = tables.Count;
        int pos = 12 + (16 * numTables);
        offsets = [];
        var body = new List<byte>();
        var directory = new List<byte>();
        foreach (var (tag, data) in tables)
        {
            offsets[tag] = pos + body.Count;
            directory.AddRange(Encoding.ASCII.GetBytes(tag));
            directory.AddRange(Be.U32(0));
            directory.AddRange(Be.U32(pos + body.Count));
            directory.AddRange(Be.U32(data.Length));
            body.AddRange(data);
            while (body.Count % 4 != 0)
                body.Add(0);
        }

        return Be.Cat(Be.U32(version), Be.U16(numTables), Be.U16(0), Be.U16(0), Be.U16(0), [.. directory], [.. body]);
    }

    public static byte[] Head(int unitsPerEm = 1000, int indexToLocFormat = 0)
    {
        var head = new byte[54];
        Be.U32(0x00010000).CopyTo(head, 0);
        Be.U32(0x5F0F3CF5).CopyTo(head, 12);
        Be.U16(unitsPerEm).CopyTo(head, 18);
        Be.U16(indexToLocFormat).CopyTo(head, 50);
        return head;
    }

    public static byte[] Hhea(int numberOfHMetrics)
    {
        var hhea = new byte[36];
        Be.U32(0x00010000).CopyTo(hhea, 0);
        Be.U16(800).CopyTo(hhea, 4);
        Be.U16(-200).CopyTo(hhea, 6);
        Be.U16(numberOfHMetrics).CopyTo(hhea, 34);
        return hhea;
    }

    /// <summary>A <c>cmap</c> with one (3,1) format-4 subtable mapping <paramref name="count"/> codepoints from <see cref="FirstCodepoint"/> to glyphs 1...</summary>
    public static byte[] Cmap4(int count) =>
        CmapWith(3, 1, Format4([(FirstCodepoint, FirstCodepoint + count - 1, 1 - FirstCodepoint)]));

    /// <summary>A <c>cmap</c> header with one encoding record pointing at <paramref name="subtable"/>.</summary>
    public static byte[] CmapWith(int platform, int encoding, byte[] subtable) =>
        Be.Cat(Be.U16(0), Be.U16(1), Be.U16(platform), Be.U16(encoding), Be.U32(12), subtable);

    /// <summary>
    /// A format-4 subtable from (start, end, idDelta) segments (idRangeOffset 0), with the
    /// closing 0xFFFF segment appended and the search fields computed as the spec defines them.
    /// </summary>
    public static byte[] Format4((int Start, int End, int Delta)[] segments)
    {
        var segs = segments.Append((0xFFFF, 0xFFFF, 1)).ToArray();
        int segCount = segs.Length;
        int pow = 1;
        while (pow * 2 <= segCount)
            pow *= 2;
        int searchRange = 2 * pow;
        int entrySelector = (int)Math.Log2(pow);
        int length = 16 + (8 * segCount);
        return Be.Cat(
            Be.U16(4), Be.U16(length), Be.U16(0), Be.U16(2 * segCount), Be.U16(searchRange),
            Be.U16(entrySelector), Be.U16((2 * segCount) - searchRange),
            segs.SelectMany(s => Be.U16(s.Item2)).ToArray(), Be.U16(0),
            segs.SelectMany(s => Be.U16(s.Item1)).ToArray(),
            segs.SelectMany(s => Be.U16(s.Item3)).ToArray(),
            segs.SelectMany(_ => Be.U16(0)).ToArray());
    }

    /// <summary>A format-12 (or 13) subtable from (start, end, startGlyph) groups.</summary>
    public static byte[] Format12((long Start, long End, long Glyph)[] groups, int format = 12) =>
        Be.Cat(
            Be.U16(format), Be.U16(0), Be.U32(16 + (12 * groups.Length)), Be.U32(0), Be.U32(groups.Length),
            groups.SelectMany(g => Be.Cat(Be.U32(g.Start), Be.U32(g.End), Be.U32(g.Glyph))).ToArray());

    /// <summary>A format-6 subtable mapping <paramref name="glyphs"/> from <paramref name="first"/> on.</summary>
    public static byte[] Format6(int first, params int[] glyphs) =>
        Be.Cat(Be.U16(6), Be.U16(10 + (2 * glyphs.Length)), Be.U16(0), Be.U16(first), Be.U16(glyphs.Length),
            glyphs.SelectMany(Be.U16).ToArray());

    /// <summary>A format-0 subtable (256 one-byte glyph ids).</summary>
    public static byte[] Format0(params (int Code, int Glyph)[] map)
    {
        var ids = new byte[256];
        foreach (var (code, glyph) in map)
            ids[code] = (byte)glyph;
        return Be.Cat(Be.U16(0), Be.U16(262), Be.U16(0), ids);
    }

    // ------------------------------------------------------------------ CFF

    private static byte[] CffTable(
        List<byte[]> charstrings, List<byte[]> globalSubrs, List<byte[]>? localSubrs, bool cid, byte[] fdSelect,
        out CffLayout layout)
    {
        layout = new CffLayout();
        byte[] header = [1, 0, 4, 4];
        byte[] nameIndex = Index([Encoding.ASCII.GetBytes("Synthetic")]);
        byte[] stringIndex = Index([]);
        byte[] gsubrIndex = Index(globalSubrs);
        byte[] charStringsIndex = Index(charstrings);
        byte[] localSubrIndex = localSubrs is null ? [] : Index(localSubrs);

        // Private DICT: Subrs (19) points just past the dict itself; otherwise defaultWidthX 0.
        byte[] privateDict = localSubrs is null ? [0x8B, 20] : [.. Int5(6), 19];

        int topDictLength = cid ? (17 + 6 + 7 + 7) : (6 + 11);
        int topDictIndexLength = Index([new byte[topDictLength]]).Length;

        int pos = header.Length;
        layout.NameIndex = pos;
        pos += nameIndex.Length;
        layout.TopDictIndex = pos;
        layout.TopDict = pos + 2 + 1 + 8;
        pos += topDictIndexLength;
        layout.StringIndex = pos;
        pos += stringIndex.Length;
        layout.GlobalSubrIndex = pos;
        pos += gsubrIndex.Length;
        layout.CharStringsIndex = pos;
        pos += charStringsIndex.Length;

        byte[] tail;
        byte[] topDict;
        if (!cid)
        {
            layout.Private = pos;
            layout.LocalSubrIndex = pos + privateDict.Length;
            topDict = [.. Int5(layout.CharStringsIndex), 17, .. Int5(privateDict.Length), .. Int5(layout.Private), 18];
            tail = [.. privateDict, .. localSubrIndex];
        }
        else
        {
            int fdDictLength = 11;
            int fdArrayLength = Index([new byte[fdDictLength]]).Length;
            layout.FdArrayIndex = pos;
            layout.Private = pos + fdArrayLength;
            layout.LocalSubrIndex = layout.Private + privateDict.Length;
            layout.FdSelect = layout.LocalSubrIndex + localSubrIndex.Length;
            byte[] fdDict = [.. Int5(privateDict.Length), .. Int5(layout.Private), 18];
            topDict =
            [
                .. Int5(391), .. Int5(392), .. Int5(0), 12, 30,
                .. Int5(layout.CharStringsIndex), 17,
                .. Int5(layout.FdArrayIndex), 12, 36,
                .. Int5(layout.FdSelect), 12, 37,
            ];
            tail = [.. Index([fdDict]), .. privateDict, .. localSubrIndex, .. fdSelect];
        }

        if (topDict.Length != topDictLength)
            throw new InvalidOperationException("Top DICT length mismatch.");

        byte[] table = [.. header, .. nameIndex, .. Index([topDict]), .. stringIndex, .. gsubrIndex, .. charStringsIndex, .. tail];
        layout.CffLength = table.Length;
        return table;
    }

    /// <summary>A CFF INDEX with 4-byte offsets (an empty one is just the zero count).</summary>
    public static byte[] Index(IReadOnlyList<byte[]> objects)
    {
        if (objects.Count == 0)
            return [0, 0];
        var bytes = new List<byte>(Be.U16(objects.Count)) { 4 };
        long offset = 1;
        bytes.AddRange(Be.U32(offset));
        foreach (var o in objects)
        {
            offset += o.Length;
            bytes.AddRange(Be.U32(offset));
        }

        foreach (var o in objects)
            bytes.AddRange(o);
        return [.. bytes];
    }

    private static byte[] Int5(int v) => [29, .. Be.U32(v)];

    private static readonly Dictionary<string, byte[]> Operators = new(StringComparer.Ordinal)
    {
        ["hstem"] = [0x01],
        ["vstem"] = [0x03],
        ["vmoveto"] = [0x04],
        ["rlineto"] = [0x05],
        ["hlineto"] = [0x06],
        ["vlineto"] = [0x07],
        ["rrcurveto"] = [0x08],
        ["callsubr"] = [0x0A],
        ["return"] = [0x0B],
        ["endchar"] = [0x0E],
        ["hstemhm"] = [0x12],
        ["hintmask"] = [0x13],
        ["cntrmask"] = [0x14],
        ["rmoveto"] = [0x15],
        ["hmoveto"] = [0x16],
        ["vstemhm"] = [0x17],
        ["rcurveline"] = [0x18],
        ["rlinecurve"] = [0x19],
        ["vvcurveto"] = [0x1A],
        ["hhcurveto"] = [0x1B],
        ["callgsubr"] = [0x1D],
        ["vhcurveto"] = [0x1E],
        ["hvcurveto"] = [0x1F],
        ["hflex"] = [0x0C, 0x22],
        ["flex"] = [0x0C, 0x23],
        ["hflex1"] = [0x0C, 0x24],
        ["flex1"] = [0x0C, 0x25],
        ["abs"] = [0x0C, 0x09],
    };

    /// <summary>
    /// A Type 2 charstring from a space-separated program: integers become operands (one byte
    /// for -107..107, else the 3-byte form 28), names become operators, and <c>0xNN</c> is a raw byte.
    /// </summary>
    public static byte[] Charstring(string program)
    {
        var bytes = new List<byte>();
        foreach (string token in program.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("0x", StringComparison.Ordinal))
                bytes.Add(byte.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            else if (Operators.TryGetValue(token, out var op))
                bytes.AddRange(op);
            else
            {
                int v = int.Parse(token, CultureInfo.InvariantCulture);
                if (v is >= -107 and <= 107)
                    bytes.Add((byte)(v + 139));
                else
                    bytes.AddRange([28, (byte)(v >> 8), (byte)v]);
            }
        }

        return [.. bytes];
    }

    /// <summary><paramref name="program"/> repeated <paramref name="times"/> times.</summary>
    public static string Repeat(string program, int times) => string.Join(' ', Enumerable.Repeat(program, times));
}
