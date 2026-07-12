using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Covers <see cref="DefaultSystemFontProvider.AddResolvedFontHint"/> (issue #152), the
/// provider-level backing for the public <see cref="KernSmith.BmFont.HintFontLocation"/> API:
/// a consumer-supplied family -&gt; path hint is stored directly in <c>_resolvedFontCache</c>
/// and validated on lookup exactly like any other cache/seed entry — a wrong hint costs one
/// bounded failed check and falls through to normal resolution, never a false resolution.
/// </summary>
public class DefaultSystemFontProviderHintTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureFontPath;

    public DefaultSystemFontProviderHintTests()
    {
        _tempDir = Path.Join(Path.GetTempPath(), "kernsmith-hint-tests-" + Guid.NewGuid().ToString("N")[..8]);
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
    public void LoadFont_ValidHint_ResolvesWithoutScanning()
    {
        // Arrange — do NOT rely on GetInstalledFonts() ever running.
        var provider = new DefaultSystemFontProvider();
        provider.AddResolvedFontHint("Roboto", _fixtureFontPath, faceIndex: 0);

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        result.FaceIndex.ShouldBe(0);

        // Proves the hint resolved before registry/heuristic/full-scan ever ran.
        provider._cachedFonts.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_HintFileMissing_FallsThroughCleanlyWithoutThrowing()
    {
        // Arrange — hint points at a file that doesn't exist; no real fonts either,
        // so resolution is fully deterministic.
        var provider = new DefaultSystemFontProvider();
        var family = "HintTestMissing_" + Guid.NewGuid().ToString("N")[..8];
        provider.AddResolvedFontHint(family, Path.Join(_tempDir, "does-not-exist.ttf"));
        provider._cachedFonts = new List<KernSmith.Font.Models.SystemFontInfo>();

        // Act
        var act = () => provider.LoadFont(family);

        // Assert
        var result = Should.NotThrow(act);
        result.ShouldBeNull();
        provider._resolvedFontCache.ContainsKey(family).ShouldBeFalse();
    }

    [Fact]
    public void LoadFont_HintFamilyMismatch_FallsThroughCleanlyWithoutThrowing()
    {
        // Arrange — hint's file exists but its real embedded family ("Roboto") doesn't
        // match the family name the hint was registered under.
        var provider = new DefaultSystemFontProvider();
        var family = "HintTestMismatch_" + Guid.NewGuid().ToString("N")[..8];
        provider.AddResolvedFontHint(family, _fixtureFontPath);
        provider._cachedFonts = new List<KernSmith.Font.Models.SystemFontInfo>();

        // Act
        var result = provider.LoadFont(family);

        // Assert
        result.ShouldBeNull();
        provider._resolvedFontCache.ContainsKey(family).ShouldBeFalse();
    }

    [Fact]
    public void LoadFont_ValidHint_TakesPriorityOverSeedTable()
    {
        // Arrange — a seed candidate for the same family also exists and would resolve
        // validly, but the hint (already in _resolvedFontCache) must be consulted first
        // and short-circuit before the seed table is ever considered.
        var seedPath = Path.Join(_tempDir, "seed-copy.ttf");
        File.Copy(_fixtureFontPath, seedPath);

        var provider = new DefaultSystemFontProvider();
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Roboto"] = new[] { seedPath }
        };
        provider.AddResolvedFontHint("Roboto", _fixtureFontPath);

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        provider._resolvedFontCache["Roboto"].FilePath.ShouldBe(_fixtureFontPath);
    }
}
