using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

[Collection("RasterizerFactory")]
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
        parser.CmapTable.Keys.ShouldBeSubsetOf(requested,
            "cmap should only contain codepoints that were requested");
        parser.CmapTable.ContainsKey(65).ShouldBeTrue();
        parser.CmapTable.ContainsKey(66).ShouldBeTrue();
        parser.CmapTable.ContainsKey(67).ShouldBeTrue();
    }

    [Fact]
    public void Parser_WithNullRequestedCodepoints_ReturnsAllCodepoints()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var parser = new TtfParser(fontData, 0, requestedCodepoints: null);

        // Assert — Roboto Regular has hundreds of cmap entries; with no filter all are returned
        parser.CmapTable.Count.ShouldBeGreaterThan(100,
            "unfiltered parse should return the full cmap table");
        parser.CmapTable.ContainsKey(65).ShouldBeTrue();
        parser.CmapTable.ContainsKey(97).ShouldBeTrue();
        parser.CmapTable.ContainsKey(32).ShouldBeTrue();
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
        subsetParser.CmapTable.Count.ShouldBeLessThan(fullParser.CmapTable.Count,
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
            subsetParser.CmapTable[cp].ShouldBe(fullParser.CmapTable[cp],
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
        parser.CmapTable.ShouldBeEmpty(
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
        parser.CmapTable.ShouldBeEmpty(
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
        subsetParser.GposPairs.Count.ShouldBeLessThanOrEqualTo(fullParser.GposPairs.Count,
            "subset GPOS pairs should not exceed full GPOS pairs");

        // All subset GPOS pairs should reference only glyph indices from the subset cmap
        var subsetGlyphIndices = new HashSet<int>(subsetParser.CmapTable.Values);
        foreach (var pair in subsetParser.GposPairs)
        {
            subsetGlyphIndices.ShouldContain(pair.LeftCodepoint,
                "left glyph of GPOS pair should be in the subset");
            subsetGlyphIndices.ShouldContain(pair.RightCodepoint,
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
            subsetGlyphIndices.ShouldContain(pair.LeftCodepoint,
                "left glyph of kern pair should be in the subset");
            subsetGlyphIndices.ShouldContain(pair.RightCodepoint,
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
        parser.KernPairs.ShouldBeEmpty(
            "empty subset should yield no kern pairs");
        parser.GposPairs.ShouldBeEmpty(
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
        subsetParser.Head.ShouldBe(fullParser.Head,
            "head table should not be affected by subsetting");
        subsetParser.Hhea.ShouldBe(fullParser.Hhea,
            "hhea table should not be affected by subsetting");
        subsetParser.Os2!.WeightClass.ShouldBe(fullParser.Os2!.WeightClass);
        subsetParser.Os2!.TypoAscender.ShouldBe(fullParser.Os2!.TypoAscender);
        subsetParser.Os2!.TypoDescender.ShouldBe(fullParser.Os2!.TypoDescender);
        subsetParser.Os2!.Panose.ShouldBe(fullParser.Os2!.Panose);
        subsetParser.Names.ShouldBe(fullParser.Names,
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
        hashSet.Count.ShouldBe(3);
        hashSet.ShouldContain(65, "should contain 'A'");
        hashSet.ShouldContain(66, "should contain 'B'");
        hashSet.ShouldContain(67, "should contain 'C'");
    }

    [Fact]
    public void CharacterSet_GetCodepointsHashSet_AsciiHas95Codepoints()
    {
        // Arrange & Act
        var hashSet = CharacterSet.Ascii.GetCodepointsHashSet();

        // Assert
        hashSet.Count.ShouldBe(95,
            "printable ASCII is U+0020..U+007E = 95 codepoints");
        hashSet.ShouldContain(0x0020, "should contain space");
        hashSet.ShouldContain(0x007E, "should contain tilde");
        hashSet.ShouldNotContain(0x001F, "should not contain control characters below space");
        hashSet.ShouldNotContain(0x007F, "should not contain DEL");
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
        hashSet1.ShouldBeSameAs(hashSet2,
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
        fontInfo.AvailableCodepoints.Count.ShouldBe(3,
            "only the 3 requested codepoints should appear in AvailableCodepoints");
        fontInfo.AvailableCodepoints.ShouldContain(65);
        fontInfo.AvailableCodepoints.ShouldContain(66);
        fontInfo.AvailableCodepoints.ShouldContain(67);
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
        fontInfo.AvailableCodepoints.Count.ShouldBeGreaterThan(100,
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
        result.Model.Characters.Count.ShouldBe(4);
        result.Model.Characters.Select(c => c.Id).ShouldBe(
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

        subsetA.Width.ShouldBe(fullA.Width, "glyph width for 'A' should match");
        subsetA.Height.ShouldBe(fullA.Height, "glyph height for 'A' should match");
        subsetA.XAdvance.ShouldBe(fullA.XAdvance, "xAdvance for 'A' should match");
        subsetA.XOffset.ShouldBe(fullA.XOffset, "xOffset for 'A' should match");
        subsetA.YOffset.ShouldBe(fullA.YOffset, "yOffset for 'A' should match");
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
            subsetAV.ShouldNotBeNull(
                "kerning pair A-V should be present in subset if present in full result");
            subsetAV!.Amount.ShouldBe(fullAV.Amount,
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
            charIds.ShouldContain(pair.First,
                $"kerning pair first={pair.First} should be in the character set");
            charIds.ShouldContain(pair.Second,
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
        subsetResult.Model.Characters.Count.ShouldBeLessThan(fullResult.Model.Characters.Count,
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
        result.Model.Characters.Count.ShouldBe(1);
        result.Model.Characters[0].Id.ShouldBe('X');
        result.Pages.Count.ShouldBeGreaterThan(0);
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
        bytesParser.CmapTable.ShouldBe(spanParser.CmapTable,
            "both constructors should produce the same cmap");
        bytesParser.Head.ShouldBe(spanParser.Head,
            "both constructors should produce the same head table");
    }
}
