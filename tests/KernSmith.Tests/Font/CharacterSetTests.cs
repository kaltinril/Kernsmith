using Shouldly;

namespace KernSmith.Tests.Font;

public class CharacterSetTests
{
    [Fact]
    public void Ascii_Contains95Characters()
    {
        // Act & Assert
        CharacterSet.Ascii.Count.ShouldBe(95);
    }

    [Fact]
    public void Ascii_ContainsSpace()
    {
        // Act & Assert
        CharacterSet.Ascii.GetCodepoints().ShouldContain(32, "ASCII set should include space (U+0020)");
    }

    [Fact]
    public void Ascii_ContainsTilde()
    {
        // Act & Assert
        CharacterSet.Ascii.GetCodepoints().ShouldContain(126, "ASCII set should include tilde (U+007E)");
    }

    [Fact]
    public void Ascii_DoesNotContainDel()
    {
        // Act & Assert
        CharacterSet.Ascii.GetCodepoints().ShouldNotContain(127, "ASCII set should not include DEL (U+007F)");
    }

    [Fact]
    public void FromRanges_CreatesCorrectSet()
    {
        // Arrange & Act
        var set = CharacterSet.FromRanges((65, 90));

        // Assert
        set.Count.ShouldBe(26);
    }

    [Fact]
    public void FromChars_String_Deduplicates()
    {
        // Arrange & Act
        var set = CharacterSet.FromChars("AABB");

        // Assert
        set.Count.ShouldBe(2);
    }

    [Fact]
    public void FromChars_Codepoints_Works()
    {
        // Arrange & Act
        var set = CharacterSet.FromChars(new[] { 65, 66, 67 });

        // Assert
        set.Count.ShouldBe(3);
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
        union.Count.ShouldBe(11);
    }

    [Fact]
    public void GetCodepoints_ReturnsSorted()
    {
        // Arrange
        var set = CharacterSet.FromChars(new[] { 90, 65, 70, 32, 126 });

        // Act
        var codepoints = set.GetCodepoints().ToList();

        // Assert
        codepoints.ShouldBeInOrder(SortDirection.Ascending, "GetCodepoints should return codepoints sorted in ascending order");
    }

    [Fact]
    public void Resolve_FiltersToAvailable()
    {
        // Arrange
        var available = new List<int> { 65, 66, 67, 68 };

        // Act
        var resolved = CharacterSet.Ascii.Resolve(available).ToList();

        // Assert
        resolved.Count.ShouldBe(4);
        resolved.ShouldBe(new[] { 65, 66, 67, 68 });
    }
}
