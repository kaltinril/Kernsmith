using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The <c>hdmx</c> (horizontal device metrics) parser: per-ppem records of one hinted advance
/// byte per glyph, hardened like the other table parsers — bounds-checked, and a malformed
/// table is ignored (null), never an exception.
/// </summary>
public class HdmxTableTests
{
    /// <summary>A table over three glyphs with records at 10 and 12 ppem: widths 5,6,7 and 6,7,8.</summary>
    private static byte[] Synthetic(short numRecords = 2, int sizeDeviceRecord = 8, ushort version = 0)
    {
        var data = new byte[8 + (2 * 8)];
        BinaryPrimitives.WriteUInt16BigEndian(data, version);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), numRecords);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), sizeDeviceRecord);
        data[8] = 10; data[9] = 7; data[10] = 5; data[11] = 6; data[12] = 7;
        data[16] = 12; data[17] = 8; data[18] = 6; data[19] = 7; data[20] = 8;
        return data;
    }

    [Fact]
    public void Synthetic_ParsesRecordsByPixelSize()
    {
        var hdmx = HdmxTable.TryParse(Synthetic(), numGlyphs: 3);

        hdmx.ShouldNotBeNull();
        hdmx.PixelSizes.ShouldBe([10, 12]);
        hdmx.HasRecord(10).ShouldBeTrue();
        hdmx.HasRecord(11).ShouldBeFalse();
        hdmx.TryGetAdvance(10, 1, out int advance).ShouldBeTrue();
        advance.ShouldBe(6);
        hdmx.TryGetAdvance(12, 2, out advance).ShouldBeTrue();
        advance.ShouldBe(8);
        hdmx.TryGetAdvance(11, 0, out _).ShouldBeFalse();
        hdmx.TryGetAdvance(10, 3, out _).ShouldBeFalse("glyph index outside the font");
        hdmx.TryGetAdvance(10, -1, out _).ShouldBeFalse();
    }

    [Fact]
    public void DuplicatePixelSize_FirstRecordWins()
    {
        var data = Synthetic();
        data[16] = 10;

        var hdmx = HdmxTable.TryParse(data, numGlyphs: 3)!;

        hdmx.PixelSizes.ShouldBe([10]);
        hdmx.TryGetAdvance(10, 0, out int advance).ShouldBeTrue();
        advance.ShouldBe(5);
    }

    [Fact]
    public void Malformed_IsIgnored()
    {
        HdmxTable.TryParse(new byte[7], 3).ShouldBeNull("shorter than the header");
        HdmxTable.TryParse(Synthetic(numRecords: 0), 3).ShouldBeNull("no records");
        HdmxTable.TryParse(Synthetic(numRecords: -1), 3).ShouldBeNull("negative record count");
        HdmxTable.TryParse(Synthetic(numRecords: 3), 3).ShouldBeNull("records past the table");
        HdmxTable.TryParse(Synthetic(sizeDeviceRecord: 4), 3).ShouldBeNull("a record too small for a width per glyph");
        HdmxTable.TryParse(Synthetic(sizeDeviceRecord: int.MaxValue), 3).ShouldBeNull("a record size that overflows");
        HdmxTable.TryParse(Synthetic(sizeDeviceRecord: -8), 3).ShouldBeNull("a negative record size");
        HdmxTable.TryParse(Synthetic(), 0).ShouldBeNull("no glyphs");
        HdmxTable.TryParse(Synthetic(), 7).ShouldBeNull("more glyphs than a record holds");
        HdmxTable.TryParse(Synthetic(version: 1), 3).ShouldNotBeNull("an unknown version reads the same layout");
    }

    public static TheoryData<string> SystemFontsWithHdmx => ["arial.ttf", "tahoma.ttf", "georgia.ttf"];

    [Theory]
    [MemberData(nameof(SystemFontsWithHdmx))]
    public void SystemFont_HasRecordsAtTextSizes(string file)
    {
        var face = NativeFontFace.Load(NativeBytecodeFontTests.SystemFont(file));
        var hdmx = HdmxTable.TryParse(face.Tables.GetTable("hdmx"), face.Maxp.NumGlyphs);

        hdmx.ShouldNotBeNull();
        hdmx.HasRecord(12).ShouldBeTrue();
        hdmx.TryGetAdvance(12, face.GetGlyphIndex('a'), out int advance).ShouldBeTrue();
        advance.ShouldBeInRange(3, 12);
    }

    [Fact]
    public void Damaged_NeverThrows()
    {
        // Every truncation of the synthetic table and of each system font's hdmx, plus
        // deterministic bit flips and byte runs over them: the parser returns a table or null.
        var tables = new List<(string Name, byte[] Bytes, int NumGlyphs)> { ("synthetic", Synthetic(), 3) };
        foreach (string file in new[] { "arial.ttf", "tahoma.ttf", "georgia.ttf" })
        {
            var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
            if (!File.Exists(path))
                continue;

            var face = NativeFontFace.Load(File.ReadAllBytes(path));
            tables.Add((file, face.Tables.GetTable("hdmx").ToArray(), face.Maxp.NumGlyphs));
        }

        foreach (var (name, bytes, numGlyphs) in tables)
        {
            int step = Math.Max(1, bytes.Length / 512);
            for (int length = 0; length <= bytes.Length; length += step)
                Should.NotThrow(() => Probe(HdmxTable.TryParse(bytes.AsMemory(0, length), numGlyphs), numGlyphs), $"{name} truncated to {length}");

            var rng = new Random(0x4864);
            for (int i = 0; i < 200; i++)
            {
                var damaged = (byte[])bytes.Clone();
                int at = rng.Next(Math.Min(damaged.Length, 64));
                damaged[at] ^= (byte)(1 << rng.Next(8));
                if (i % 3 == 0)
                {
                    int start = rng.Next(damaged.Length);
                    rng.NextBytes(damaged.AsSpan(start, Math.Min(8, damaged.Length - start)));
                }
                Should.NotThrow(() => Probe(HdmxTable.TryParse(damaged, numGlyphs), numGlyphs), $"{name} damaged, case {i}");
            }
        }
    }

    /// <summary>Reads every advance a parsed table claims to hold, so an out-of-bounds record surfaces.</summary>
    private static void Probe(HdmxTable? hdmx, int numGlyphs)
    {
        if (hdmx is null)
            return;

        foreach (int ppem in hdmx.PixelSizes)
        {
            for (int glyph = 0; glyph < numGlyphs; glyph += Math.Max(1, numGlyphs / 64))
                hdmx.TryGetAdvance(ppem, glyph, out _).ShouldBeTrue();
        }
    }
}
