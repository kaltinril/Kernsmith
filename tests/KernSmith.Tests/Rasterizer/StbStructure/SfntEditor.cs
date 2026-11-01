using System.Buffers.Binary;
using System.Text;

namespace KernSmith.Tests.Rasterizer.StbStructure;

/// <summary>
/// Byte-level edits to an sfnt (no checksum upkeep), for building fonts that break exactly one
/// structural rule. Every position is relative to the start of the named table unless the
/// method says otherwise.
/// </summary>
internal sealed class SfntEditor
{
    public SfntEditor(byte[] font) => Bytes = (byte[])font.Clone();

    public byte[] Bytes { get; }

    public static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Fixtures", name));

    public static SfntEditor Roboto() => new(Fixture("Roboto-Regular.ttf"));

    public static SfntEditor SourceSerif() => new(Fixture("SourceSerif4-Regular.otf"));

    /// <summary>Position of the table's directory record, from the file start.</summary>
    public int RecordOf(string tag)
    {
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(4));
        byte[] t = Encoding.ASCII.GetBytes(tag);
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (16 * i);
            if (Bytes.AsSpan(rec, 4).SequenceEqual(t))
                return rec;
        }

        throw new InvalidOperationException($"No '{tag}' table.");
    }

    public int OffsetOf(string tag) => (int)BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(RecordOf(tag) + 8));

    public int LengthOf(string tag) => (int)BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(RecordOf(tag) + 12));

    public SfntEditor SetLength(string tag, long length)
    {
        BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(RecordOf(tag) + 12), (uint)length);
        return this;
    }

    public SfntEditor SetOffset(string tag, long offset)
    {
        BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(RecordOf(tag) + 8), (uint)offset);
        return this;
    }

    public SfntEditor Rename(string tag, string newTag)
    {
        Encoding.ASCII.GetBytes(newTag).CopyTo(Bytes, RecordOf(tag));
        return this;
    }

    public ushort U16(string tag, int pos) => BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(OffsetOf(tag) + pos));

    public uint U32(string tag, int pos) => BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(OffsetOf(tag) + pos));

    public SfntEditor SetU16(string tag, int pos, int value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan(OffsetOf(tag) + pos), (ushort)value);
        return this;
    }

    public SfntEditor SetU32(string tag, int pos, long value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(OffsetOf(tag) + pos), (uint)value);
        return this;
    }

    public SfntEditor SetByte(string tag, int pos, int value)
    {
        Bytes[OffsetOf(tag) + pos] = (byte)value;
        return this;
    }

    /// <summary>Overwrites the start of a table with <paramref name="content"/> and sets its length to fit.</summary>
    public SfntEditor ReplaceTable(string tag, byte[] content)
    {
        if (content.Length > LengthOf(tag))
            throw new InvalidOperationException($"Replacement '{tag}' ({content.Length} bytes) does not fit in {LengthOf(tag)}.");
        content.CopyTo(Bytes, OffsetOf(tag));
        return SetLength(tag, content.Length);
    }

    // ------------------------------------------------------------ glyf

    public int NumGlyphs => U16("maxp", 4);

    private bool LongLoca => U16("head", 50) == 1;

    public int LocaEntry(int glyph) =>
        LongLoca ? (int)U32("loca", glyph * 4) : U16("loca", glyph * 2) * 2;

    public SfntEditor SetLocaEntry(int glyph, int offset) =>
        LongLoca ? SetU32("loca", glyph * 4, offset) : SetU16("loca", glyph * 2, offset / 2);

    /// <summary>The glyph's record, relative to the <c>glyf</c> table start.</summary>
    public (int Start, int Length) GlyphRecord(int glyph)
    {
        int start = LocaEntry(glyph);
        return (start, LocaEntry(glyph + 1) - start);
    }

    /// <summary>Overwrites a glyph's record in place (the record keeps its loca length).</summary>
    public SfntEditor WriteGlyph(int glyph, byte[] record)
    {
        var (start, length) = GlyphRecord(glyph);
        if (record.Length > length)
            throw new InvalidOperationException($"Glyph {glyph} holds {length} bytes; {record.Length} do not fit.");
        record.CopyTo(Bytes, OffsetOf("glyf") + start);
        return this;
    }

    /// <summary>Non-empty glyph records of at least <paramref name="minLength"/> bytes, largest first.</summary>
    public IEnumerable<int> GlyphsWithRecordAtLeast(int minLength) =>
        Enumerable.Range(1, NumGlyphs - 1)
            .Where(g => GlyphRecord(g).Length >= minLength)
            .OrderByDescending(g => GlyphRecord(g).Length);

    /// <summary>Wraps a single font as face 0 of a one-face TTC (table offsets moved by the new header).</summary>
    public static byte[] AsCollection(byte[] font, int numFonts = 1)
    {
        int header = 12 + (4 * numFonts);
        var ttc = new byte[header + font.Length];
        "ttcf"u8.CopyTo(ttc);
        BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(8), (uint)numFonts);
        for (int i = 0; i < numFonts; i++)
            BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(12 + (4 * i)), (uint)header);
        font.CopyTo(ttc, header);

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < numTables; i++)
        {
            var offset = ttc.AsSpan(header + 12 + (16 * i) + 8);
            BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)header);
        }

        return ttc;
    }
}

/// <summary>Big-endian byte builders.</summary>
internal static class Be
{
    public static byte[] U16(int v) => [(byte)(v >> 8), (byte)v];

    public static byte[] U32(long v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    public static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
