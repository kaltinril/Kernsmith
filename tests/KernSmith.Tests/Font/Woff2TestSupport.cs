using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace KernSmith.Tests.Font;

/// <summary>
/// Test-side WOFF2 tooling, written independently of the decoder under test: fixture access,
/// a minimal sfnt/glyf reader for comparing decoded fonts with their source TTFs, and a WOFF2
/// builder (null transforms only, single fonts and collections) that compresses with the BCL
/// Brotli encoder.
/// </summary>
internal static class Woff2TestSupport
{
    /// <summary>
    /// Committed WOFF2 fixtures, each produced by fontTools from the TTF beside it (see
    /// Fixtures/FONT-LICENSES.md): Roboto (glyf transform, long loca, untransformed hmtx),
    /// Noto Sans Latin subset (hinted, glyf + hmtx transforms, short loca) and Roboto Flex
    /// Latin subset (OVERLAP_SIMPLE flags, glyf + hmtx transforms, short loca).
    /// </summary>
    public static readonly string[] FixtureNames = ["Roboto-Regular", "NotoSans-Regular-Latin", "RobotoFlex-Latin"];

    public static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    public static byte[] Ttf(string name) => Fixture(name + ".ttf");

    public static byte[] Woff2(string name) => Fixture(name + ".woff2");

    // ──────────────────────────────────────────────
    // sfnt reading
    // ──────────────────────────────────────────────

    public sealed record TableRecord(string Tag, uint Checksum, int Offset, int Length);

    /// <summary>Reads the table records of the font at <paramref name="directoryOffset"/>.</summary>
    public static List<TableRecord> ReadTableRecords(byte[] sfnt, int directoryOffset = 0)
    {
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(directoryOffset + 4));
        var records = new List<TableRecord>(numTables);
        for (var i = 0; i < numTables; i++)
        {
            var r = sfnt.AsSpan(directoryOffset + 12 + i * 16);
            records.Add(new TableRecord(
                Encoding.ASCII.GetString(r.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(r.Slice(4)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(r.Slice(8)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(r.Slice(12))));
        }

        return records;
    }

    public static Dictionary<string, byte[]> ReadTables(byte[] sfnt, int directoryOffset = 0) =>
        ReadTableRecords(sfnt, directoryOffset).ToDictionary(r => r.Tag, r => sfnt.AsSpan(r.Offset, r.Length).ToArray());

    public static uint TableChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var i = 0;
        for (; i + 4 <= data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i));
        if (i < data.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            tail.Clear();
            data.Slice(i).CopyTo(tail);
            sum += BinaryPrimitives.ReadUInt32BigEndian(tail);
        }

        return sum;
    }

    // ──────────────────────────────────────────────
    // glyf parsing
    // ──────────────────────────────────────────────

    /// <summary>A glyph reduced to what a rasterizer sees, independent of how the bytes were packed.</summary>
    public sealed record ParsedGlyph(
        int ContourCount,
        short XMin, short YMin, short XMax, short YMax,
        int[] EndPoints,
        (int X, int Y, bool OnCurve)[] Points,
        bool OverlapSimple,
        byte[] Instructions,
        byte[] CompositeRecords)
    {
        public string Describe() =>
            $"contours={ContourCount} bbox=({XMin},{YMin},{XMax},{YMax}) ends=[{string.Join(",", EndPoints)}] " +
            $"points=[{string.Join(" ", Points.Select(p => $"{p.X},{p.Y}{(p.OnCurve ? "" : "o")}"))}] " +
            $"overlap={OverlapSimple} instr={Convert.ToHexString(Instructions)} comp={Convert.ToHexString(CompositeRecords)}";
    }

    public static ParsedGlyph[] ParseGlyphs(Dictionary<string, byte[]> tables)
    {
        var head = tables["head"];
        var longLoca = BinaryPrimitives.ReadInt16BigEndian(head.AsSpan(50)) != 0;
        int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(tables["maxp"].AsSpan(4));
        var loca = tables["loca"];
        var glyf = tables["glyf"];

        int Offset(int i) => longLoca
            ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(i * 4))
            : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(i * 2)) * 2;

        var glyphs = new ParsedGlyph[numGlyphs];
        for (var g = 0; g < numGlyphs; g++)
        {
            var start = Offset(g);
            var end = Offset(g + 1);
            glyphs[g] = end == start ? Empty : ParseGlyph(glyf.AsSpan(start, end - start));
        }

        return glyphs;
    }

    private static readonly ParsedGlyph Empty = new(0, 0, 0, 0, 0, [], [], false, [], []);

    private static ParsedGlyph ParseGlyph(ReadOnlySpan<byte> g)
    {
        var contours = BinaryPrimitives.ReadInt16BigEndian(g);
        var xMin = BinaryPrimitives.ReadInt16BigEndian(g.Slice(2));
        var yMin = BinaryPrimitives.ReadInt16BigEndian(g.Slice(4));
        var xMax = BinaryPrimitives.ReadInt16BigEndian(g.Slice(6));
        var yMax = BinaryPrimitives.ReadInt16BigEndian(g.Slice(8));
        var p = 10;

        if (contours < 0)
        {
            var start = p;
            var hasInstructions = false;
            ushort flags;
            do
            {
                flags = BinaryPrimitives.ReadUInt16BigEndian(g.Slice(p));
                hasInstructions |= (flags & 0x0100) != 0;
                p += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
                if ((flags & 0x0008) != 0) p += 2;
                else if ((flags & 0x0040) != 0) p += 4;
                else if ((flags & 0x0080) != 0) p += 8;
            }
            while ((flags & 0x0020) != 0);

            var records = g.Slice(start, p - start).ToArray();
            byte[] instructions = [];
            if (hasInstructions)
            {
                int length = BinaryPrimitives.ReadUInt16BigEndian(g.Slice(p));
                instructions = g.Slice(p + 2, length).ToArray();
            }

            return new ParsedGlyph(contours, xMin, yMin, xMax, yMax, [], [], false, instructions, records);
        }

        var endPoints = new int[contours];
        for (var c = 0; c < contours; c++, p += 2)
            endPoints[c] = BinaryPrimitives.ReadUInt16BigEndian(g.Slice(p));
        var pointCount = contours == 0 ? 0 : endPoints[^1] + 1;

        int instructionLength = BinaryPrimitives.ReadUInt16BigEndian(g.Slice(p));
        p += 2;
        var instr = g.Slice(p, instructionLength).ToArray();
        p += instructionLength;

        var pointFlags = new byte[pointCount];
        for (var i = 0; i < pointCount;)
        {
            var f = g[p++];
            pointFlags[i++] = f;
            if ((f & 0x08) != 0)
            {
                int repeat = g[p++];
                for (var r = 0; r < repeat; r++)
                    pointFlags[i++] = f;
            }
        }

        var xs = new int[pointCount];
        var ys = new int[pointCount];
        var value = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var f = pointFlags[i];
            if ((f & 0x02) != 0) value += (f & 0x10) != 0 ? g[p++] : -g[p++];
            else if ((f & 0x10) == 0) { value += BinaryPrimitives.ReadInt16BigEndian(g.Slice(p)); p += 2; }
            xs[i] = value;
        }

        value = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var f = pointFlags[i];
            if ((f & 0x04) != 0) value += (f & 0x20) != 0 ? g[p++] : -g[p++];
            else if ((f & 0x20) == 0) { value += BinaryPrimitives.ReadInt16BigEndian(g.Slice(p)); p += 2; }
            ys[i] = value;
        }

        var points = new (int, int, bool)[pointCount];
        for (var i = 0; i < pointCount; i++)
            points[i] = (xs[i], ys[i], (pointFlags[i] & 0x01) != 0);

        var overlap = pointCount > 0 && (pointFlags[0] & 0x40) != 0;
        return new ParsedGlyph(contours, xMin, yMin, xMax, yMax, endPoints, points, overlap, instr, []);
    }

    // ──────────────────────────────────────────────
    // WOFF2 directory inspection
    // ──────────────────────────────────────────────

    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep",
        "CFF ", "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE",
        "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt",
        "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar",
        "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    ];

    public sealed record DirectoryEntry(string Tag, int TransformVersion, int OrigLength, int? TransformLength);

    public sealed record ParsedWoff2(
        uint Flavor, List<DirectoryEntry> Entries, int CompressedOffset, int CompressedLength, byte[] CollectionDirectory);

    public static ParsedWoff2 ParseWoff2(byte[] woff2)
    {
        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woff2.AsSpan(4));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(woff2.AsSpan(12));
        var compressedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(woff2.AsSpan(20));
        var p = 48;
        var entries = new List<DirectoryEntry>();
        for (var i = 0; i < numTables; i++)
        {
            var flags = woff2[p++];
            string tag;
            if ((flags & 63) == 63)
            {
                tag = Encoding.ASCII.GetString(woff2, p, 4);
                p += 4;
            }
            else
            {
                tag = KnownTags[flags & 63];
            }

            var version = flags >> 6;
            var origLength = ReadBase128(woff2, ref p);
            var transformed = tag is "glyf" or "loca" ? version != 3 : version != 0;
            int? transformLength = transformed ? ReadBase128(woff2, ref p) : null;
            entries.Add(new DirectoryEntry(tag, version, origLength, transformLength));
        }

        var collectionStart = p;
        if (flavor == 0x74746366)
        {
            p += 4;
            var numFonts = Read255(woff2, ref p);
            for (var f = 0; f < numFonts; f++)
            {
                var count = Read255(woff2, ref p);
                p += 4;
                for (var t = 0; t < count; t++)
                    Read255(woff2, ref p);
            }
        }

        return new ParsedWoff2(flavor, entries, p, compressedLength, woff2.AsSpan(collectionStart, p - collectionStart).ToArray());
    }

    /// <summary>
    /// Re-wraps a WOFF2 file around a modified decompressed stream: decompresses with the BCL,
    /// applies <paramref name="mutate"/> to the stream bytes, recompresses and patches the header.
    /// The table directory is kept, so table lengths must not change.
    /// </summary>
    public static byte[] RewrapWithMutatedStream(byte[] woff2, Action<byte[], List<(DirectoryEntry Entry, int Offset)>> mutate)
    {
        var parsed = ParseWoff2(woff2);
        var stream = BclDecompress(woff2.AsSpan(parsed.CompressedOffset, parsed.CompressedLength));

        var layout = new List<(DirectoryEntry, int)>();
        var offset = 0;
        foreach (var entry in parsed.Entries)
        {
            layout.Add((entry, offset));
            offset += entry.TransformLength ?? entry.OrigLength;
        }

        mutate(stream, layout);
        var compressed = BclCompress(stream, quality: 4);

        var result = new byte[parsed.CompressedOffset + compressed.Length];
        woff2.AsSpan(0, parsed.CompressedOffset).CopyTo(result);
        compressed.CopyTo(result, parsed.CompressedOffset);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)result.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20), (uint)compressed.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(28), 0); // metaOffset
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(32), 0); // metaLength
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(36), 0); // metaOrigLength
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(40), 0); // privOffset
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(44), 0); // privLength
        return result;
    }

    // ──────────────────────────────────────────────
    // WOFF2 building (null transforms)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds a WOFF2 file holding <paramref name="fonts"/> with every table null-transformed
    /// (glyf/loca flagged version 3). One font gives a plain WOFF2; several give a collection,
    /// with byte-identical tables stored once and shared through the collection directory.
    /// </summary>
    public static byte[] BuildNullTransformWoff2(params byte[][] fonts)
    {
        var isCollection = fonts.Length > 1;
        var unique = new List<(string Tag, byte[] Data)>();
        var fontIndices = new List<(uint Flavor, List<int> Indices)>();

        foreach (var font in fonts)
        {
            var tables = ReadTables(font)
                .OrderBy(t => t.Key == "loca" ? "glyf\u0001" : t.Key, StringComparer.Ordinal)
                .ToList();
            var indices = new List<int>();
            foreach (var (tag, data) in tables)
            {
                var existing = unique.FindIndex(u => u.Tag == tag && u.Data.AsSpan().SequenceEqual(data));
                if (existing < 0)
                {
                    unique.Add((tag, data));
                    existing = unique.Count - 1;
                }

                indices.Add(existing);
            }

            fontIndices.Add((BinaryPrimitives.ReadUInt32BigEndian(font), indices));
        }

        var header = new MemoryStream();
        foreach (var (tag, data) in unique)
        {
            var known = Array.IndexOf(KnownTags, tag);
            var version = tag is "glyf" or "loca" ? 3 : 0;
            header.WriteByte((byte)((known < 0 ? 63 : known) | (version << 6)));
            if (known < 0)
                header.Write(Encoding.ASCII.GetBytes(tag));
            WriteBase128(header, data.Length);
        }

        if (isCollection)
        {
            WriteUInt32(header, 0x00010000);
            Write255(header, fonts.Length);
            foreach (var (flavor, indices) in fontIndices)
            {
                Write255(header, indices.Count);
                WriteUInt32(header, flavor);
                foreach (var index in indices)
                    Write255(header, index);
            }
        }

        var stream = new MemoryStream();
        foreach (var (_, data) in unique)
            stream.Write(data);
        var compressed = BclCompress(stream.ToArray());

        var directory = header.ToArray();
        var woff2 = new byte[48 + directory.Length + compressed.Length];
        "wOF2"u8.CopyTo(woff2);
        BinaryPrimitives.WriteUInt32BigEndian(woff2.AsSpan(4), isCollection ? 0x74746366u : fontIndices[0].Flavor);
        BinaryPrimitives.WriteUInt32BigEndian(woff2.AsSpan(8), (uint)woff2.Length);
        BinaryPrimitives.WriteUInt16BigEndian(woff2.AsSpan(12), (ushort)unique.Count);
        BinaryPrimitives.WriteUInt32BigEndian(woff2.AsSpan(16), (uint)fonts.Sum(f => f.Length));
        BinaryPrimitives.WriteUInt32BigEndian(woff2.AsSpan(20), (uint)compressed.Length);
        BinaryPrimitives.WriteUInt16BigEndian(woff2.AsSpan(24), 1);
        directory.CopyTo(woff2, 48);
        compressed.CopyTo(woff2, 48 + directory.Length);
        return woff2;
    }

    /// <summary>Concatenates fonts into a TrueType Collection (version 1 header, tables not shared).</summary>
    public static byte[] BuildTtc(params byte[][] fonts)
    {
        var headerSize = 12 + 4 * fonts.Length;
        var directorySizes = fonts.Select(f => 12 + 16 * BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(4))).ToArray();
        var output = new MemoryStream();
        output.Write(new byte[headerSize + directorySizes.Sum()]);
        var buffer = output.GetBuffer();

        "ttcf"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8), (uint)fonts.Length);

        var directoryOffset = headerSize;
        var directoryOffsets = new int[fonts.Length];
        for (var f = 0; f < fonts.Length; f++)
        {
            directoryOffsets[f] = directoryOffset;
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12 + 4 * f), (uint)directoryOffset);
            directoryOffset += directorySizes[f];
        }

        var records = new List<(int RecordPosition, byte[] Data)>();
        for (var f = 0; f < fonts.Length; f++)
        {
            fonts[f].AsSpan(0, directorySizes[f]).CopyTo(buffer.AsSpan(directoryOffsets[f]));
            foreach (var (record, i) in ReadTableRecords(fonts[f]).Select((r, i) => (r, i)))
                records.Add((directoryOffsets[f] + 12 + i * 16, fonts[f].AsSpan(record.Offset, record.Length).ToArray()));
        }

        foreach (var (recordPosition, data) in records)
        {
            while (output.Length % 4 != 0)
                output.WriteByte(0);
            var offset = (int)output.Length;
            output.Write(data);
            buffer = output.GetBuffer();
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(recordPosition + 8), (uint)offset);
        }

        while (output.Length % 4 != 0)
            output.WriteByte(0);
        return output.ToArray();
    }

    public static byte[] BclCompress(byte[] data, int quality = 11)
    {
        var destination = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length) + 64];
        if (!BrotliEncoder.TryCompress(data, destination, out var written, quality, 22))
            throw new InvalidOperationException("BCL Brotli compression failed");
        return destination.AsSpan(0, written).ToArray();
    }

    private static byte[] BclDecompress(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static int ReadBase128(byte[] data, ref int p)
    {
        var value = 0;
        for (var i = 0; i < 5; i++)
        {
            var b = data[p++];
            value = (value << 7) | (b & 0x7f);
            if ((b & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("UIntBase128 longer than 5 bytes");
    }

    private static int Read255(byte[] data, ref int p)
    {
        int code = data[p++];
        return code switch
        {
            253 => (data[p++] << 8) | data[p++],
            255 => data[p++] + 253,
            254 => data[p++] + 506,
            _ => code,
        };
    }

    private static void WriteBase128(Stream s, int value)
    {
        Span<byte> bytes = stackalloc byte[5];
        var count = 0;
        do
        {
            bytes[count++] = (byte)(value & 0x7f);
            value >>= 7;
        }
        while (value != 0);

        for (var i = count - 1; i >= 0; i--)
            s.WriteByte((byte)(bytes[i] | (i > 0 ? 0x80 : 0)));
    }

    private static void Write255(Stream s, int value)
    {
        if (value < 253)
        {
            s.WriteByte((byte)value);
            return;
        }

        s.WriteByte(253);
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }

    private static void WriteUInt32(Stream s, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        s.Write(bytes);
    }
}
