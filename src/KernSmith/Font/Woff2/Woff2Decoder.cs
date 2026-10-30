using System.Buffers.Binary;
using KernSmith.Font.Brotli;

namespace KernSmith.Font.Woff2;

/// <summary>
/// Decodes WOFF 2.0 (W3C Recommendation) to a plain sfnt — a TTF/OTF, or a TTC for a WOFF2
/// collection — that every backend and the core parser can read.
/// </summary>
/// <remarks>
/// All tables share one Brotli stream, decoded by <see cref="ManagedBrotliDecoder"/> (pure
/// managed, so WOFF2 works on browser-wasm). Transformed <c>glyf</c>/<c>loca</c> and
/// <c>hmtx</c> are rebuilt; every other table passes through. The output directory is sorted
/// by tag with 4-byte-aligned tables, fresh table checksums and a recomputed
/// <c>head.checkSumAdjustment</c>, as the spec requires of decoders.
/// </remarks>
internal static class Woff2Decoder
{
    private const int HeaderSize = 48;
    private const uint CollectionFlavor = 0x74746366; // 'ttcf'
    private const uint TagGlyf = 0x676C7966;
    private const uint TagLoca = 0x6C6F6361;
    private const uint TagHmtx = 0x686D7478;
    private const uint TagHead = 0x68656164;
    private const uint TagHhea = 0x68686561;
    private const uint TagMaxp = 0x6D617870;

    /// <summary>Cap on the decompressed table data and the rebuilt font (matches the WOFF 1.0 path).</summary>
    private const int MaxDecodedSize = 100 * 1024 * 1024;

    /// <summary>Known Table Tags, indexed by the low 6 bits of a directory entry's flags (index 63 = explicit tag).</summary>
    private static readonly uint[] s_knownTags = BuildKnownTags(
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep",
        "CFF ", "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE",
        "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt",
        "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar",
        "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill");

    /// <summary>Builds the exception every malformed-WOFF2 path throws.</summary>
    internal static FontParsingException Invalid(string reason) => new($"Invalid WOFF2 font: {reason}.");

    /// <summary>Decodes a complete WOFF2 file (signature already checked by the caller).</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw Invalid($"the file is {data.Length} bytes, shorter than the 48-byte WOFF2 header");

        var header = new Woff2Reader(data.Slice(0, HeaderSize), "header");
        header.ReadUInt32(); // signature
        var flavor = header.ReadUInt32();
        var length = header.ReadUInt32();
        if (length != data.Length)
            throw Invalid($"the header length field ({length}) does not match the file length ({data.Length})");
        int numTables = header.ReadUInt16();
        if (numTables == 0)
            throw Invalid("the table directory is empty");
        header.ReadUInt16(); // reserved
        header.ReadUInt32(); // totalSfntSize: advisory only
        var totalCompressedSize = header.ReadUInt32();

        var directory = new Woff2Reader(data.Slice(HeaderSize), "table directory");
        var entries = ReadTableDirectory(ref directory, numTables, out var totalStreamLength);
        var (fonts, collectionVersion) = flavor == CollectionFlavor
            ? ReadCollectionDirectory(ref directory, numTables)
            : ([new FontEntry(flavor, Enumerable.Range(0, numTables).ToArray())], 0u);

        var compressedStart = HeaderSize + directory.Position;
        if (totalCompressedSize > (uint)(data.Length - compressedStart))
            throw Invalid("the compressed data extends past the end of the file");

        byte[] stream;
        try
        {
            stream = ManagedBrotliDecoder.Decompress(data.Slice(compressedStart, (int)totalCompressedSize), totalStreamLength);
        }
        catch (FontParsingException ex)
        {
            throw new FontParsingException($"Invalid WOFF2 font: the compressed table data is corrupt. {ex.Message}", ex);
        }

        if (stream.Length != totalStreamLength)
            throw Invalid($"the compressed data decoded to {stream.Length} bytes, but the table directory describes {totalStreamLength}");

        var tables = ReconstructTables(stream, entries, fonts);
        return fonts.Length == 1 && flavor != CollectionFlavor
            ? WriteSingleFont(fonts[0], entries, tables)
            : WriteCollection(fonts, collectionVersion, entries, tables);
    }

    // ──────────────────────────────────────────────
    // Directories
    // ──────────────────────────────────────────────

    private static TableEntry[] ReadTableDirectory(ref Woff2Reader directory, int numTables, out int totalStreamLength)
    {
        var entries = new TableEntry[numTables];
        long streamOffset = 0;
        for (var i = 0; i < numTables; i++)
        {
            var flags = directory.ReadByte();
            var tagIndex = flags & 0x3F;
            var tag = tagIndex == 0x3F ? directory.ReadUInt32() : s_knownTags[tagIndex];
            var version = flags >> 6;
            var origLength = directory.ReadUIntBase128();

            var isGlyfOrLoca = tag is TagGlyf or TagLoca;
            var transformed = isGlyfOrLoca ? version != 3 : version != 0;
            if (transformed && !(isGlyfOrLoca ? version == 0 : tag == TagHmtx && version == 1))
                throw Invalid($"table '{TagName(tag)}' uses unknown transform version {version}");

            var streamLength = transformed ? directory.ReadUIntBase128() : origLength;
            if (tag == TagLoca && transformed && streamLength != 0)
                throw Invalid("a transformed loca table must have a transformLength of 0");
            if (origLength > MaxDecodedSize || streamOffset + streamLength > MaxDecodedSize)
                throw Invalid($"the tables exceed the {MaxDecodedSize}-byte limit");

            entries[i] = new TableEntry(tag, transformed, (int)origLength, (int)streamOffset, (int)streamLength);
            streamOffset += streamLength;
        }

        totalStreamLength = (int)streamOffset;
        return entries;
    }

    private static (FontEntry[] Fonts, uint Version) ReadCollectionDirectory(ref Woff2Reader directory, int numTables)
    {
        var version = directory.ReadUInt32();
        int numFonts = directory.Read255UInt16();
        if (numFonts == 0)
            throw Invalid("the collection contains no fonts");

        var fonts = new FontEntry[numFonts];
        for (var f = 0; f < numFonts; f++)
        {
            int count = directory.Read255UInt16();
            var flavor = directory.ReadUInt32();
            var indices = new int[count];
            for (var t = 0; t < count; t++)
            {
                indices[t] = directory.Read255UInt16();
                if (indices[t] >= numTables)
                    throw Invalid($"collection font {f} refers to table {indices[t]}, but there are only {numTables}");
            }

            fonts[f] = new FontEntry(flavor, indices);
        }

        return (fonts, version);
    }

    // ──────────────────────────────────────────────
    // Table reconstruction
    // ──────────────────────────────────────────────

    private static byte[][] ReconstructTables(byte[] stream, TableEntry[] entries, FontEntry[] fonts)
    {
        var tables = new byte[]?[entries.Length];
        var locaFormatByGlyf = new int[entries.Length];

        foreach (var font in fonts)
        {
            var glyfIndex = -1;
            var locaIndex = -1;
            var hmtxIndex = -1;
            var headIndex = -1;
            var hheaIndex = -1;
            var maxpIndex = -1;
            var seen = new HashSet<uint>();
            foreach (var index in font.TableIndices)
            {
                var tag = entries[index].Tag;
                if (!seen.Add(tag))
                    throw Invalid($"a font lists table '{TagName(tag)}' more than once");
                switch (tag)
                {
                    case TagGlyf: glyfIndex = index; break;
                    case TagLoca: locaIndex = index; break;
                    case TagHmtx: hmtxIndex = index; break;
                    case TagHead: headIndex = index; break;
                    case TagHhea: hheaIndex = index; break;
                    case TagMaxp: maxpIndex = index; break;
                }
            }

            var glyfTransformed = glyfIndex >= 0 && entries[glyfIndex].Transformed;
            var locaTransformed = locaIndex >= 0 && entries[locaIndex].Transformed;
            if (glyfTransformed != locaTransformed || (glyfIndex >= 0) != (locaIndex >= 0))
                throw Invalid("glyf and loca must both be present and share one transform");

            if (glyfTransformed && tables[glyfIndex] is null)
            {
                var source = Slice(stream, entries[glyfIndex]);
                var declaredFormat = Woff2GlyfTransform.DeclaredIndexFormat(source);
                var numGlyphs = Woff2GlyfTransform.DeclaredNumGlyphs(source);
                if (entries[locaIndex].OrigLength != (numGlyphs + 1) * (declaredFormat == 0 ? 2 : 4))
                    throw Invalid("the loca length does not match the transformed glyf glyph count and index format");

                var result = Woff2GlyfTransform.Reconstruct(source);
                tables[glyfIndex] = result.Glyf;
                tables[locaIndex] = result.Loca;
                locaFormatByGlyf[glyfIndex] = result.IndexFormat;
            }

            if (headIndex >= 0 && tables[headIndex] is null)
            {
                var head = Slice(stream, entries[headIndex]).ToArray();
                if (head.Length < 54)
                    throw Invalid("the head table is too short");
                if (glyfTransformed)
                    BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)locaFormatByGlyf[glyfIndex]);
                tables[headIndex] = head;
            }

            if (hmtxIndex >= 0 && entries[hmtxIndex].Transformed && tables[hmtxIndex] is null)
            {
                if (glyfIndex < 0 || headIndex < 0 || hheaIndex < 0 || maxpIndex < 0)
                    throw Invalid("a transformed hmtx needs glyf, loca, head, hhea and maxp");

                var hhea = Slice(stream, entries[hheaIndex]);
                var maxp = Slice(stream, entries[maxpIndex]);
                if (hhea.Length < 36 || maxp.Length < 6)
                    throw Invalid("the hhea or maxp table is too short");

                var glyf = tables[glyfIndex] ?? Slice(stream, entries[glyfIndex]).ToArray();
                var loca = tables[locaIndex] ?? Slice(stream, entries[locaIndex]).ToArray();
                var indexFormat = BinaryPrimitives.ReadInt16BigEndian(tables[headIndex]!.AsSpan(50));
                var hmtx = Woff2HmtxTransform.Reconstruct(
                    Slice(stream, entries[hmtxIndex]),
                    BinaryPrimitives.ReadUInt16BigEndian(maxp.Slice(4)),
                    BinaryPrimitives.ReadUInt16BigEndian(hhea.Slice(34)),
                    glyf, loca, indexFormat);
                if (hmtx.Length != entries[hmtxIndex].OrigLength)
                    throw Invalid("the rebuilt hmtx length does not match the table directory");
                tables[hmtxIndex] = hmtx;
            }

            foreach (var index in font.TableIndices)
                tables[index] ??= Slice(stream, entries[index]).ToArray();
        }

        // Entries no font refers to are dropped from the output.
        for (var i = 0; i < tables.Length; i++)
            tables[i] ??= [];
        return tables!;
    }

    private static ReadOnlySpan<byte> Slice(byte[] stream, TableEntry entry) =>
        stream.AsSpan(entry.StreamOffset, entry.StreamLength);

    // ──────────────────────────────────────────────
    // sfnt output
    // ──────────────────────────────────────────────

    private static byte[] WriteSingleFont(FontEntry font, TableEntry[] entries, byte[][] tables)
    {
        var order = SortedByTag(font, entries);
        var directorySize = 12 + 16 * order.Length;
        long size = directorySize;
        foreach (var index in order)
            size = Align4(size) + tables[index].Length;
        size = Align4(size);
        if (size > MaxDecodedSize)
            throw Invalid($"the decoded font exceeds the {MaxDecodedSize}-byte limit");

        var output = new byte[size];
        var offsets = new Dictionary<int, int>();
        var position = directorySize;
        foreach (var index in order)
        {
            position = (int)Align4(position);
            offsets[index] = position;
            tables[index].CopyTo(output, position);
            position += tables[index].Length;
        }

        WriteFontDirectory(output, 0, font.Flavor, order, entries, tables, offsets);
        UpdateCheckSumAdjustment(output, 0, order, entries, offsets, wholeFile: true);
        return output;
    }

    private static byte[] WriteCollection(FontEntry[] fonts, uint version, TableEntry[] entries, byte[][] tables)
    {
        var headerSize = 12 + 4 * fonts.Length + (version >= 0x00020000 ? 12 : 0);
        var orders = fonts.Select(f => SortedByTag(f, entries)).ToArray();
        long size = headerSize + orders.Sum(o => 12 + 16 * o.Length);

        var used = new SortedSet<int>(fonts.SelectMany(f => f.TableIndices));
        foreach (var index in used)
            size = Align4(size) + tables[index].Length;
        size = Align4(size);
        if (size > MaxDecodedSize)
            throw Invalid($"the decoded collection exceeds the {MaxDecodedSize}-byte limit");

        var output = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(output, CollectionFlavor);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8), (uint)fonts.Length);

        var position = headerSize;
        var directoryOffsets = new int[fonts.Length];
        for (var f = 0; f < fonts.Length; f++)
        {
            directoryOffsets[f] = position;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(12 + 4 * f), (uint)position);
            position += 12 + 16 * orders[f].Length;
        }

        var offsets = new Dictionary<int, int>();
        foreach (var index in used)
        {
            position = (int)Align4(position);
            offsets[index] = position;
            tables[index].CopyTo(output, position);
            position += tables[index].Length;
        }

        for (var f = 0; f < fonts.Length; f++)
        {
            WriteFontDirectory(output, directoryOffsets[f], fonts[f].Flavor, orders[f], entries, tables, offsets);
            UpdateCheckSumAdjustment(output, directoryOffsets[f], orders[f], entries, offsets, wholeFile: false);
        }

        return output;
    }

    private static void WriteFontDirectory(
        byte[] output, int at, uint flavor, int[] order, TableEntry[] entries, byte[][] tables, Dictionary<int, int> offsets)
    {
        var count = order.Length;
        var entrySelector = 0;
        while ((2 << entrySelector) <= count)
            entrySelector++;
        var searchRange = 16 << entrySelector;

        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 10), (ushort)(count * 16 - searchRange));

        for (var i = 0; i < count; i++)
        {
            var index = order[i];
            var table = output.AsSpan(offsets[index], tables[index].Length);
            if (entries[index].Tag == TagHead)
                table.Slice(8, 4).Clear(); // checkSumAdjustment is excluded from the head checksum

            var record = output.AsSpan(at + 12 + 16 * i);
            BinaryPrimitives.WriteUInt32BigEndian(record, entries[index].Tag);
            BinaryPrimitives.WriteUInt32BigEndian(record.Slice(4), Checksum(table));
            BinaryPrimitives.WriteUInt32BigEndian(record.Slice(8), (uint)offsets[index]);
            BinaryPrimitives.WriteUInt32BigEndian(record.Slice(12), (uint)table.Length);
        }
    }

    /// <summary>
    /// Sets <c>head.checkSumAdjustment</c> so the font sums to 0xB1B0AFBA. For a single font that
    /// is the whole file; in a collection it is the font's own directory plus its tables (a head
    /// shared between faces keeps the value for the last face that uses it).
    /// </summary>
    private static void UpdateCheckSumAdjustment(
        byte[] output, int directoryAt, int[] order, TableEntry[] entries, Dictionary<int, int> offsets, bool wholeFile)
    {
        var headPosition = Array.FindIndex(order, i => entries[i].Tag == TagHead);
        if (headPosition < 0)
            return;
        var headIndex = order[headPosition];

        uint sum;
        if (wholeFile)
        {
            sum = Checksum(output);
        }
        else
        {
            sum = Checksum(output.AsSpan(directoryAt, 12 + 16 * order.Length));
            for (var i = 0; i < order.Length; i++)
                sum += BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(directoryAt + 12 + 16 * i + 4));
        }

        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offsets[headIndex] + 8), 0xB1B0AFBA - sum);
    }

    private static int[] SortedByTag(FontEntry font, TableEntry[] entries) =>
        font.TableIndices.OrderBy(i => entries[i].Tag).ToArray();

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var i = 0;
        for (; i + 4 <= data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i));
        for (var shift = 24; i < data.Length; i++, shift -= 8)
            sum += (uint)data[i] << shift;
        return sum;
    }

    private static long Align4(long value) => (value + 3) & ~3L;

    private static string TagName(uint tag)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((tag >> (24 - 8 * i)) & 0xFF);
            chars[i] = c is >= ' ' and <= '~' ? c : '?';
        }

        return new string(chars);
    }

    private static uint[] BuildKnownTags(params string[] tags)
    {
        var result = new uint[63];
        for (var i = 0; i < tags.Length; i++)
            result[i] = BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(tags[i]));
        return result;
    }

    private readonly record struct TableEntry(uint Tag, bool Transformed, int OrigLength, int StreamOffset, int StreamLength);

    private sealed record FontEntry(uint Flavor, int[] TableIndices);
}
