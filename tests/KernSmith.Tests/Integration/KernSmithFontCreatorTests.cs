using KernSmith.Font;
using KernSmith.Gum;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Tests for the KernSmithFontCreator.RegisterFont file-path overload.
/// </summary>
public sealed class KernSmithFontCreatorTests : IDisposable
{
    private const string TestFontPath = "Fixtures/Roboto-Regular.ttf";

    public void Dispose() => KernSmithFontCreator.ClearRegisteredFonts();

    [Fact]
    public void RegisterFont_FilePath_NullFamilyName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            KernSmithFontCreator.RegisterFont(null!, "some/path.ttf"));
    }

    [Fact]
    public void RegisterFont_FilePath_NullPath_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            KernSmithFontCreator.RegisterFont("Test", (string)null!));
    }

    [Fact]
    public void RegisterFont_FilePath_LoadsAndRegistersFont()
    {
        // Arrange & Act — register via file path
        KernSmithFontCreator.RegisterFont("RobotoFile", TestFontPath);

        // Assert — generate from the registered font to prove it loaded correctly
        var result = BmFont.GenerateFromSystem("RobotoFile", new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Model.Info.Face.ShouldBe("Roboto");
    }

    [Fact]
    public void RegisterFont_FilePath_WithStyle_RegistersStyledVariant()
    {
        // Arrange & Act
        KernSmithFontCreator.RegisterFont("RobotoStyled", TestFontPath, style: "Bold");

        // Assert — request bold, which should resolve the registered "Bold" variant
        var result = BmFont.GenerateFromSystem("RobotoStyled", new FontGeneratorOptions
        {
            Size = 24,
            Bold = true,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RegisterFont_FilePath_NonExistentFile_Throws()
    {
        Should.Throw<Exception>(() =>
            KernSmithFontCreator.RegisterFont("Missing", "nonexistent/font.ttf"));
    }
}
