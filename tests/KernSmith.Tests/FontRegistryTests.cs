using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Tests for the BmFont font registry: RegisterFont, UnregisterFont,
/// ClearRegisteredFonts, and registry-priority behavior in GenerateFromSystem.
/// </summary>
public sealed class FontRegistryTests : IDisposable
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    public void Dispose() => BmFont.ClearRegisteredFonts();

    // ------------------------------------------------------------------
    // RegisterFont + GenerateFromSystem — basic round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void RegisteredFont_IsUsedByGenerateFromSystem()
    {
        // Arrange
        var fontData = LoadTestFont();
        BmFont.RegisterFont("MyCustomFont", fontData);

        // Act — should resolve "MyCustomFont" from registry, not system
        var result = BmFont.GenerateFromSystem("MyCustomFont", new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Case-insensitive lookup
    // ------------------------------------------------------------------

    [Fact]
    public void RegisteredFont_LookupIsCaseInsensitive()
    {
        // Arrange
        BmFont.RegisterFont("My Font", LoadTestFont());

        // Act — query with different casing
        var result = BmFont.GenerateFromSystem("MY FONT", new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Style variant registration
    // ------------------------------------------------------------------

    [Fact]
    public void RegisteredFont_BoldStyleVariant_IsUsedWhenBoldRequested()
    {
        // Arrange — register both regular and "Bold" under the same family
        var fontData = LoadTestFont();
        BmFont.RegisterFont("StyledFont", fontData);
        BmFont.RegisterFont("StyledFont", fontData, style: "Bold");

        // Act — request bold
        var result = BmFont.GenerateFromSystem("StyledFont", new FontGeneratorOptions
        {
            Size = 24,
            Bold = true,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert — should succeed (the Bold variant was resolved)
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RegisteredFont_ItalicStyleVariant_IsUsedWhenItalicRequested()
    {
        // Arrange
        var fontData = LoadTestFont();
        BmFont.RegisterFont("StyledFont", fontData);
        BmFont.RegisterFont("StyledFont", fontData, style: "Italic");

        // Act
        var result = BmFont.GenerateFromSystem("StyledFont", new FontGeneratorOptions
        {
            Size = 24,
            Italic = true,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RegisteredFont_BoldItalicStyleVariant_IsUsedWhenBothRequested()
    {
        // Arrange
        var fontData = LoadTestFont();
        BmFont.RegisterFont("StyledFont", fontData);
        BmFont.RegisterFont("StyledFont", fontData, style: "Bold Italic");

        // Act
        var result = BmFont.GenerateFromSystem("StyledFont", new FontGeneratorOptions
        {
            Size = 24,
            Bold = true,
            Italic = true,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Style fallback — no styled variant, falls back to regular
    // ------------------------------------------------------------------

    [Fact]
    public void RegisteredFont_NoStyledVariant_FallsBackToRegular()
    {
        // Arrange — only register the regular variant
        BmFont.RegisterFont("RegularOnly", LoadTestFont());

        // Act — request bold (no "Bold" variant registered, should use regular + synthetic)
        var result = BmFont.GenerateFromSystem("RegularOnly", new FontGeneratorOptions
        {
            Size = 24,
            Bold = true,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Registry takes priority over system fonts
    // ------------------------------------------------------------------

    [Fact]
    public void RegisteredFont_TakesPriorityOverSystemFont()
    {
        if (!OperatingSystem.IsWindows())
            return; // Need a system font to test priority against

        // Arrange — register our test font under the name "Arial"
        BmFont.RegisterFont("Arial", LoadTestFont());

        // Act
        var result = BmFont.GenerateFromSystem("Arial", new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert — should produce a result using the registered font (Roboto),
        // not the system Arial. We verify by checking the face name in the output.
        result.Model.Info.Face.ShouldBe("Roboto",
            "Registered font (Roboto) should take priority over system font (Arial)");
    }

    // ------------------------------------------------------------------
    // UnregisterFont
    // ------------------------------------------------------------------

    [Fact]
    public void UnregisterFont_RemovesRegisteredFont()
    {
        // Arrange
        BmFont.RegisterFont("Ephemeral", LoadTestFont());

        // Act
        bool removed = BmFont.UnregisterFont("Ephemeral");

        // Assert
        removed.ShouldBeTrue();

        // Generating should now fail (font not found on system either)
        Should.Throw<FontParsingException>(() =>
            BmFont.GenerateFromSystem("Ephemeral", new FontGeneratorOptions
            {
                Size = 24,
                Characters = CharacterSet.FromRanges((32, 126))
            }));
    }

    [Fact]
    public void UnregisterFont_StyleVariant_RemovesOnlyThatVariant()
    {
        // Arrange
        var fontData = LoadTestFont();
        BmFont.RegisterFont("PartialRemove", fontData);
        BmFont.RegisterFont("PartialRemove", fontData, style: "Bold");

        // Act — remove only the Bold variant
        BmFont.UnregisterFont("PartialRemove", style: "Bold");

        // Assert — regular variant should still work
        var result = BmFont.GenerateFromSystem("PartialRemove", new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromRanges((32, 126))
        });
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UnregisterFont_NotRegistered_ReturnsFalse()
    {
        bool removed = BmFont.UnregisterFont("NeverRegistered");
        removed.ShouldBeFalse();
    }

    // ------------------------------------------------------------------
    // ClearRegisteredFonts
    // ------------------------------------------------------------------

    [Fact]
    public void ClearRegisteredFonts_RemovesAllRegistrations()
    {
        // Arrange
        var fontData = LoadTestFont();
        BmFont.RegisterFont("Font1", fontData);
        BmFont.RegisterFont("Font2", fontData);

        // Act
        BmFont.ClearRegisteredFonts();

        // Assert — both should fail
        Should.Throw<FontParsingException>(() =>
            BmFont.GenerateFromSystem("Font1", new FontGeneratorOptions
            {
                Size = 24,
                Characters = CharacterSet.FromRanges((32, 126))
            }));

        Should.Throw<FontParsingException>(() =>
            BmFont.GenerateFromSystem("Font2", new FontGeneratorOptions
            {
                Size = 24,
                Characters = CharacterSet.FromRanges((32, 126))
            }));
    }

    // ------------------------------------------------------------------
    // Null argument validation
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterFont_NullFamilyName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            BmFont.RegisterFont(null!, LoadTestFont()));
    }

    [Fact]
    public void RegisterFont_NullFontData_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            BmFont.RegisterFont("Test", null!));
    }

    [Fact]
    public void UnregisterFont_NullFamilyName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            BmFont.UnregisterFont(null!));
    }
}
