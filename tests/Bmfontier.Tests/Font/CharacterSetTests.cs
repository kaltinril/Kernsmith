using FluentAssertions;

namespace Bmfontier.Tests.Font;

public class CharacterSetTests
{
    [Fact]
    public void Ascii_Contains95Characters()
    {
        // Act & Assert
        CharacterSet.Ascii.Count.Should().Be(95);
    }

    [Fact]
    public void Ascii_ContainsSpace()
    {
        // Act & Assert
        CharacterSet.Ascii.GetCodepoints().Should().Contain(32, "ASCII set should include space (U+0020)");
    }

    [Fact]
    public void Ascii_ContainsTilde()
    {
        // Act & Assert
        CharacterSet.Ascii.GetCodepoints().Should().Contain(126, "ASCII set should include tilde (U+007E)");
    }

    [Fact]
    public void Ascii_DoesNotContainDel()
    {
        // Act & Assert
        CharacterSet.Ascii.GetCodepoints().Should().NotContain(127, "ASCII set should not include DEL (U+007F)");
    }

    [Fact]
    public void FromRanges_CreatesCorrectSet()
    {
        // Arrange & Act
        var set = CharacterSet.FromRanges((65, 90));

        // Assert
        set.Count.Should().Be(26, "A-Z is 26 characters");
    }

    [Fact]
    public void FromChars_String_Deduplicates()
    {
        // Arrange & Act
        var set = CharacterSet.FromChars("AABB");

        // Assert
        set.Count.Should().Be(2, "duplicate characters should be deduplicated");
    }

    [Fact]
    public void FromChars_Codepoints_Works()
    {
        // Arrange & Act
        var set = CharacterSet.FromChars(new[] { 65, 66, 67 });

        // Assert
        set.Count.Should().Be(3, "three distinct codepoints should produce count of 3");
    }

    [Fact]
    public void Union_CombinesSets()
    {
        // Arrange
        var set1 = CharacterSet.FromRanges((65, 70));  // A-F = 6 chars
        var set2 = CharacterSet.FromRanges((68, 75));  // D-K = 8 chars, overlap D-F = 3

        // Act
        var union = CharacterSet.Union(set1, set2);

        // Assert — 65..75 inclusive = 11 unique characters
        union.Count.Should().Be(11, "union of (65-70) and (68-75) should have 11 unique characters");
    }

    [Fact]
    public void GetCodepoints_ReturnsSorted()
    {
        // Arrange
        var set = CharacterSet.FromChars(new[] { 90, 65, 70, 32, 126 });

        // Act
        var codepoints = set.GetCodepoints().ToList();

        // Assert
        codepoints.Should().BeInAscendingOrder("GetCodepoints should return codepoints sorted in ascending order");
    }

    [Fact]
    public void Resolve_FiltersToAvailable()
    {
        // Arrange
        var available = new List<int> { 65, 66, 67, 68 };

        // Act
        var resolved = CharacterSet.Ascii.Resolve(available).ToList();

        // Assert
        resolved.Should().HaveCount(4, "only the 4 available codepoints should be returned");
        resolved.Should().BeEquivalentTo(new[] { 65, 66, 67, 68 });
    }
}
