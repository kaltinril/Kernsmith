using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class FontCacheTests
{
    [Fact]
    public void NewCache_IsEmpty()
    {
        // Act & Assert
        new FontCache().Count.ShouldBe(0);
    }

    [Fact]
    public void Add_ThenGet_ReturnsSameData()
    {
        // Arrange
        var cache = new FontCache();
        var data = new byte[] { 10, 20, 30 };

        // Act
        cache.Add("key", data);

        // Assert
        cache.Get("key").ShouldBeSameAs(data);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void Contains_ReflectsPresence()
    {
        // Arrange
        var cache = new FontCache();
        cache.Add("key", new byte[] { 1 });

        // Act & Assert
        cache.Contains("key").ShouldBeTrue();
        cache.Contains("missing").ShouldBeFalse();
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        // Arrange
        var cache = new FontCache();
        cache.Add("Arial", new byte[] { 1 });

        // Act & Assert
        cache.Contains("arial").ShouldBeTrue();
    }

    [Fact]
    public void Get_MissingKey_Throws()
    {
        // Act & Assert
        Should.Throw<KeyNotFoundException>(() => new FontCache().Get("nope"));
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        // Act
        var found = new FontCache().TryGet("nope", out var data);

        // Assert
        found.ShouldBeFalse();
        data.ShouldBeNull();
    }

    [Fact]
    public void TryGet_Present_ReturnsTrueAndData()
    {
        // Arrange
        var cache = new FontCache();
        var bytes = new byte[] { 5 };
        cache.Add("k", bytes);

        // Act
        var found = cache.TryGet("k", out var data);

        // Assert
        found.ShouldBeTrue();
        data.ShouldBeSameAs(bytes);
    }

    [Fact]
    public void Add_SameKeyTwice_Overwrites()
    {
        // Arrange
        var cache = new FontCache();
        cache.Add("k", new byte[] { 1 });

        // Act
        var replacement = new byte[] { 2 };
        cache.Add("k", replacement);

        // Assert
        cache.Count.ShouldBe(1);
        cache.Get("k").ShouldBeSameAs(replacement);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        // Arrange
        var cache = new FontCache();
        cache.Add("a", new byte[] { 1 });
        cache.Add("b", new byte[] { 2 });

        // Act
        cache.Clear();

        // Assert
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void LoadFile_CachesByFullPath()
    {
        // Arrange
        var cache = new FontCache();
        var temp = Path.Combine(Path.GetTempPath(), $"ks-font-{Guid.NewGuid():N}.bin");
        var bytes = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(temp, bytes);

        try
        {
            // Act
            cache.LoadFile(temp);

            // Assert
            cache.Contains(Path.GetFullPath(temp)).ShouldBeTrue();
            cache.Get(Path.GetFullPath(temp)).ShouldBe(bytes);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
