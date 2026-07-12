using KernSmith.Font;
using KernSmith.Font.Models;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Covers the Phase 1 lazy per-family resolved-font cache on
/// <see cref="DefaultSystemFontProvider"/>: cache hits, eviction of stale
/// entries, and the standalone <c>IsCacheEntryValid</c> validator.
/// </summary>
public class DefaultSystemFontProviderCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureFontPath;

    public DefaultSystemFontProviderCacheTests()
    {
        _tempDir = Path.Join(Path.GetTempPath(), "kernsmith-cache-tests-" + Guid.NewGuid().ToString("N")[..8]);
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
    public void LoadFont_ValidCacheEntry_ReturnsFromCacheWithoutScanning()
    {
        // Arrange — pre-populate the cache; do NOT rely on GetInstalledFonts() ever running.
        var provider = new DefaultSystemFontProvider();
        provider._resolvedFontCache["Roboto"] = new SystemFontInfo
        {
            FamilyName = "Roboto",
            StyleName = "Regular",
            FilePath = _fixtureFontPath,
            FaceIndex = 0
        };

        // Act
        var result = provider.LoadFont("Roboto");

        // Assert
        result.ShouldNotBeNull();
        result!.Data.ShouldBe(File.ReadAllBytes(_fixtureFontPath));
        result.FaceIndex.ShouldBe(0);

        // The full-scan cache (_cachedFonts) must remain untouched — proving the
        // cache hit short-circuited before GetInstalledFonts() ever ran.
        provider._cachedFonts.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_StaleCacheEntry_FileDeleted_EvictsAndFallsThrough()
    {
        // Arrange
        var provider = new DefaultSystemFontProvider();
        var family = "KernSmithZzzCacheTestFont_" + Guid.NewGuid().ToString("N")[..8];
        provider._resolvedFontCache[family] = new SystemFontInfo
        {
            FamilyName = family,
            StyleName = "Regular",
            FilePath = _fixtureFontPath,
            FaceIndex = 0
        };

        File.Delete(_fixtureFontPath);

        // Act — falls through to registry/full scan, which won't find this made-up family.
        var result = provider.LoadFont(family);

        // Assert
        result.ShouldBeNull();
        provider._resolvedFontCache.ContainsKey(family).ShouldBeFalse();
    }

    [Fact]
    public void LoadFont_StaleCacheEntry_FamilyMismatch_EvictsAndFallsThrough()
    {
        // Arrange — cache key doesn't match the file's actual embedded family ("Roboto").
        var provider = new DefaultSystemFontProvider();
        var family = "KernSmithZzzCacheTestFont2_" + Guid.NewGuid().ToString("N")[..8];
        provider._resolvedFontCache[family] = new SystemFontInfo
        {
            FamilyName = family,
            StyleName = "Regular",
            FilePath = _fixtureFontPath,
            FaceIndex = 0
        };

        // Act — falls through to registry/full scan, which won't find this made-up family either.
        var result = provider.LoadFont(family);

        // Assert
        result.ShouldBeNull();
        provider._resolvedFontCache.ContainsKey(family).ShouldBeFalse();
    }

    [Fact]
    public void LoadFont_FullScanFallback_PopulatesResolvedFontCache()
    {
        // Arrange — seed _cachedFonts directly (bypassing the real ScanSystemFonts()/registry
        // scan) so LoadFont's full-scan fallback branch resolves deterministically, then
        // verify its write-back into _resolvedFontCache actually ran.
        var provider = new DefaultSystemFontProvider();
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

        // Assert
        result.ShouldNotBeNull();
        provider._resolvedFontCache.ShouldContainKey("Roboto");
        provider._resolvedFontCache["Roboto"].FilePath.ShouldBe(_fixtureFontPath);
        provider._resolvedFontCache["Roboto"].FaceIndex.ShouldBe(0);
    }

    [Fact]
    public void LoadFont_SecondCallForSameFamily_IsPureCacheHit()
    {
        // Arrange
        var provider = new DefaultSystemFontProvider();
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

        var first = provider.LoadFont("Roboto");
        first.ShouldNotBeNull();

        // Null out _cachedFonts so a repeat full scan is impossible: if the second
        // LoadFont call fell through to GetInstalledFonts() again, it would hit the
        // real (unseeded) ScanSystemFonts() path and find nothing.
        provider._cachedFonts = null;

        // Act
        var second = provider.LoadFont("Roboto");

        // Assert — only reachable via _resolvedFontCache, since _cachedFonts is null.
        second.ShouldNotBeNull();
        second!.Data.ShouldBe(first!.Data);
        second.FaceIndex.ShouldBe(first.FaceIndex);
        provider._cachedFonts.ShouldBeNull();
    }

    [Fact]
    public void LoadFont_StyledRequest_DoesNotPopulateResolvedFontCache()
    {
        // Arrange — two styles so a style-specific request actually resolves
        // (rather than short-circuiting via the "style not found" null return),
        // proving the "only cache when styleName is null" scoping holds even
        // on a successful styled resolution.
        var provider = new DefaultSystemFontProvider();
        provider._cachedFonts = new List<SystemFontInfo>
        {
            new SystemFontInfo
            {
                FamilyName = "Roboto",
                StyleName = "Regular",
                FilePath = _fixtureFontPath,
                FaceIndex = 0
            },
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

        // Assert
        result.ShouldNotBeNull();
        provider._resolvedFontCache.ContainsKey("Roboto").ShouldBeFalse();
    }

    [Fact]
    public void IsCacheEntryValid_ValidFileAndMatchingFamily_ReturnsTrue()
    {
        var entry = new SystemFontInfo
        {
            FamilyName = "Roboto",
            StyleName = "Regular",
            FilePath = _fixtureFontPath,
            FaceIndex = 0
        };

        DefaultSystemFontProvider.IsCacheEntryValid(entry, "Roboto").ShouldBeTrue();
    }

    [Fact]
    public void IsCacheEntryValid_MissingFile_ReturnsFalse()
    {
        var entry = new SystemFontInfo
        {
            FamilyName = "Roboto",
            StyleName = "Regular",
            FilePath = Path.Join(_tempDir, "does-not-exist.ttf"),
            FaceIndex = 0
        };

        DefaultSystemFontProvider.IsCacheEntryValid(entry, "Roboto").ShouldBeFalse();
    }

    [Fact]
    public void IsCacheEntryValid_FamilyMismatch_ReturnsFalse()
    {
        var entry = new SystemFontInfo
        {
            FamilyName = "Roboto",
            StyleName = "Regular",
            FilePath = _fixtureFontPath,
            FaceIndex = 0
        };

        DefaultSystemFontProvider.IsCacheEntryValid(entry, "SomeOtherFamily").ShouldBeFalse();
    }

    [Fact]
    public void IsCacheEntryValid_CorruptFontBytes_ReturnsFalseWithoutThrowing()
    {
        var corruptPath = Path.Join(_tempDir, "corrupt.ttf");
        File.WriteAllBytes(corruptPath, new byte[] { 1, 2, 3, 4, 5 });

        var entry = new SystemFontInfo
        {
            FamilyName = "Roboto",
            StyleName = "Regular",
            FilePath = corruptPath,
            FaceIndex = 0
        };

        var act = () => DefaultSystemFontProvider.IsCacheEntryValid(entry, "Roboto");

        Should.NotThrow(act).ShouldBeFalse();
    }
}
