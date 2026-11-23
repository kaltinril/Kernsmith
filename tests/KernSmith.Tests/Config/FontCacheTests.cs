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

    /// <summary>A real file, and the same path spelled another way.</summary>
    private static (string Dir, string File, string Respelled) TempFontWithDotSegments(byte[] bytes)
    {
        var dir = Directory.CreateTempSubdirectory("ks-font-cache").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        var file = Path.Combine(dir, "font.bin");
        File.WriteAllBytes(file, bytes);
        return (dir, file, Path.Combine(dir, "sub", "..", "font.bin"));
    }

    [Fact]
    public void LoadFile_IsFoundUnderTheSpellingItWasLoadedWith()
    {
        var bytes = new byte[] { 9, 8, 7 };
        var (dir, _, respelled) = TempFontWithDotSegments(bytes);
        try
        {
            var cache = new FontCache();
            cache.LoadFile(respelled);

            cache.Contains(respelled).ShouldBeTrue();
            cache.Get(respelled).ShouldBe(bytes);
            cache.TryGet(respelled, out var found).ShouldBeTrue();
            found.ShouldBe(bytes);
            cache.Count.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFile_IsFoundWithForwardSlashes()
    {
        var bytes = new byte[] { 9, 8, 7 };
        var (dir, file, _) = TempFontWithDotSegments(bytes);
        try
        {
            var slashes = file.Replace(Path.DirectorySeparatorChar, '/');
            var cache = new FontCache();
            cache.LoadFile(slashes);

            cache.Get(slashes).ShouldBe(bytes);
            cache.Get(file).ShouldBe(bytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFile_IsFoundByRelativePath()
    {
        var bytes = new byte[] { 9, 8, 7 };
        var name = $"ks-font-{Guid.NewGuid():N}.bin";
        var file = Path.Combine(Directory.GetCurrentDirectory(), name);
        File.WriteAllBytes(file, bytes);
        try
        {
            var cache = new FontCache();
            cache.LoadFile(name);

            cache.Get(name).ShouldBe(bytes);
            cache.Get(file).ShouldBe(bytes);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadFile_RespelledLookupIsStillCaseInsensitive()
    {
        var bytes = new byte[] { 9, 8, 7 };
        var (dir, _, respelled) = TempFontWithDotSegments(bytes);
        try
        {
            var cache = new FontCache();
            cache.LoadFile(respelled);

            cache.Get(respelled.ToUpperInvariant()).ShouldBe(bytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\0name")]
    public void Lookup_KeyThatCannotBeAPath_IsSimplyNotCached(string key)
    {
        var cache = new FontCache();

        cache.Contains(key).ShouldBeFalse();
        cache.TryGet(key, out _).ShouldBeFalse();
        Should.Throw<KeyNotFoundException>(() => cache.Get(key));
    }

    [Fact]
    public void GenerateBatch_FontPathNotInCanonicalForm_Generates()
    {
        var font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));
        var (dir, file, respelled) = TempFontWithDotSegments(font);
        try
        {
            var options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.FromChars("AB") };
            var result = BmFont.GenerateBatch(
            [
                new BatchJob { FontPath = respelled, Options = options },
                new BatchJob { FontPath = file.Replace(Path.DirectorySeparatorChar, '/'), Options = options },
            ]);

            result.Results.Select(r => r.Error?.Message).ShouldBe([null, null]);
            result.Succeeded.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateBatch_SystemFontJob_IsNotServedAFileOfTheSameRelativeName()
    {
        var family = $"KsFamily{Guid.NewGuid():N}";
        var fileInCwd = Path.Combine(Directory.GetCurrentDirectory(), family);
        File.WriteAllBytes(fileInCwd, [1, 2, 3]);
        try
        {
            var font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));
            var cache = new FontCache();
            cache.LoadFile(family);
            cache.Add(family, font);

            var result = BmFont.GenerateBatch(
                [new BatchJob
                {
                    SystemFont = family,
                    Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.FromChars("AB") },
                }],
                new BatchOptions { FontCache = cache });

            result.Results[0].Error.ShouldBeNull();
            result.Succeeded.ShouldBe(1);
        }
        finally
        {
            File.Delete(fileInCwd);
        }
    }
}
