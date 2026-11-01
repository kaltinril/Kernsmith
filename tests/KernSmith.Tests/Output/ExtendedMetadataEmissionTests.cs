using KernSmith.Output;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// The <c>kernsmith</c> extended metadata (text line, XML element, binary block 6) is written
/// only when it carries a field beyond the generator version (REF-05), so a default font is a
/// plain BMFont file.
/// </summary>
public sealed class ExtendedMetadataEmissionTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontResult Generate(Action<FontGeneratorOptions>? configure = null)
    {
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("AB"),
            Rasterizer = new StbTrueTypeRasterizer(),
        };
        configure?.Invoke(options);
        return BmFont.Generate(Font(), options);
    }

    [Fact]
    public void DefaultFont_HasNoExtendedMetadataInAnyFormat()
    {
        var result = Generate();

        result.Model.Extended.ShouldBeNull();
        result.FntText.ShouldNotContain("kernsmith", Case.Sensitive);
        result.FntXml.ShouldNotContain("kernsmith", Case.Sensitive);
        HasBlock(result.FntBinary, 6).ShouldBeFalse();
    }

    [Fact]
    public void FontWithAnExtendedField_KeepsItInEveryFormat()
    {
        var result = Generate(o => o.FallbackCharacter = '?');

        result.Model.Extended.ShouldNotBeNull();
        result.FntText.ShouldContain("kernsmith version=", Case.Sensitive);
        result.FntXml.ShouldContain("<kernsmith", Case.Sensitive);
        HasBlock(result.FntBinary, 6).ShouldBeTrue();
        BmFontReader.ReadText(result.FntText).Extended!.FallbackCharacter.ShouldBe('?');
    }

    /// <summary>Walks a binary .fnt's blocks (type byte, int32 size) looking for one type.</summary>
    internal static bool HasBlock(byte[] fnt, int type)
    {
        var i = 4;
        while (i + 5 <= fnt.Length)
        {
            if (fnt[i] == type)
                return true;
            i += 5 + BitConverter.ToInt32(fnt, i + 1);
        }

        return false;
    }
}
