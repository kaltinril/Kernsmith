using System.Buffers.Binary;
using System.Runtime.InteropServices;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Cff;

namespace KernSmith.Rasterizers.Native.Tests.Fuzzing;

/// <summary>
/// Deterministic, table-aware damage to an sfnt font: every mutation lands inside one named
/// table (or the table directory), so a crash reports which parser it reached. Half of the
/// in-table positions go to the table header and, for <c>glyf</c>/<c>CFF </c>/<c>CFF2</c>, to
/// the outlines of the sampled glyphs — uniform positions in a large outline table would almost
/// never touch a glyph the harness actually renders.
/// </summary>
internal sealed class FontMutator
{
    /// <summary>Pseudo-tag for the sfnt table directory itself.</summary>
    public const string Directory = "<directory>";

    private static readonly MutationKind[] s_kinds = Enum.GetValues<MutationKind>();

    private readonly byte[] _font;
    private readonly Dictionary<string, TableSpan> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(int Offset, int Length)>> _hotRanges = new(StringComparer.Ordinal);

    /// <param name="font">An undamaged single-face sfnt.</param>
    /// <param name="sampleCodepoints">The codepoints the harness renders; their outlines are hot ranges.</param>
    /// <param name="tablesToMutate">Tags to target (tables missing from the font are skipped).</param>
    public FontMutator(byte[] font, IEnumerable<int> sampleCodepoints, IEnumerable<string> tablesToMutate)
    {
        _font = font;

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var present = new Dictionary<string, TableSpan>(StringComparer.Ordinal);
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            string tag = System.Text.Encoding.ASCII.GetString(font, record, 4);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            present[tag] = new TableSpan(record, offset, length);
        }

        present[Directory] = new TableSpan(-1, 0, 12 + (numTables * 16));

        foreach (string tag in tablesToMutate)
        {
            if (present.TryGetValue(tag, out var span) && span.Length > 0)
                _tables[tag] = span;
        }

        Tables = [.. _tables.Keys.Order(StringComparer.Ordinal)];
        FindHotRanges(sampleCodepoints);
    }

    /// <summary>The tags this mutator targets, in a stable order.</summary>
    public IReadOnlyList<string> Tables { get; }

    /// <summary>Every mutation kind, in a stable order.</summary>
    public static IReadOnlyList<MutationKind> Kinds => s_kinds;

    /// <summary>Returns a damaged copy of the font and a description that reproduces it.</summary>
    public byte[] Apply(string tag, MutationKind kind, Random rng, out string description)
    {
        var table = _tables[tag];
        var font = (byte[])_font.Clone();

        if (kind == MutationKind.Truncate)
        {
            if (table.RecordOffset >= 0 && rng.Next(3) != 0)
            {
                int newLength = rng.Next(table.Length);
                BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(table.RecordOffset + 12), (uint)newLength);
                description = $"length {table.Length} -> {newLength}";
                return font;
            }

            int cut = table.Offset + rng.Next(table.Length);
            description = $"file cut at {cut}";
            return font[..cut];
        }

        _hotRanges.TryGetValue(tag, out var hot);
        description = ByteDamage.Apply(font, kind, table.Offset, table.Length, rng,
            width => ByteDamage.Pick(table.Offset, table.Length, width, rng, hot));
        return font;
    }

    private void FindHotRanges(IEnumerable<int> sampleCodepoints)
    {
        var face = NativeFontFace.Load(_font);
        var glyphs = sampleCodepoints.Select(face.GetGlyphIndex).Where(g => g > 0).Distinct().ToArray();

        if (face.Loca is { } loca && _tables.TryGetValue("glyf", out var glyf))
        {
            var ranges = new List<(int, int)>();
            foreach (int glyph in glyphs)
            {
                var (offset, length) = loca.GetGlyphRange(glyph);
                if (length > 0)
                    ranges.Add((glyf.Offset + offset, length));
            }

            _hotRanges["glyf"] = ranges;
        }

        foreach (string tag in new[] { "CFF ", "CFF2" })
        {
            if (face.Tables.TryGetTable(tag) is not { } table)
                continue;

            var cff = tag == "CFF " ? CffFont.ParseCff(table) : CffFont.ParseCff2(table);
            var ranges = new List<(int, int)>();
            foreach (int glyph in glyphs)
            {
                var charstring = cff.CharStrings[glyph];
                if (MemoryMarshal.TryGetArray(charstring, out var segment) && segment.Count > 0)
                    ranges.Add((segment.Offset, segment.Count));
            }

            _hotRanges[tag] = ranges;
        }
    }

    /// <param name="RecordOffset">Where the table's directory record starts (-1 for the directory itself).</param>
    private readonly record struct TableSpan(int RecordOffset, int Offset, int Length);
}
