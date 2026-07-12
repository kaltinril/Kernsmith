using KernSmith.Font;
using KernSmith.Font.Models;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Phase 4: end-to-end integration proof for the full <c>LoadFont</c> resolution chain —
/// cache/seed -> registry -> heuristic filename match -> full scan. Each test configures
/// fixtures so that only ONE tier is actually capable of producing a non-null result (every
/// other tier's data source is deliberately empty or misdirected), proving real tier
/// ordering and write-back behavior rather than assuming it from reading the code.
///
/// Registry write-back is intentionally NOT covered here (or anywhere in this suite) — see
/// Phase 1's rationale: there is no registry mock in this codebase, and that decision hasn't
/// changed. It remains covered only by the full `dotnet test` run on real Windows registry
/// state.
/// </summary>
public class DefaultSystemFontProviderResolutionOrderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureFontPath;

    public DefaultSystemFontProviderResolutionOrderTests()
    {
        _tempDir = Path.Join(Path.GetTempPath(), "kernsmith-resolution-order-tests-" + Guid.NewGuid().ToString("N")[..8]);
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
    public void LoadFont_ResolvableOnlyViaSeed_ResolvesViaSeed_AndSecondCallIsPureCacheHit()
    {
        // Arrange — seed candidate is valid; the heuristic tier's directory is empty (no
        // filename-narrowed candidates possible) and the full-scan fallback list is empty
        // too, so seed is the ONLY tier capable of producing a non-null result.
        var emptyDir = Path.Join(_tempDir, "seed-only-empty");
        Directory.CreateDirectory(emptyDir);

        var provider = new DefaultSystemFontProvider();
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Roboto"] = new[] { _fixtureFontPath }
        };
        provider.FontDirectoriesOverride = new List<string> { emptyDir };
        // _cachedFonts is deliberately left unset (null) — if the seed tier failed to
        // short-circuit, GetInstalledFonts() would run a real scan and _cachedFonts would
        // become non-null, revealing the bug (same proof technique as Phases 1-2).

        // Act — first call
        var first = provider.LoadFont("Roboto");

        // Assert — resolved, and _cachedFonts staying null proves GetInstalledFonts() (the
        // full scan) never ran, so this could only have come from the seed tier.
        first.ShouldNotBeNull();
        first!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        provider._cachedFonts.ShouldBeNull();
        provider._resolvedFontCache.ShouldContainKey("Roboto");

        // Invalidate the seed source before the second call — if the second call somehow
        // bypassed _resolvedFontCache and re-ran seed resolution, it would now fail.
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        // Act — second call
        var second = provider.LoadFont("Roboto");

        // Assert — pure cache hit
        second.ShouldNotBeNull();
        second!.Data.ShouldBe(first.Data);
        provider._cachedFonts.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_ResolvableOnlyViaHeuristic_ResolvesViaHeuristic_AndSecondCallIsPureCacheHit()
    {
        // Arrange — not seeded; the fixture directory contains a filename-narrowable match
        // ("Roboto-Regular.ttf"); the full-scan fallback list is empty, so the heuristic
        // tier is the ONLY tier capable of producing a non-null result.
        var provider = new DefaultSystemFontProvider();
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        provider.FontDirectoriesOverride = new List<string> { _tempDir };
        // _cachedFonts is deliberately left unset (null) — same proof technique as above.

        // Act — first call
        var first = provider.LoadFont("Roboto");

        // Assert — resolved, and _cachedFonts staying null proves the full scan never ran.
        first.ShouldNotBeNull();
        first!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        provider._cachedFonts.ShouldBeNull();
        provider._resolvedFontCache.ShouldContainKey("Roboto");

        // Invalidate the heuristic tier's ability to REDISCOVER the file (point it at an
        // empty directory) without touching the actual file on disk — the confirmed cache
        // entry's validity check (IsCacheEntryValidCore) re-reads that same file on every
        // hit, so deleting it would invalidate the cache entry too, not just the heuristic
        // tier. This isolates "didn't need to re-scan the directory" from "the cached file
        // itself is still there," which is exactly what a pure cache hit means.
        var noLongerDiscoverableDir = Path.Join(_tempDir, "heuristic-now-empty");
        Directory.CreateDirectory(noLongerDiscoverableDir);
        provider.FontDirectoriesOverride = new List<string> { noLongerDiscoverableDir };

        // Act — second call
        var second = provider.LoadFont("Roboto");

        // Assert — pure cache hit
        second.ShouldNotBeNull();
        second!.Data.ShouldBe(first.Data);
        provider._cachedFonts.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_ResolvableOnlyViaFullScan_ResolvesViaFullScan_AndSecondCallIsPureCacheHit()
    {
        // Arrange — not seeded; the heuristic directory contains a copy of the fixture
        // under a deliberately misleading, non-matching filename ("font1.ttf") so the
        // heuristic tier finds ZERO filename-narrowed candidates and must fall through;
        // the full-scan fallback list (_cachedFonts) is the ONLY tier seeded with a match.
        var heuristicDir = Path.Join(_tempDir, "fullscan-only");
        Directory.CreateDirectory(heuristicDir);
        var misleadingPath = Path.Join(heuristicDir, "font1.ttf");
        File.Copy(_fixtureFontPath, misleadingPath);

        var provider = new DefaultSystemFontProvider();
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        provider.FontDirectoriesOverride = new List<string> { heuristicDir };
        provider._cachedFonts = new List<SystemFontInfo>
        {
            new SystemFontInfo
            {
                FamilyName = "Roboto",
                StyleName = "Regular",
                FilePath = misleadingPath,
                FaceIndex = 0
            }
        };

        // Act — first call
        var first = provider.LoadFont("Roboto");

        // Assert — resolved via the full-scan backstop, proving it still works after 3
        // phases of tiers were added in front of it.
        first.ShouldNotBeNull();
        first!.Data.ShouldBe(File.ReadAllBytes(misleadingPath));
        provider._resolvedFontCache.ShouldContainKey("Roboto");

        // Invalidate the full-scan source before the second call.
        provider._cachedFonts = null;

        // Act — second call
        var second = provider.LoadFont("Roboto");

        // Assert — pure cache hit (would otherwise require a real, unseeded full scan)
        second.ShouldNotBeNull();
        second!.Data.ShouldBe(first.Data);
        provider._cachedFonts.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_NotResolvableByAnyTier_ReturnsNull_NoThrow_NothingCached()
    {
        // Arrange — a family that cannot be found via seed, heuristic, or full scan.
        var provider = new DefaultSystemFontProvider();
        var family = "KernSmithZzzGenuinelyNotInstalled_" + Guid.NewGuid().ToString("N")[..8];
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        provider.FontDirectoriesOverride = new List<string> { _tempDir }; // has Roboto, but won't filename-match this family
        provider._cachedFonts = new List<SystemFontInfo>();

        // Act
        var act = () => provider.LoadFont(family);

        // Assert
        var result = Should.NotThrow(act);
        result.ShouldBeNull();
        provider._resolvedFontCache.ContainsKey(family).ShouldBeFalse();
    }
}
