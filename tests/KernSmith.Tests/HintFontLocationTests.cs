using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Tests for the public <see cref="BmFont.HintFontLocation"/> API (issue #152): lets a
/// consumer pre-populate the system font resolver's cache with a known file path for a
/// family name, as a lighter-weight alternative to <see cref="BmFont.RegisterFont"/> when
/// the consumer already knows where a font lives on disk.
/// </summary>
public sealed class HintFontLocationTests
{
    private static string FixtureFontPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    [Fact]
    public void HintedFont_IsUsedByGenerateFromSystem()
    {
        // Arrange — the hint's family name must match the fixture's actual embedded family
        // ("Roboto"), same as any other cache/seed entry's validate-before-trust check.
        BmFont.HintFontLocation("Roboto", FixtureFontPath);

        // Act
        var result = BmFont.GenerateFromSystem("Roboto", new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Model.Info.Face.ShouldBe("Roboto");

        // Proves the hint (not some other real system "Roboto" install, if one exists) is
        // what actually resolved — the resolved-font cache would only ever hold our fixture
        // path if the hint was consulted and validated at tier 1.
        BmFont.SystemFontProvider._resolvedFontCache["Roboto"].FilePath.ShouldBe(FixtureFontPath);
    }

    [Fact]
    public void HintedFont_WrongPath_FallsThroughAndFails()
    {
        // Arrange — the hinted file doesn't actually contain this family, so it must be
        // rejected by the same validate-before-trust check as any other cache entry, and
        // resolution must fall through to (and fail) normal system font lookup rather than
        // trusting the bad hint.
        var family = "HintedFontWrongPath_" + Guid.NewGuid().ToString("N")[..8];
        BmFont.HintFontLocation(family, Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.ttf"));

        Should.Throw<FontParsingException>(() =>
            BmFont.GenerateFromSystem(family, new FontGeneratorOptions
            {
                Size = 24,
                Characters = CharacterSet.FromRanges((32, 126))
            }));
    }

    [Fact]
    public void HintFontLocation_NullFamilyName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            BmFont.HintFontLocation(null!, FixtureFontPath));
    }

    [Fact]
    public void HintFontLocation_NullPath_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            BmFont.HintFontLocation("SomeFamily", null!));
    }
}
