using Shouldly;

namespace KernSmith.Tests;

public class InputValidationTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ---------------------------------------------------------------
    // B1.1 — Null input guards
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_NullFontData_ThrowsArgumentNullException()
    {
        // Act
        byte[] nullData = null!;
        var act = () => BmFont.Generate(nullData, new FontGeneratorOptions { Size = 32 });

        // Assert
        var ex = Should.Throw<ArgumentNullException>(act);
        ex.ParamName.ShouldBe("fontData");
    }

    [Fact]
    public void Load_NullPath_ThrowsArgumentNullException()
    {
        // Act
        var act = () => BmFont.Load(null!);

        // Assert
        var ex = Should.Throw<ArgumentNullException>(act);
        ex.ParamName.ShouldBe("fntPath");
    }

    [Fact]
    public void FromChars_NullString_ThrowsArgumentNullException()
    {
        // Act
        var act = () => CharacterSet.FromChars((string)null!);

        // Assert
        var ex = Should.Throw<ArgumentNullException>(act);
        ex.ParamName.ShouldBe("characters");
    }

    [Fact]
    public void FromChars_NullCodepoints_ThrowsArgumentNullException()
    {
        // Act
        var act = () => CharacterSet.FromChars((IEnumerable<int>)null!);

        // Assert
        var ex = Should.Throw<ArgumentNullException>(act);
        ex.ParamName.ShouldBe("codepoints");
    }

    // ---------------------------------------------------------------
    // B1.2 — Empty / garbage font data
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_EmptyFontData_ThrowsFontParsingException()
    {
        // Act
        var act = () => BmFont.Generate(Array.Empty<byte>(), 32);

        // Assert
        Should.Throw<FontParsingException>(act);
    }

    [Fact]
    public void Generate_GarbageFontData_ThrowsFontParsingException()
    {
        // Arrange
        var random = new Random(42);
        var garbage = new byte[256];
        random.NextBytes(garbage);

        // Act
        var act = () => BmFont.Generate(garbage, 32);

        // Assert
        Should.Throw<FontParsingException>(act);
    }

    // ---------------------------------------------------------------
    // B1.3 — Builder without font
    // ---------------------------------------------------------------

    [Fact]
    public void Builder_WithoutFont_ThrowsInvalidOperationException()
    {
        // Act
        var act = () => BmFont.Builder().Build();

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("No font specified");
    }

    // ---------------------------------------------------------------
    // B1.4 — Guard condition tests
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_SdfWithSuperSampling_ThrowsInvalidOperationException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Sdf = true,
            SuperSampleLevel = 2
        });

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("SDF");
        ex.Message.ShouldContain("super sampling");
    }

    [Fact]
    public void Generate_ChannelPackingWithColorFont_ThrowsInvalidOperationException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            ChannelPacking = true,
            ColorFont = true
        });

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Channel packing");
        ex.Message.ShouldContain("color");
    }

    // ---------------------------------------------------------------
    // A9 — Size validation
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_SizeZero_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions { Size = 0 });

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Generate_SizeNegative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions { Size = -1 });

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    // ---------------------------------------------------------------
    // A10 — MaxTextureWidth / MaxTextureHeight validation
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_MaxTextureWidthZero_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            MaxTextureWidth = 0
        });

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Generate_MaxTextureHeightNegative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            MaxTextureHeight = -5
        });

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }
}
