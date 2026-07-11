using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

public class DefaultSystemFontProviderScanTests : IDisposable
{
    private readonly string _tempDir;

    public DefaultSystemFontProviderScanTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kernsmith-sysfont-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ScanFontDirectories_FindsFontInProvidedDirectory()
    {
        // Arrange
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");
        var destPath = Path.Combine(_tempDir, "Roboto-Regular.ttf");
        File.Copy(sourcePath, destPath);

        // Act
        var results = DefaultSystemFontProvider.ScanFontDirectories(new[] { _tempDir });

        // Assert
        results.Count.ShouldBe(1);
        results[0].FamilyName.ShouldBe("Roboto");
        results[0].StyleName.ShouldBe("Regular");
        results[0].FaceIndex.ShouldBe(0);
        results[0].FilePath.ShouldBe(destPath);
    }

    [Fact]
    public void ScanFontDirectories_EmptyOrMissingDirectory_ReturnsEmptyList()
    {
        // Arrange — one dir with only a non-font file, one dir that doesn't exist at all.
        var nonFontPath = Path.Combine(_tempDir, "notes.txt");
        File.WriteAllText(nonFontPath, "not a font");
        var missingDir = Path.Combine(_tempDir, "does-not-exist");

        // Act
        var results = DefaultSystemFontProvider.ScanFontDirectories(new[] { _tempDir, missingDir });

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public void ScanFontDirectories_CorruptFontFile_SkippedWithoutThrowing()
    {
        // Arrange
        var corruptPath = Path.Combine(_tempDir, "corrupt.ttf");
        File.WriteAllBytes(corruptPath, new byte[] { 1, 2, 3, 4, 5 });

        // Act
        var act = () => DefaultSystemFontProvider.ScanFontDirectories(new[] { _tempDir });

        // Assert
        var results = Should.NotThrow(act);
        results.ShouldBeEmpty();
    }
}
