using KernSmith.Font;
using KernSmith.Font.Models;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Covers Phase 3 of the lazy font resolution cache: a heuristic filename-targeted parse
/// tier that runs between the registry fast path and the expensive full directory scan.
/// It narrows candidates by filename first (cheap), then verifies with a real parse (the
/// filename match is only a hint — the parsed family name is the actual source of truth),
/// so an incorrect filename hint costs one bounded parse, never a false resolution.
///
/// Uses <c>FontDirectoriesOverride</c> — a test seam analogous to Phase 2's
/// <c>SeedTableOverride</c> — so these tests run against a fixture temp dir instead of the
/// real OS's actual font directories.
/// </summary>
public class DefaultSystemFontProviderHeuristicTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureFontPath;

    public DefaultSystemFontProviderHeuristicTests()
    {
        _tempDir = Path.Join(Path.GetTempPath(), "kernsmith-heuristic-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var sourcePath = Path.Join(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");
        _fixtureFontPath = Path.Join(_tempDir, "Roboto-Regular.ttf");
        File.Copy(sourcePath, _fixtureFontPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadFont_FilenameHintsButParseRejects_ReturnsNullWithoutFallingThrough()
    {
        // Arrange — a file literally named "Robotoxyz.ttf" trivially filename-matches a
        // query for "Robotoxyz", but its actual embedded family is "Roboto" (it's a copy
        // of the Roboto fixture), so the parse must reject it. This mirrors the classic
        // "Helvetica" vs "HelveticaNeue-Bold.ttf" decoy case using only the one real font
        // fixture this repo has, inverted per the task's own suggestion.
        var provider = new DefaultSystemFontProvider();
        var decoyPath = Path.Join(_tempDir, "Robotoxyz.ttf");
        File.Copy(_fixtureFontPath, decoyPath);
        provider.FontDirectoriesOverride = new List<string> { _tempDir };

        // A trap: if the heuristic tier incorrectly fell through to the full scan after
        // rejecting the decoy, this fabricated entry would satisfy the request and reveal
        // the bug by returning non-null.
        provider._cachedFonts = new List<SystemFontInfo>
        {
            new SystemFontInfo
            {
                FamilyName = "Robotoxyz",
                StyleName = "Regular",
                FilePath = _fixtureFontPath,
                FaceIndex = 0
            }
        };

        // Act
        var result = provider.LoadFont("Robotoxyz");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_NoFilenameNarrowedCandidates_FallsThroughToFullScanUnchanged()
    {
        // Arrange — an empty directory produces zero filename-narrowed candidates, so this
        // tier must fall through to the existing full-scan resolution chain unchanged.
        var provider = new DefaultSystemFontProvider();
        var emptyDir = Path.Join(_tempDir, "empty-heuristic-dir");
        Directory.CreateDirectory(emptyDir);
        provider.FontDirectoriesOverride = new List<string> { emptyDir };

        provider._cachedFonts = new List<SystemFontInfo>
        {
            new SystemFontInfo
            {
                FamilyName = "Roboto",
                StyleName = "Regular",
                FilePath = _fixtureFontPath,
                FaceIndex = 0
            }
        };

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert — resolved via the full-scan fallback, same as before Phase 3.
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        provider._resolvedFontCache.ShouldContainKey("Roboto");
    }

    [Fact]
    public void LoadFont_MultipleFilenameNarrowedCandidates_OnlyValidOneChosen()
    {
        // Arrange — a corrupt decoy also filename-matches "Roboto" alongside the genuine
        // fixture; only the genuine one should actually parse and resolve.
        var provider = new DefaultSystemFontProvider();
        var corruptPath = Path.Join(_tempDir, "Roboto-Corrupt.ttf");
        File.WriteAllBytes(corruptPath, new byte[] { 1, 2, 3, 4, 5 });
        provider.FontDirectoriesOverride = new List<string> { _tempDir };

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        provider._resolvedFontCache["Roboto"].FilePath.ShouldBe(_fixtureFontPath);
    }

    [Fact]
    public void LoadFont_StyleRequestedAndFoundAmongHeuristicMatches_Resolves()
    {
        // Arrange
        var provider = new DefaultSystemFontProvider();
        provider.FontDirectoriesOverride = new List<string> { _tempDir };

        // Act — the fixture's actual style is "Regular".
        var result = provider.LoadFont("Roboto", "Regular");

        // Assert
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
    }

    [Fact]
    public void LoadFont_StyleRequestedNotFoundAmongHeuristicMatches_ReturnsNullWithoutFallingThrough()
    {
        // Arrange — only a "Regular" style Roboto exists in the fixture dir.
        var provider = new DefaultSystemFontProvider();
        provider.FontDirectoriesOverride = new List<string> { _tempDir };

        // A trap: if the heuristic tier incorrectly fell through to the full scan after
        // finding no "Bold" match, this seeded entry would satisfy the request.
        provider._cachedFonts = new List<SystemFontInfo>
        {
            new SystemFontInfo
            {
                FamilyName = "Roboto",
                StyleName = "Bold",
                FilePath = _fixtureFontPath,
                FaceIndex = 0
            }
        };

        // Act
        var result = provider.LoadFont("Roboto", "Bold");

        // Assert — mirrors the registry tier's "family found, style not found" short circuit.
        result.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_SuccessfulHeuristicResolution_NoStyleRequested_PopulatesResolvedFontCache()
    {
        // Arrange
        var provider = new DefaultSystemFontProvider();
        provider.FontDirectoriesOverride = new List<string> { _tempDir };

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        provider._resolvedFontCache.ShouldContainKey("Roboto");
        provider._resolvedFontCache["Roboto"].FilePath.ShouldBe(_fixtureFontPath);

        // Proves GetInstalledFonts() never ran — resolved entirely via the heuristic tier.
        provider._cachedFonts.ShouldBeNull();
    }
}
