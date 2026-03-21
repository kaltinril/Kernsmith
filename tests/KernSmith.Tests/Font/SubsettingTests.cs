using KernSmith.Font;
using FluentAssertions;

namespace KernSmith.Tests.Font;

public class SubsettingTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ──────────────────────────────────────────────
    // TtfParser: cmap filtering
    // ──────────────────────────────────────────────

    [Fact]
    public void Parser_WithRequestedCodepoints_FiltersCmapToOnlyThoseCodepoints()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int> { 65, 66, 67 }; // A, B, C

        // Act
        var parser = new TtfParser(fontData, 0, requested);

        // Assert
        parser.CmapTable.Keys.Should().BeSubsetOf(requested,
            "cmap should only contain codepoints that were requested");
        parser.CmapTable.Should().ContainKey(65, "requested codepoint 'A' should be present");
        parser.CmapTable.Should().ContainKey(66, "requested codepoint 'B' should be present");
        parser.CmapTable.Should().ContainKey(67, "requested codepoint 'C' should be present");
    }

    [Fact]
    public void Parser_WithNullRequestedCodepoints_ReturnsAllCodepoints()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var parser = new TtfParser(fontData, 0, requestedCodepoints: null);

        // Assert — Roboto Regular has hundreds of cmap entries; with no filter all are returned
        parser.CmapTable.Count.Should().BeGreaterThan(100,
            "unfiltered parse should return the full cmap table");
        parser.CmapTable.Should().ContainKey(65, "full cmap should contain 'A'");
        parser.CmapTable.Should().ContainKey(97, "full cmap should contain 'a'");
        parser.CmapTable.Should().ContainKey(32, "full cmap should contain space");
    }

    [Fact]
    public void Parser_SubsetIsSmaller_ThanFullParse()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int> { 65, 66, 67 }; // A, B, C

        // Act
        var fullParser = new TtfParser(fontData, 0, requestedCodepoints: null);
        var subsetParser = new TtfParser(fontData, 0, requested);

        // Assert
        subsetParser.CmapTable.Count.Should().BeLessThan(fullParser.CmapTable.Count,
            "subset cmap should be smaller than full cmap");
    }

    [Fact]
    public void Parser_SubsetGlyphIndices_MatchFullParseForSameCodepoints()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int> { 65, 66, 67 };

        // Act
        var fullParser = new TtfParser(fontData, 0, requestedCodepoints: null);
        var subsetParser = new TtfParser(fontData, 0, requested);

        // Assert — glyph indices for the same codepoints should be identical
        foreach (var cp in requested)
        {
            subsetParser.CmapTable[cp].Should().Be(fullParser.CmapTable[cp],
                $"glyph index for U+{cp:X4} should be the same whether subsetting or not");
        }
    }

    [Fact]
    public void Parser_WithEmptyRequestedCodepoints_ProducesEmptyCmap()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int>();

        // Act
        var parser = new TtfParser(fontData, 0, requested);

        // Assert
        parser.CmapTable.Should().BeEmpty(
            "requesting zero codepoints should produce an empty cmap");
    }

    [Fact]
    public void Parser_WithNonExistentCodepoints_ProducesEmptyCmap()
    {
        // Arrange
        var fontData = LoadTestFont();
        // U+FFFD is the replacement char — Roboto may or may not have it,
        // but U+FFFE is a non-character that no font should map
        var requested = new HashSet<int> { 0xFFFE, 0x10FFFE };

        // Act
        var parser = new TtfParser(fontData, 0, requested);

        // Assert
        parser.CmapTable.Should().BeEmpty(
            "requesting codepoints absent from the font should produce an empty cmap");
    }

    // ──────────────────────────────────────────────
    // TtfParser: kerning pair filtering
    // ──────────────────────────────────────────────

    [Fact]
    public void Parser_WithSubset_FiltersGposPairsToRelevantGlyphs()
    {
        // Arrange
        var fontData = LoadTestFont();
        // Use a small set that is likely to have GPOS kerning in Roboto
        var requested = new HashSet<int> { 65, 86 }; // A, V — a classic kerning pair

        // Act
        var fullParser = new TtfParser(fontData, 0, requestedCodepoints: null);
        var subsetParser = new TtfParser(fontData, 0, requested);

        // Assert — subset should have fewer or equal GPOS pairs
        subsetParser.GposPairs.Count.Should().BeLessThanOrEqualTo(fullParser.GposPairs.Count,
            "subset GPOS pairs should not exceed full GPOS pairs");

        // All subset GPOS pairs should reference only glyph indices from the subset cmap
        var subsetGlyphIndices = new HashSet<int>(subsetParser.CmapTable.Values);
        foreach (var pair in subsetParser.GposPairs)
        {
            subsetGlyphIndices.Should().Contain(pair.LeftCodepoint,
                "left glyph of GPOS pair should be in the subset");
            subsetGlyphIndices.Should().Contain(pair.RightCodepoint,
                "right glyph of GPOS pair should be in the subset");
        }
    }

    [Fact]
    public void Parser_WithSubset_FiltersKernPairsToRelevantGlyphs()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int> { 65, 86 }; // A, V

        // Act
        var subsetParser = new TtfParser(fontData, 0, requested);

        // Assert — all kern pairs should reference only glyph indices in the subset
        var subsetGlyphIndices = new HashSet<int>(subsetParser.CmapTable.Values);
        foreach (var pair in subsetParser.KernPairs)
        {
            subsetGlyphIndices.Should().Contain(pair.LeftCodepoint,
                "left glyph of kern pair should be in the subset");
            subsetGlyphIndices.Should().Contain(pair.RightCodepoint,
                "right glyph of kern pair should be in the subset");
        }
    }

    [Fact]
    public void Parser_WithEmptySubset_ProducesNoKerningPairs()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int>();

        // Act
        var parser = new TtfParser(fontData, 0, requested);

        // Assert
        parser.KernPairs.Should().BeEmpty(
            "empty subset should yield no kern pairs");
        parser.GposPairs.Should().BeEmpty(
            "empty subset should yield no GPOS pairs");
    }

    // ──────────────────────────────────────────────
    // TtfParser: metadata is unaffected by subsetting
    // ──────────────────────────────────────────────

    [Fact]
    public void Parser_WithSubset_PreservesMetadataTables()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int> { 65 };

        // Act
        var fullParser = new TtfParser(fontData, 0, requestedCodepoints: null);
        var subsetParser = new TtfParser(fontData, 0, requested);

        // Assert — head, hhea, OS/2, name should be identical
        subsetParser.Head.Should().BeEquivalentTo(fullParser.Head,
            "head table should not be affected by subsetting");
        subsetParser.Hhea.Should().BeEquivalentTo(fullParser.Hhea,
            "hhea table should not be affected by subsetting");
        subsetParser.Os2.Should().BeEquivalentTo(fullParser.Os2,
            "OS/2 table should not be affected by subsetting");
        subsetParser.Names.Should().BeEquivalentTo(fullParser.Names,
            "name table should not be affected by subsetting");
    }

    // ──────────────────────────────────────────────
    // CharacterSet.GetCodepointsHashSet
    // ──────────────────────────────────────────────

    [Fact]
    public void CharacterSet_GetCodepointsHashSet_ReturnsExpectedValues()
    {
        // Arrange
        var charSet = CharacterSet.FromChars("ABC");

        // Act
        var hashSet = charSet.GetCodepointsHashSet();

        // Assert
        hashSet.Should().HaveCount(3);
        hashSet.Should().Contain(65, "should contain 'A'");
        hashSet.Should().Contain(66, "should contain 'B'");
        hashSet.Should().Contain(67, "should contain 'C'");
    }

    [Fact]
    public void CharacterSet_GetCodepointsHashSet_AsciiHas95Codepoints()
    {
        // Arrange & Act
        var hashSet = CharacterSet.Ascii.GetCodepointsHashSet();

        // Assert
        hashSet.Should().HaveCount(95,
            "printable ASCII is U+0020..U+007E = 95 codepoints");
        hashSet.Should().Contain(0x0020, "should contain space");
        hashSet.Should().Contain(0x007E, "should contain tilde");
        hashSet.Should().NotContain(0x001F, "should not contain control characters below space");
        hashSet.Should().NotContain(0x007F, "should not contain DEL");
    }

    [Fact]
    public void CharacterSet_GetCodepointsHashSet_ReturnsSameInstanceOnMultipleCalls()
    {
        // Arrange
        var charSet = CharacterSet.FromChars("X");

        // Act
        var hashSet1 = charSet.GetCodepointsHashSet();
        var hashSet2 = charSet.GetCodepointsHashSet();

        // Assert — should return the same underlying set (not a copy)
        hashSet1.Should().BeSameAs(hashSet2,
            "GetCodepointsHashSet should return the same reference for efficiency");
    }

    // ──────────────────────────────────────────────
    // TtfFontReader: threading RequestedCodepoints
    // ──────────────────────────────────────────────

    [Fact]
    public void TtfFontReader_WithRequestedCodepoints_FiltersOutput()
    {
        // Arrange
        var fontData = LoadTestFont();
        var reader = new TtfFontReader();
        reader.RequestedCodepoints = new HashSet<int> { 65, 66, 67 }; // A, B, C
        reader.SharedFontBytes = fontData;

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert
        fontInfo.AvailableCodepoints.Should().HaveCount(3,
            "only the 3 requested codepoints should appear in AvailableCodepoints");
        fontInfo.AvailableCodepoints.Should().Contain(65);
        fontInfo.AvailableCodepoints.Should().Contain(66);
        fontInfo.AvailableCodepoints.Should().Contain(67);
    }

    [Fact]
    public void TtfFontReader_WithoutRequestedCodepoints_ReturnsAll()
    {
        // Arrange
        var fontData = LoadTestFont();
        var reader = new TtfFontReader();
        // RequestedCodepoints defaults to null

        // Act
        var fontInfo = reader.ReadFont(fontData);

        // Assert
        fontInfo.AvailableCodepoints.Should().HaveCountGreaterThan(100,
            "without subsetting, all codepoints should be returned");
    }

    // ──────────────────────────────────────────────
    // End-to-end: BmFont.Generate with subsetting
    // ──────────────────────────────────────────────

    [Fact]
    public void Generate_WithSmallCharacterSet_ProducesCorrectGlyphCount()
    {
        // Arrange
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("Hello");

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars
        });

        // Assert — "Hello" has 4 unique codepoints: H, e, l, o
        result.Model.Characters.Should().HaveCount(4);
        result.Model.Characters.Select(c => c.Id).Should().BeEquivalentTo(
            new[] { 'H', 'e', 'l', 'o' }.Select(c => (int)c));
    }

    [Fact]
    public void Generate_SubsetProducesSameGlyphs_AsFullSetForSameCodepoints()
    {
        // Arrange
        var fontData = LoadTestFont();
        var subsetChars = CharacterSet.FromChars("AV");
        var fullChars = CharacterSet.Ascii;

        // Act
        var subsetResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = subsetChars,
            Kerning = true
        });

        var fullResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = fullChars,
            Kerning = true
        });

        // Assert — the subset characters should have the same metrics as in the full set
        var subsetA = subsetResult.Model.Characters.First(c => c.Id == 65);
        var fullA = fullResult.Model.Characters.First(c => c.Id == 65);

        subsetA.Width.Should().Be(fullA.Width, "glyph width for 'A' should match");
        subsetA.Height.Should().Be(fullA.Height, "glyph height for 'A' should match");
        subsetA.XAdvance.Should().Be(fullA.XAdvance, "xAdvance for 'A' should match");
        subsetA.XOffset.Should().Be(fullA.XOffset, "xOffset for 'A' should match");
        subsetA.YOffset.Should().Be(fullA.YOffset, "yOffset for 'A' should match");
    }

    [Fact]
    public void Generate_SubsetKerning_MatchesFullKerningForSamePairs()
    {
        // Arrange
        var fontData = LoadTestFont();
        // A and V is one of the most common kerning pairs
        var subsetChars = CharacterSet.FromChars("AV");

        // Act
        var subsetResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = subsetChars,
            Kerning = true
        });

        var fullResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Kerning = true
        });

        // Assert — if AV kerning exists in full, it should also exist in subset with same value
        var fullAV = fullResult.Model.KerningPairs.FirstOrDefault(k => k.First == 65 && k.Second == 86);
        var subsetAV = subsetResult.Model.KerningPairs.FirstOrDefault(k => k.First == 65 && k.Second == 86);

        if (fullAV != null)
        {
            subsetAV.Should().NotBeNull(
                "kerning pair A-V should be present in subset if present in full result");
            subsetAV!.Amount.Should().Be(fullAV.Amount,
                "kerning amount for A-V should match between subset and full parse");
        }
    }

    [Fact]
    public void Generate_SubsetKerning_DoesNotContainPairsOutsideSubset()
    {
        // Arrange
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("AV");

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            Kerning = true
        });

        // Assert — all kerning pairs should reference only codepoints in the character set
        var charIds = new HashSet<int>(result.Model.Characters.Select(c => c.Id));
        foreach (var pair in result.Model.KerningPairs)
        {
            charIds.Should().Contain(pair.First,
                $"kerning pair first={pair.First} should be in the character set");
            charIds.Should().Contain(pair.Second,
                $"kerning pair second={pair.Second} should be in the character set");
        }
    }

    [Fact]
    public void Generate_SubsetHasFewerGlyphs_ThanFullAscii()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var subsetResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AB")
        });

        var fullResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
        });

        // Assert
        subsetResult.Model.Characters.Count.Should().BeLessThan(fullResult.Model.Characters.Count,
            "a 2-character subset should have fewer glyphs than full ASCII");
    }

    [Fact]
    public void Generate_SingleCharacterSubset_ProducesOneGlyph()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("X")
        });

        // Assert
        result.Model.Characters.Should().HaveCount(1);
        result.Model.Characters[0].Id.Should().Be('X');
        result.Pages.Should().HaveCountGreaterThan(0);
    }

    // ──────────────────────────────────────────────
    // SharedFontBytes path
    // ──────────────────────────────────────────────

    [Fact]
    public void Parser_SharedBytesConstructor_ProducesSameResult_AsSpanConstructor()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requested = new HashSet<int> { 65, 66, 67 };

        // Act
        var spanParser = new TtfParser((ReadOnlySpan<byte>)fontData, 0, requested);
        var bytesParser = new TtfParser(fontData, 0, requested);

        // Assert
        bytesParser.CmapTable.Should().BeEquivalentTo(spanParser.CmapTable,
            "both constructors should produce the same cmap");
        bytesParser.Head.Should().BeEquivalentTo(spanParser.Head,
            "both constructors should produce the same head table");
    }
}
