using System.Buffers.Binary;
using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Holds the parser's name, hmtx and font-info bookkeeping to frozen copies of their
/// straightforward implementations (every candidate name decoded, every hmtx entry read up
/// front, LINQ over the cmap) for every fixture font and for randomized name tables.
/// </summary>
public sealed class ParserSmallItemsEquivalenceTests
{
    private static readonly string FixtureDir = Path.Join(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> FixtureFonts() => GposEquivalenceTests.FixtureFonts();

    [Theory]
    [MemberData(nameof(FixtureFonts))]
    public void FixtureFont_NamesHmtxAndFontInfo_MatchReference(string relativePath)
    {
        var font = File.ReadAllBytes(Path.Join(FixtureDir, relativePath));
        foreach (var requested in new HashSet<int>?[] { null, new(Enumerable.Range(0x20, 0x5F)), new(Enumerable.Range(0x20, 0xE0)) })
        {
            var parser = new TtfParser(font, 0, requested);
            parser.Names.ShouldBe(ReferenceNames(FindTable(font, "name")));

            var hhea = parser.Hhea;
            var expectedHmtx = hhea == null ? null : ReferenceHmtx(FindTable(font, "hmtx"), hhea.NumberOfHMetrics);
            if (expectedHmtx == null)
            {
                parser.Hmtx.ShouldBeNull();
            }
            else
            {
                parser.Hmtx.ShouldNotBeNull();
                parser.Hmtx.GlyphCount.ShouldBe(expectedHmtx.Value.Lsb.Length);
                for (var g = 0; g < expectedHmtx.Value.Lsb.Length; g++)
                {
                    parser.Hmtx.GetAdvanceWidth(g).ShouldBe(expectedHmtx.Value.Advance[Math.Min(g, expectedHmtx.Value.Advance.Length - 1)]);
                    parser.Hmtx.GetLeftSideBearing(g).ShouldBe(expectedHmtx.Value.Lsb[g]);
                }
                Should.Throw<ArgumentOutOfRangeException>(() => parser.Hmtx.GetAdvanceWidth(expectedHmtx.Value.Lsb.Length));
                Should.Throw<ArgumentOutOfRangeException>(() => parser.Hmtx.GetLeftSideBearing(-1));
            }

            var info = new TtfFontReader { RequestedCodepoints = requested }.ReadFont(font);
            info.AvailableCodepoints.ShouldBe(
                parser.CmapTable.Where(kvp => kvp.Value != 0).Select(kvp => kvp.Key).OrderBy(cp => cp).ToList());
            info.NumGlyphs.ShouldBe(parser.CmapTable.Values.Distinct().Count());
            info.DesignMetrics.Keys.ShouldBe(info.AvailableCodepoints.Where(cp =>
                expectedHmtx != null && (uint)parser.CmapTable[cp] < (uint)expectedHmtx.Value.Lsb.Length).ToList());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomNameTables_MatchReference(int seed)
    {
        var rng = new Random(seed);
        var roboto = File.ReadAllBytes(Path.Join(FixtureDir, "Roboto-Regular.ttf"));
        for (var trial = 0; trial < 80; trial++)
        {
            var name = RandomNameTable(rng);
            var parser = new TtfParser(GposEquivalenceTests.WithTable(roboto, "name", name), 0, null, parseAdvancedTables: false);
            parser.Names.ShouldBe(ReferenceNames(name), $"seed {seed} trial {trial}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void TruncatedHmtx_MatchesReference(int trailingBytes)
    {
        // numberOfHMetrics full entries plus a ragged tail of left side bearings.
        var roboto = File.ReadAllBytes(Path.Join(FixtureDir, "Roboto-Regular.ttf"));
        var numberOfHMetrics = new TtfParser(roboto, 0, null, parseAdvancedTables: false).Hhea!.NumberOfHMetrics;
        foreach (var fullEntries in new[] { numberOfHMetrics - 1, numberOfHMetrics })
        {
            var source = FindTable(roboto, "hmtx");
            var hmtx = new byte[fullEntries * 4 + trailingBytes];
            for (var i = 0; i < hmtx.Length; i++)
                hmtx[i] = i < source.Length ? source[i] : (byte)(i * 37);
            var parser = new TtfParser(GposEquivalenceTests.WithTable(roboto, "hmtx", hmtx), 0, null, parseAdvancedTables: false);
            var expected = ReferenceHmtx(hmtx, numberOfHMetrics);
            if (expected == null)
            {
                parser.Hmtx.ShouldBeNull();
                continue;
            }

            parser.Hmtx.ShouldNotBeNull();
            parser.Hmtx.GlyphCount.ShouldBe(expected.Value.Lsb.Length);
            for (var g = 0; g < expected.Value.Lsb.Length; g++)
            {
                parser.Hmtx.GetAdvanceWidth(g).ShouldBe(expected.Value.Advance[Math.Min(g, expected.Value.Advance.Length - 1)]);
                parser.Hmtx.GetLeftSideBearing(g).ShouldBe(expected.Value.Lsb[g]);
            }
        }
    }

    private static byte[] RandomNameTable(Random rng)
    {
        var count = rng.Next(0, 30);
        var strings = new List<byte>();
        var records = new List<byte>();
        void U16(List<byte> to, int v) { to.Add((byte)(v >> 8)); to.Add((byte)v); }
        for (var i = 0; i < count; i++)
        {
            var (platform, encoding) = rng.Next(5) switch
            {
                0 => (1, 0),
                1 => (0, 3),
                2 => (3, 10),
                _ => (3, 1),
            };
            var language = rng.Next(3) == 0 ? 0x0409 : rng.Next(0, 0x0500);
            var nameId = rng.Next(0, 16);
            var text = new string((char)('A' + rng.Next(26)), rng.Next(0, 6)) + i;
            var bytes = platform == 3
                ? System.Text.Encoding.BigEndianUnicode.GetBytes(text)
                : System.Text.Encoding.Latin1.GetBytes(text);
            if (rng.Next(6) == 0 && bytes.Length > 0)
                bytes = bytes[..^1]; // odd UTF-16 length
            var offset = strings.Count;
            var length = bytes.Length;
            if (rng.Next(12) == 0)
                length += 1000; // runs past the table
            strings.AddRange(bytes);
            U16(records, platform); U16(records, encoding); U16(records, language);
            U16(records, nameId); U16(records, length); U16(records, offset);
        }

        var table = new List<byte>();
        U16(table, 0); U16(table, count); U16(table, 6 + count * 12);
        table.AddRange(records);
        table.AddRange(strings);
        return table.ToArray();
    }

    // ---------------------------------------------------------------- frozen references

    private static NameInfo? ReferenceNames(byte[] tableBytes)
    {
        ReadOnlySpan<byte> table = tableBytes;
        if (table.IsEmpty || table.Length < 6)
            return null;

        var count = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(2));
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(4));
        var targetIds = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14 };
        var results = new Dictionary<int, (int priority, string value)>();

        for (var i = 0; i < count; i++)
        {
            var recOffset = 6 + i * 12;
            if (recOffset + 12 > table.Length)
                break;
            var platformID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset));
            var encodingID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 2));
            var languageID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 4));
            var nameID = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 6));
            var length = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 8));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(recOffset + 10));
            if (!targetIds.Contains(nameID))
                continue;
            var strStart = stringOffset + offset;
            if (strStart + length > table.Length)
                continue;
            var strData = table.Slice(strStart, length);
            string value;
            int priority;
            if (platformID == 3 && encodingID == 1)
            {
                var chars = new char[strData.Length / 2];
                for (var c = 0; c < chars.Length; c++)
                    chars[c] = (char)BinaryPrimitives.ReadUInt16BigEndian(strData.Slice(c * 2));
                value = new string(chars);
                priority = languageID == 0x0409 ? 3 : 2;
            }
            else if (platformID == 1 && encodingID == 0)
            {
                var chars = new char[strData.Length];
                for (var c = 0; c < strData.Length; c++)
                    chars[c] = (char)strData[c];
                value = new string(chars);
                priority = 1;
            }
            else
            {
                continue;
            }

            if (!results.TryGetValue(nameID, out var existing) || priority > existing.priority)
                results[nameID] = (priority, value);
        }

        string? Get(int id) => results.TryGetValue(id, out var r) ? r.value : null;
        return new NameInfo(
            FontFamily: Get(1), FontSubfamily: Get(2), FullName: Get(4), PostScriptName: Get(6),
            Copyright: Get(0), Trademark: Get(7), UniqueId: Get(3), Version: Get(5),
            Manufacturer: Get(8), Designer: Get(9), Description: Get(10), License: Get(13), LicenseUrl: Get(14));
    }

    private static (ushort[] Advance, short[] Lsb)? ReferenceHmtx(byte[] data, int numberOfHMetrics)
    {
        if (data.Length == 0 || numberOfHMetrics <= 0)
            return null;
        var fullMetricsBytes = numberOfHMetrics * 4;
        if (data.Length < fullMetricsBytes)
            return null;
        var trailingLsbCount = (data.Length - fullMetricsBytes) / 2;
        var advance = new ushort[numberOfHMetrics];
        var lsb = new short[numberOfHMetrics + trailingLsbCount];
        var offset = 0;
        for (var i = 0; i < numberOfHMetrics; i++)
        {
            advance[i] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            lsb[i] = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 2));
            offset += 4;
        }
        for (var i = 0; i < trailingLsbCount; i++)
        {
            lsb[numberOfHMetrics + i] = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
            offset += 2;
        }
        return (advance, lsb);
    }

    private static byte[] FindTable(byte[] font, string tag)
    {
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < numTables; i++)
        {
            var record = 12 + (i * 16);
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) != tag)
                continue;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            return font[offset..(offset + length)];
        }
        return [];
    }
}
