using System.Buffers.Binary;
using KernSmith.Font;
using Shouldly;
using static KernSmith.Tests.Font.Woff2TestSupport;

namespace KernSmith.Tests.Font;

/// <summary>
/// WOFF2 decoding (W3C WOFF2, RFC 7932 Brotli) checked against the source TTF of each fixture:
/// the WOFF2 files were produced by fontTools, an independent encoder, so agreement here means
/// the transforms are reversed the way the spec defines them, not just the way we wrote them.
/// </summary>
public class Woff2DecompressorTests
{
    public static TheoryData<string> Fixtures() => new(FixtureNames);

    // ──────────────────────────────────────────────
    // The fixtures exercise what they claim to
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("Roboto-Regular", false)]
    [InlineData("NotoSans-Regular-Latin", true)]
    [InlineData("RobotoFlex-Latin", true)]
    public void Fixture_UsesTheGlyfAndExpectedHmtxTransform(string name, bool hmtxTransformed)
    {
        var entries = ParseWoff2(Woff2(name)).Entries.ToDictionary(e => e.Tag);

        entries["glyf"].TransformVersion.ShouldBe(0, "glyf version 0 is the transformed glyf");
        entries["loca"].TransformVersion.ShouldBe(0);
        entries["loca"].TransformLength.ShouldBe(0);
        entries["hmtx"].TransformVersion.ShouldBe(hmtxTransformed ? 1 : 0);
    }

    [Fact]
    public void Fixtures_CoverCompositesInstructionsOverlapFlagsAndBothLocaFormats()
    {
        var roboto = ParseGlyphs(ReadTables(Ttf("Roboto-Regular")));
        var noto = ParseGlyphs(ReadTables(Ttf("NotoSans-Regular-Latin")));
        var flex = ParseGlyphs(ReadTables(Ttf("RobotoFlex-Latin")));

        roboto.Count(g => g.ContourCount < 0).ShouldBeGreaterThan(100, "Roboto should carry composite glyphs");
        noto.Count(g => g.Instructions.Length > 0).ShouldBeGreaterThan(100, "Noto Sans should be hinted");
        noto.Count(g => g.ContourCount < 0 && g.Instructions.Length > 0).ShouldBeGreaterThan(0,
            "Noto Sans should have composite glyphs with instructions");
        flex.Count(g => g.OverlapSimple).ShouldBeGreaterThan(100, "Roboto Flex should set OVERLAP_SIMPLE");

        IndexToLocFormat(Ttf("Roboto-Regular")).ShouldBe(1);
        IndexToLocFormat(Ttf("NotoSans-Regular-Latin")).ShouldBe(0);
    }

    // ──────────────────────────────────────────────
    // Decoded fonts match their source TTF
    // ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decompress_Fixture_TablesOtherThanGlyfAndLocaMatchTheTtf(string name)
    {
        var expected = ReadTables(Ttf(name));
        var actual = ReadTables(WoffDecompressor.Decompress(Woff2(name)));

        actual.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        foreach (var (tag, ttfBytes) in expected)
        {
            if (tag is "glyf" or "loca")
                continue;

            var expectedBytes = ttfBytes;
            var actualBytes = actual[tag];
            if (tag == "head")
            {
                // checkSumAdjustment covers the whole file, which legitimately differs; and the
                // WOFF2 spec requires the encoder to set head.flags bit 11 ("font data is
                // lossless-transformed") whenever it transforms glyf.
                actualBytes = (byte[])actualBytes.Clone();
                expectedBytes = (byte[])expectedBytes.Clone();
                expectedBytes.AsSpan(8, 4).CopyTo(actualBytes.AsSpan(8));
                expectedBytes[16] |= 0x08;
            }

            actualBytes.ShouldBe(expectedBytes, $"table '{tag}' should round-trip byte for byte");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decompress_Fixture_EveryGlyphMatchesTheTtf(string name)
    {
        var expected = ParseGlyphs(ReadTables(Ttf(name)));
        var actual = ParseGlyphs(ReadTables(WoffDecompressor.Decompress(Woff2(name))));

        actual.Length.ShouldBe(expected.Length);
        for (var g = 0; g < expected.Length; g++)
            actual[g].Describe().ShouldBe(expected[g].Describe(), $"glyph {g}");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decompress_Fixture_ProducesAValidSfntDirectoryAndChecksums(string name)
    {
        var sfnt = WoffDecompressor.Decompress(Woff2(name));
        var records = ReadTableRecords(sfnt);

        records.Select(r => r.Tag).ShouldBe(records.Select(r => r.Tag).Order(StringComparer.Ordinal), "tables sorted by tag");
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        int searchRange = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(6));
        var expectedSearchRange = 16 * (1 << (int)Math.Floor(Math.Log2(numTables)));
        searchRange.ShouldBe(expectedSearchRange);

        foreach (var record in records)
        {
            (record.Offset % 4).ShouldBe(0, $"'{record.Tag}' must start on a 4-byte boundary");
            var data = sfnt.AsSpan(record.Offset, record.Length).ToArray();
            if (record.Tag == "head")
                data.AsSpan(8, 4).Clear();
            record.Checksum.ShouldBe(TableChecksum(data), $"checksum of '{record.Tag}'");
        }

        TableChecksum(sfnt).ShouldBe(0xB1B0AFBAu, "head.checkSumAdjustment must balance the whole-font checksum");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReadFontInfo_FromWoff2_MatchesTheTtf(string name)
    {
        var expected = BmFont.ReadFontInfo(Ttf(name));
        var actual = BmFont.ReadFontInfo(Woff2(name));

        actual.FamilyName.ShouldBe(expected.FamilyName);
        actual.NumGlyphs.ShouldBe(expected.NumGlyphs);
        actual.UnitsPerEm.ShouldBe(expected.UnitsPerEm);
        actual.Ascender.ShouldBe(expected.Ascender);
        actual.Descender.ShouldBe(expected.Descender);
        actual.AvailableCodepoints.ShouldBe(expected.AvailableCodepoints, "cmap coverage");
        actual.KerningPairs.ShouldBe(expected.KerningPairs, "kerning pairs");
        expected.KerningPairs.Count.ShouldBeGreaterThan(0, "the fixture should actually carry kerning");
        actual.DesignMetrics.OrderBy(kv => kv.Key).ShouldBe(expected.DesignMetrics.OrderBy(kv => kv.Key), "hmtx advances and bearings");
    }

    // ──────────────────────────────────────────────
    // Null transforms and collections (test-built)
    // ──────────────────────────────────────────────

    [Fact]
    public void Decompress_NullTransformWoff2_ReproducesEveryTableExactly()
    {
        var ttf = Ttf("NotoSans-Regular-Latin");
        var woff2 = BuildNullTransformWoff2(ttf);

        var actual = ReadTables(WoffDecompressor.Decompress(woff2));

        foreach (var (tag, expected) in ReadTables(ttf))
        {
            var bytes = actual[tag];
            if (tag == "head")
            {
                bytes = (byte[])bytes.Clone();
                expected.AsSpan(8, 4).CopyTo(bytes.AsSpan(8));
            }

            bytes.ShouldBe(expected, $"null-transformed '{tag}'");
        }
    }

    [Fact]
    public void Decompress_Woff2Collection_ProducesATtcWithEachFont()
    {
        var noto = Ttf("NotoSans-Regular-Latin");
        var flex = Ttf("RobotoFlex-Latin");
        var woff2 = BuildNullTransformWoff2(noto, flex, noto);

        var ttc = WoffDecompressor.Decompress(woff2);

        BinaryPrimitives.ReadUInt32BigEndian(ttc).ShouldBe(0x74746366u, "a WOFF2 collection decodes to a 'ttcf' file");
        BinaryPrimitives.ReadUInt32BigEndian(ttc.AsSpan(8)).ShouldBe(3u);
        var sources = new[] { noto, flex, noto };
        for (var face = 0; face < 3; face++)
        {
            var directory = (int)BinaryPrimitives.ReadUInt32BigEndian(ttc.AsSpan(12 + 4 * face));
            var tables = ReadTables(ttc, directory);
            foreach (var (tag, expected) in ReadTables(sources[face]))
            {
                var bytes = tables[tag];
                if (tag == "head")
                {
                    bytes = (byte[])bytes.Clone();
                    expected.AsSpan(8, 4).CopyTo(bytes.AsSpan(8));
                }

                bytes.ShouldBe(expected, $"face {face} table '{tag}'");
            }

            BmFont.ReadFontInfo(ttc, face).FamilyName.ShouldBe(BmFont.ReadFontInfo(sources[face]).FamilyName);
        }
    }

    [Fact]
    public void Decompress_Woff2Collection_StoresSharedTablesOnce()
    {
        var noto = Ttf("NotoSans-Regular-Latin");
        var woff2 = BuildNullTransformWoff2(noto, noto);
        ParseWoff2(woff2).Entries.Count.ShouldBe(ReadTableRecords(noto).Count, "the builder should share every table");

        var ttc = WoffDecompressor.Decompress(woff2);

        var first = ReadTableRecords(ttc, (int)BinaryPrimitives.ReadUInt32BigEndian(ttc.AsSpan(12)));
        var second = ReadTableRecords(ttc, (int)BinaryPrimitives.ReadUInt32BigEndian(ttc.AsSpan(16)));
        second.Select(r => r.Offset).ShouldBe(first.Select(r => r.Offset), "shared tables should point at the same data");
    }

    // ──────────────────────────────────────────────
    // Malformed input
    // ──────────────────────────────────────────────

    [Fact]
    public void Decompress_HeaderOnly_ThrowsFontParsingException()
    {
        var data = new byte[48];
        "wOF2"u8.CopyTo(data);

        Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(data));
    }

    [Fact]
    public void Decompress_LengthFieldDisagreesWithData_Throws()
    {
        var woff2 = (byte[])Woff2("RobotoFlex-Latin").Clone();
        BinaryPrimitives.WriteUInt32BigEndian(woff2.AsSpan(8), (uint)woff2.Length + 4);

        var ex = Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(woff2));
        ex.Message.ShouldContain("length", Case.Insensitive);
    }

    [Fact]
    public void Decompress_UnknownTransformVersion_Throws()
    {
        // GDEF with transform version 1: only glyf/loca/hmtx define non-null transforms.
        var woff2 = BuildNullTransformWoff2(Ttf("NotoSans-Regular-Latin"));
        ParseWoff2(woff2).Entries[0].Tag.ShouldBe("GDEF", "the builder writes tables in ordinal tag order");
        const int FirstEntryFlags = 48;
        woff2[FirstEntryFlags] |= 1 << 6;

        var ex = Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(woff2));
        ex.Message.ShouldContain("transform", Case.Insensitive);
    }

    [Fact]
    public void Decompress_EveryTruncation_ThrowsFontParsingException()
    {
        var woff2 = Woff2("RobotoFlex-Latin");
        for (var length = 0; length < woff2.Length; length += 37)
        {
            var truncated = woff2.AsSpan(0, length).ToArray();
            Should.Throw<FontParsingException>(() => WoffDecompressor.Decompress(truncated), $"truncated to {length} bytes");
        }
    }

    [Theory]
    [InlineData("glyf")]
    [InlineData("hmtx")]
    public async Task Decompress_CorruptTransformedTable_NeverThrowsAnythingButFontParsingException(string tag)
    {
        await Task.Run(() =>
        {
            var source = Woff2("RobotoFlex-Latin");
            var random = new Random(178);
            var accepted = 0;
            var rejected = 0;
            for (var iteration = 0; iteration < 600; iteration++)
            {
                var corrupted = RewrapWithMutatedStream(source, (stream, layout) =>
                {
                    var (entry, offset) = layout.Single(l => l.Entry.Tag == tag);
                    var length = entry.TransformLength ?? entry.OrigLength;
                    for (var k = 0; k < 1 + random.Next(4); k++)
                    {
                        // Bias toward the headers and size fields at the front of the table.
                        var at = offset + (random.Next(3) == 0 ? random.Next(Math.Min(64, length)) : random.Next(length));
                        stream[at] = (byte)random.Next(256);
                    }
                });

                try
                {
                    WoffDecompressor.Decompress(corrupted);
                    accepted++;
                }
                catch (FontParsingException)
                {
                    rejected++;
                }
                catch (Exception ex)
                {
                    throw new ShouldAssertException(
                        $"{tag} corruption {iteration}: expected FontParsingException, got {ex.GetType().Name}: {ex.Message}", ex);
                }
            }

            // Both outcomes must occur, or the corruption never reached the transform code
            // (all rejected up front) or never touched anything it validates (all accepted).
            accepted.ShouldBeGreaterThan(0);
            rejected.ShouldBeGreaterThan(0);
        }, TestContext.Current.CancellationToken);
    }

    private static int IndexToLocFormat(byte[] sfnt) =>
        BinaryPrimitives.ReadInt16BigEndian(ReadTables(sfnt)["head"].AsSpan(50));
}
