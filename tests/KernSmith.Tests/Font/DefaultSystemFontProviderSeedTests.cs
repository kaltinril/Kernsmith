using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Covers Phase 2 of the lazy font resolution cache: seeding
/// <see cref="DefaultSystemFontProvider"/>'s <c>_resolvedFontCache</c> with UNVERIFIED
/// well-known candidate paths so the FIRST request for a common family (not just repeat
/// requests) can also skip the expensive registry/full-scan resolution chain. Every seeded
/// entry is validated exactly like any other cache entry before use, so an incorrect guess
/// costs one bounded failed check and falls through unchanged — these tests exercise that
/// contract via <c>SeedTableOverride</c>, a test seam that substitutes a fixture-controlled
/// candidate list for the real OS-specific <see cref="WellKnownFontSeeds"/> table.
/// </summary>
public class DefaultSystemFontProviderSeedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureFontPath;

    public DefaultSystemFontProviderSeedTests()
    {
        _tempDir = Path.Join(Path.GetTempPath(), "kernsmith-seed-tests-" + Guid.NewGuid().ToString("N")[..8]);
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
    public void LoadFont_SeedPathMissing_FallsThroughCleanlyWithoutThrowing()
    {
        // Arrange — seed candidate points at a file that doesn't exist; no real fonts either,
        // so resolution is fully deterministic (no dependency on the real OS's installed fonts).
        var provider = new DefaultSystemFontProvider();
        var family = "SeedTestMissing_" + Guid.NewGuid().ToString("N")[..8];
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [family] = new[] { Path.Join(_tempDir, "does-not-exist.ttf") }
        };
        provider._cachedFonts = new List<KernSmith.Font.Models.SystemFontInfo>();

        // Act
        var act = () => provider.LoadFont(family);

        // Assert
        var result = Should.NotThrow(act);
        result.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_SeedPathValid_ResolvesFromSeedWithoutScanning()
    {
        // Arrange — the seed candidate's actual embedded family ("Roboto") matches the key,
        // so validation succeeds on the very first call.
        var provider = new DefaultSystemFontProvider();
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Roboto"] = new[] { _fixtureFontPath }
        };

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));

        // Proves GetInstalledFonts() never ran — the seed resolved before registry/full-scan.
        provider._cachedFonts.ShouldBeNull();

        // Promoted into the confirmed cache, so the next call is a normal cache hit.
        provider._resolvedFontCache.ShouldContainKey("Roboto");
        provider._resolvedFontCache["Roboto"].FilePath.ShouldBe(_fixtureFontPath);
    }

    [Fact]
    public void LoadFont_SeedFirstCandidateInvalid_FallsBackToSecondCandidate()
    {
        // Arrange — first candidate doesn't exist; second is the valid fixture.
        var provider = new DefaultSystemFontProvider();
        var wrongPath = Path.Join(_tempDir, "does-not-exist.ttf");
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Roboto"] = new[] { wrongPath, _fixtureFontPath }
        };

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        provider._resolvedFontCache["Roboto"].FilePath.ShouldBe(_fixtureFontPath);
    }

    [Fact]
    public void LoadFont_FamilyNotInSeedTable_FallsThroughUnchanged()
    {
        // Arrange — an empty override means nothing is seeded for any family, proving
        // Phase 2 doesn't interfere with resolution for families outside the seed table.
        var provider = new DefaultSystemFontProvider();
        var family = "KernSmithZzzNotSeeded_" + Guid.NewGuid().ToString("N")[..8];
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        provider._cachedFonts = new List<KernSmith.Font.Models.SystemFontInfo>();

        // Act
        var result = provider.LoadFont(family);

        // Assert
        result.ShouldBeNull();
        provider._resolvedFontCache.ContainsKey(family).ShouldBeFalse();
    }

    [Fact]
    public void LoadFont_SeedMiss_NotRetriedEvenIfPathBecomesValidLater()
    {
        // Arrange — seed path doesn't exist yet, and there's no real font to fall back to.
        var provider = new DefaultSystemFontProvider();
        var seedPath = Path.Join(_tempDir, "arrives-later.ttf");
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Roboto"] = new[] { seedPath }
        };
        provider._cachedFonts = new List<KernSmith.Font.Models.SystemFontInfo>();

        var first = provider.LoadFont("Roboto");
        first.ShouldBeNull();

        // The seed path now exists and would validate (embedded family is "Roboto") if
        // seeding were retried.
        File.Copy(_fixtureFontPath, seedPath);

        // Act
        var second = provider.LoadFont("Roboto");

        // Assert — a failed seed attempt must not be retried within the same provider instance.
        second.ShouldBeNull();
    }
}
