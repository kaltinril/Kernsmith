using System.Buffers.Binary;
using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Holds <see cref="TtfParser.GposPairs"/> to the frozen <see cref="ReferenceGposParser"/>
/// oracle — the same pairs in the same order — over every fixture font with and without
/// subsetting, and over randomized (often malformed) PairPos format 1 and 2 subtables.
/// </summary>
public sealed class GposEquivalenceTests
{
    private static readonly string FixtureDir = Path.Join(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> FixtureFonts()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixtureDir, "*.*", SearchOption.AllDirectories)
                     .Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                 || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(FixtureDir, path));
        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureFonts))]
    public void FixtureFont_GposPairs_MatchReferenceWithAndWithoutSubsetting(string relativePath)
    {
        var font = File.ReadAllBytes(Path.Join(FixtureDir, relativePath));
        var gpos = FindTable(font, "GPOS");

        var full = new TtfParser(font);
        var requests = new List<HashSet<int>?>
        {
            null,
            Range(0x20, 0x7E),
            Range(0x20, 0xFF),
            Range(0x20, 0x24F),
            new("AVTWYoaeLPry.,".Select(c => (int)c)),
        };
        var rng = new Random(relativePath.Length);
        var mapped = full.CmapTable.Keys.OrderBy(c => c).ToList();
        for (var i = 0; i < 4 && mapped.Count > 0; i++)
            requests.Add(new HashSet<int>(mapped.Where(_ => rng.Next(3) == 0)));

        var sawPairs = false;
        foreach (var requested in requests)
        {
            var parser = new TtfParser(font, 0, requested);
            var relevant = requested == null ? null : new HashSet<int>(parser.CmapTable.Values);
            var expected = ReferenceGposParser.Parse(gpos, relevant);
            parser.GposPairs.ShouldBe(expected);
            sawPairs |= expected.Count > 0;
        }

        if (relativePath.Contains("Roboto-Regular", StringComparison.Ordinal))
            sawPairs.ShouldBeTrue("Roboto has GPOS kerning; the comparison must see real pairs");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RandomPairPosSubtables_MatchReference(int seed)
    {
        var rng = new Random(seed);
        var roboto = File.ReadAllBytes(Path.Join(FixtureDir, "Roboto-Regular.ttf"));
        var cmapCodepoints = new TtfParser(roboto, 0, null, parseAdvancedTables: false)
            .CmapTable.Keys.OrderBy(c => c).ToList();

        for (var trial = 0; trial < 60; trial++)
        {
            var subtables = new List<byte[]>();
            var count = rng.Next(1, 4);
            for (var s = 0; s < count; s++)
                subtables.Add(rng.Next(2) == 0 ? RandomFormat1(rng) : RandomFormat2(rng));

            var gpos = Gpos(subtables);
            var font = WithTable(roboto, "GPOS", gpos);

            HashSet<int>? requested = rng.Next(4) switch
            {
                0 => null,
                1 => Range(0x20, 0x7E),
                _ => new HashSet<int>(cmapCodepoints.Where(_ => rng.Next(4) == 0)),
            };

            var parser = new TtfParser(font, 0, requested);
            var relevant = requested == null ? null : new HashSet<int>(parser.CmapTable.Values);
            parser.GposPairs.ShouldBe(ReferenceGposParser.Parse(gpos, relevant), $"seed {seed} trial {trial}");
        }
    }

    // ------------------------------------------------------------ random subtables

    // Roboto's ASCII glyphs sit in a low id range; keep random ids near it so subsetting bites.
    private static int Glyph(Random rng) => rng.Next(10) == 0 ? rng.Next(0, 1400) : rng.Next(0, 140);

    private static byte[] RandomCoverage(Random rng, int pairSetCount)
    {
        if (rng.Next(2) == 0)
        {
            // Format 1, unsorted and possibly with duplicate glyphs.
            var n = rng.Next(0, pairSetCount + 4);
            var parts = new List<byte[]> { U16(1), U16(n) };
            for (var i = 0; i < n; i++)
                parts.Add(U16(Glyph(rng)));
            return Concat(parts);
        }
        else
        {
            // Format 2, ranges with arbitrary (possibly overlapping / out of range) start indices.
            var ranges = rng.Next(0, 6);
            var parts = new List<byte[]> { U16(2), U16(ranges) };
            for (var i = 0; i < ranges; i++)
            {
                var start = Glyph(rng);
                parts.Add(U16(start));
                parts.Add(U16(start + rng.Next(0, 30)));
                parts.Add(U16(rng.Next(0, pairSetCount + 3)));
            }
            return Concat(parts);
        }
    }

    private static byte[] RandomClassDef(Random rng, int classCount)
    {
        if (rng.Next(3) == 0)
        {
            var start = Glyph(rng);
            var n = rng.Next(0, 80);
            var parts = new List<byte[]> { U16(1), U16(start), U16(n) };
            for (var i = 0; i < n; i++)
                parts.Add(U16(rng.Next(0, classCount + 1)));
            return Concat(parts);
        }
        else
        {
            // Format 2; ranges may overlap (malformed but must parse identically).
            var ranges = rng.Next(0, 12);
            var parts = new List<byte[]> { U16(2), U16(ranges) };
            for (var i = 0; i < ranges; i++)
            {
                var start = Glyph(rng);
                parts.Add(U16(start));
                parts.Add(U16(start + rng.Next(0, 25)));
                parts.Add(U16(rng.Next(0, classCount + 1)));
            }
            return Concat(parts);
        }
    }

    private static int RandomValueFormat(Random rng) => rng.Next(4) switch
    {
        0 => 0x0004,
        1 => 0x0005,
        2 => 0x000F,
        _ => rng.Next(0, 16),
    };

    private static byte[] ValueRecord(Random rng, int format)
    {
        var parts = new List<byte[]>();
        for (var bit = 0; bit < 16; bit++)
        {
            if ((format & (1 << bit)) != 0)
                parts.Add(I16(rng.Next(3) == 0 ? 0 : rng.Next(-120, 120)));
        }
        return Concat(parts);
    }

    private static byte[] RandomFormat1(Random rng)
    {
        var vf1 = RandomValueFormat(rng);
        var vf2 = rng.Next(3) == 0 ? 0x0004 : 0;
        var pairSetCount = rng.Next(0, 20);

        var pairSets = new List<byte[]>();
        for (var i = 0; i < pairSetCount; i++)
        {
            var n = rng.Next(0, 25);
            var parts = new List<byte[]> { U16(n) };
            for (var p = 0; p < n; p++)
            {
                parts.Add(U16(Glyph(rng)));
                parts.Add(ValueRecord(rng, vf1));
                parts.Add(ValueRecord(rng, vf2));
            }
            pairSets.Add(Concat(parts));
        }

        var coverage = RandomCoverage(rng, pairSetCount);
        var headerSize = 10 + pairSetCount * 2;
        var offsets = new List<byte[]>();
        var cursor = headerSize;
        foreach (var ps in pairSets)
        {
            // Occasionally point past the end or share a pair set.
            offsets.Add(U16(rng.Next(25) == 0 ? 0xFFF0 : cursor));
            cursor += ps.Length;
        }
        var coverageAt = cursor;
        var body = Concat(
            U16(1), U16(coverageAt), U16(vf1), U16(vf2), U16(pairSetCount),
            Concat(offsets), Concat(pairSets), coverage);
        return MaybeTruncate(rng, body);
    }

    private static byte[] RandomFormat2(Random rng)
    {
        var vf1 = RandomValueFormat(rng);
        var vf2 = rng.Next(3) == 0 ? 0x0004 : 0;
        var class1Count = rng.Next(0, 12);
        var class2Count = rng.Next(0, 12);

        var records = new List<byte[]>();
        for (var i = 0; i < class1Count * class2Count; i++)
        {
            records.Add(ValueRecord(rng, vf1));
            records.Add(ValueRecord(rng, vf2));
        }
        var recordBytes = Concat(records);
        var coverage = RandomCoverage(rng, 40);
        var classDef1 = RandomClassDef(rng, class1Count);
        var classDef2 = RandomClassDef(rng, class2Count);

        var coverageAt = 16 + recordBytes.Length;
        var classDef1At = coverageAt + coverage.Length;
        var classDef2At = classDef1At + classDef1.Length;
        if (rng.Next(20) == 0) classDef1At = 0xFFF0;
        if (rng.Next(20) == 0) classDef2At = 0xFFF0;

        var body = Concat(
            U16(2), U16(coverageAt), U16(vf1), U16(vf2),
            U16(classDef1At), U16(classDef2At), U16(class1Count), U16(class2Count),
            recordBytes, coverage, classDef1, classDef2);
        return MaybeTruncate(rng, body);
    }

    private static byte[] MaybeTruncate(Random rng, byte[] body) =>
        rng.Next(8) == 0 ? body[..rng.Next(0, body.Length + 1)] : body;

    // ------------------------------------------------------------ table plumbing

    /// <summary>A GPOS with one 'kern' feature pointing at one PairPos lookup holding the subtables.</summary>
    private static byte[] Gpos(List<byte[]> subtables)
    {
        // header(10) FeatureList@10: count(2)+record(6)=8 -> Feature@18: 6 bytes -> LookupList@24: 4 bytes -> Lookup@28
        var lookupHeader = 6 + subtables.Count * 2;
        var offsets = new List<byte[]>();
        var cursor = lookupHeader;
        foreach (var s in subtables)
        {
            offsets.Add(U16(cursor));
            cursor += s.Length;
        }
        return Concat(
            U16(1), U16(0), U16(0), U16(10), U16(24),
            U16(1), "kern"u8.ToArray(), U16(8),
            U16(0), U16(1), U16(0),
            U16(1), U16(4),
            U16(2), U16(0), U16(subtables.Count), Concat(offsets),
            Concat(subtables));
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

    /// <summary>The font with one table replaced (or added), rebuilt as a fresh sfnt.</summary>
    private static byte[] WithTable(byte[] font, string replacedTag, byte[] replacement)
    {
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var tables = new List<(string Tag, byte[] Data)>();
        for (var i = 0; i < numTables; i++)
        {
            var record = 12 + (i * 16);
            var tag = System.Text.Encoding.ASCII.GetString(font, record, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            tables.Add((tag, tag == replacedTag ? replacement : font[offset..(offset + length)]));
        }

        if (!tables.Any(t => t.Tag == replacedTag))
            tables.Add((replacedTag, replacement));

        var dataOffset = 12 + (tables.Count * 16);
        var result = new byte[dataOffset + tables.Sum(t => (t.Data.Length + 3) & ~3)];
        BinaryPrimitives.WriteUInt32BigEndian(result, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), (ushort)tables.Count);
        for (var i = 0; i < tables.Count; i++)
        {
            var record = 12 + (i * 16);
            System.Text.Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(result, record);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(record + 8), (uint)dataOffset);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(record + 12), (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(result, dataOffset);
            dataOffset += (tables[i].Data.Length + 3) & ~3;
        }

        return result;
    }

    private static HashSet<int> Range(int first, int last) =>
        new(Enumerable.Range(first, last - first + 1));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Concat(List<byte[]> parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] U16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        return buffer;
    }

    private static byte[] I16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, (short)value);
        return buffer;
    }
}
