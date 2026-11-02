using System.Buffers.Binary;

namespace KernSmith.HintParity;

/// <summary>
/// Which hinting engine FreeType selects for a font under <c>FT_LOAD_DEFAULT</c>, inferred from the
/// table directory the way FreeType decides it: CFF outlines go to the Adobe CFF engine; a
/// <c>glyf</c> font with <c>fpgm</c>, <c>prep</c> or glyph instructions runs the TrueType
/// interpreter (v40 in grayscale, with nothing to execute when only <c>prep</c> exists); a
/// <c>glyf</c> font with none of those falls back to the auto-hinter.
/// </summary>
internal sealed record FreeTypeEngine(string Name, string Detail)
{
    public static readonly FreeTypeEngine Unknown = new("unknown", "could not read the table directory");

    public override string ToString() => Detail.Length == 0 ? Name : $"{Name} ({Detail})";

    public static FreeTypeEngine Infer(byte[] font)
    {
        try
        {
            return InferCore(font);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return Unknown;
        }
    }

    private static FreeTypeEngine InferCore(byte[] font)
    {
        if (font.Length < 12)
            return Unknown;

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(font);
        int offset = 0;
        if (tag == 0x74746366) // 'ttcf': first face
            offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(12)));
        else if (tag == 0x774F4646 || tag == 0x774F4632) // 'wOFF' / 'wOF2'
            return new FreeTypeEngine("unknown", "compressed container; pass the decoded font");

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset + 4));
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        for (int i = 0; i < numTables; i++)
        {
            int record = offset + 12 + (i * 16);
            string tableName = System.Text.Encoding.ASCII.GetString(font, record, 4);
            int tableOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12)));
            tables.TryAdd(tableName, (tableOffset, length));
        }

        if (tables.ContainsKey("CFF ") || tables.ContainsKey("CFF2"))
            return new FreeTypeEngine("cff", tables.ContainsKey("CFF2") ? "CFF2, Adobe engine" : "CFF, Adobe engine");

        if (!tables.ContainsKey("glyf"))
            return new FreeTypeEngine("unknown", "no glyf or CFF table");

        bool fpgm = tables.ContainsKey("fpgm");
        bool prep = tables.ContainsKey("prep");
        bool cvt = tables.ContainsKey("cvt ");
        int maxInstructions = 0;
        if (tables.TryGetValue("maxp", out var maxp) && maxp.Length >= 32
            && BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(maxp.Offset)) == 0x00010000)
        {
            maxInstructions = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(maxp.Offset + 26));
        }

        bool bit3 = false;
        if (tables.TryGetValue("head", out var head) && head.Length >= 18)
            bit3 = (BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(head.Offset + 16)) & 0x0008) != 0;

        if (!fpgm && !prep && maxInstructions == 0)
            return new FreeTypeEngine("autohint", "no fpgm/prep/instructions");

        var parts = new List<string>();
        if (fpgm) parts.Add("fpgm");
        if (prep) parts.Add("prep");
        if (cvt) parts.Add("cvt");
        parts.Add($"maxSizeOfInstructions={maxInstructions}");
        parts.Add(bit3 ? "head bit3 set (integer ppem)" : "head bit3 clear");

        string name = !fpgm && maxInstructions == 0 ? "bytecode-prep-only" : "bytecode";
        return new FreeTypeEngine(name, string.Join(", ", parts));
    }
}
