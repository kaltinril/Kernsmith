using KernSmith.Output;
using KernSmith.Output.Model;
using FluentAssertions;

namespace KernSmith.Tests.Output;

public sealed class KernSmithReaderTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontResult GenerateTestFont()
    {
        var fontData = LoadTestFont();
        return BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Kerning = true
        });
    }

    // ------------------------------------------------------------------
    // Round-trip tests
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_TextFormat()
    {
        // Arrange
        var original = GenerateTestFont();
        var text = original.ToString();

        // Act
        var parsed = BmFontReader.ReadText(text);

        // Assert
        parsed.Info.Face.Should().Be(original.Model.Info.Face);
        parsed.Info.Size.Should().Be(original.Model.Info.Size);
        parsed.Characters.Count.Should().Be(original.Model.Characters.Count);
        parsed.KerningPairs.Count.Should().Be(original.Model.KerningPairs.Count);
        parsed.Common.LineHeight.Should().Be(original.Model.Common.LineHeight);
        parsed.Pages.Count.Should().Be(original.Model.Pages.Count);
    }

    [Fact]
    public void RoundTrip_XmlFormat()
    {
        // Arrange
        var original = GenerateTestFont();
        var xml = original.ToXml();

        // Act
        var parsed = BmFontReader.ReadXml(xml);

        // Assert
        parsed.Info.Face.Should().Be(original.Model.Info.Face);
        parsed.Info.Size.Should().Be(original.Model.Info.Size);
        parsed.Characters.Count.Should().Be(original.Model.Characters.Count);
        parsed.KerningPairs.Count.Should().Be(original.Model.KerningPairs.Count);
        parsed.Common.LineHeight.Should().Be(original.Model.Common.LineHeight);
        parsed.Pages.Count.Should().Be(original.Model.Pages.Count);
    }

    [Fact]
    public void RoundTrip_BinaryFormat()
    {
        // Arrange
        var original = GenerateTestFont();
        var binary = original.ToBinary();

        // Act
        var parsed = BmFontReader.ReadBinary(binary);

        // Assert
        parsed.Info.Face.Should().Be(original.Model.Info.Face);
        parsed.Info.Size.Should().Be(original.Model.Info.Size);
        parsed.Characters.Count.Should().Be(original.Model.Characters.Count);
        parsed.KerningPairs.Count.Should().Be(original.Model.KerningPairs.Count);
        parsed.Common.LineHeight.Should().Be(original.Model.Common.LineHeight);
        parsed.Pages.Count.Should().Be(original.Model.Pages.Count);
    }

    // ------------------------------------------------------------------
    // Auto-detection tests
    // ------------------------------------------------------------------

    [Fact]
    public void Read_AutoDetects_TextFormat()
    {
        // Arrange
        var original = GenerateTestFont();
        var text = original.ToString();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        // Act
        var parsed = BmFontReader.Read(bytes);

        // Assert
        parsed.Info.Face.Should().Be("Roboto");
    }

    [Fact]
    public void Read_AutoDetects_XmlFormat()
    {
        // Arrange
        var original = GenerateTestFont();
        var xml = original.ToXml();
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);

        // Act
        var parsed = BmFontReader.Read(bytes);

        // Assert
        parsed.Info.Face.Should().Be("Roboto");
        parsed.Characters.Count.Should().Be(original.Model.Characters.Count);
    }

    [Fact]
    public void Read_AutoDetects_BinaryFormat()
    {
        // Arrange
        var original = GenerateTestFont();
        var binary = original.ToBinary();

        // Act
        var parsed = BmFontReader.Read(binary);

        // Assert
        parsed.Info.Face.Should().Be("Roboto");
    }

    // ------------------------------------------------------------------
    // LoadModel convenience methods
    // ------------------------------------------------------------------

    [Fact]
    public void LoadModel_FromString_Works()
    {
        // Arrange
        var original = GenerateTestFont();
        var text = original.ToString();

        // Act
        var model = BmFont.LoadModel(text);

        // Assert
        model.Info.Face.Should().Be("Roboto");
        model.Characters.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LoadModel_FromBytes_Works()
    {
        // Arrange
        var original = GenerateTestFont();
        var binary = original.ToBinary();

        // Act
        var model = BmFont.LoadModel(binary);

        // Assert
        model.Info.Face.Should().Be("Roboto");
        model.Characters.Count.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Round-trip field fidelity tests
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_TextFormat_PreservesCharacterDetails()
    {
        // Arrange
        var original = GenerateTestFont();
        var text = original.ToString();

        // Act
        var parsed = BmFontReader.ReadText(text);

        // Assert -- verify individual character entries match
        var originalChars = original.Model.Characters.OrderBy(c => c.Id).ToList();
        var parsedChars = parsed.Characters.OrderBy(c => c.Id).ToList();

        for (int i = 0; i < originalChars.Count; i++)
        {
            parsedChars[i].Id.Should().Be(originalChars[i].Id);
            parsedChars[i].X.Should().Be(originalChars[i].X);
            parsedChars[i].Y.Should().Be(originalChars[i].Y);
            parsedChars[i].Width.Should().Be(originalChars[i].Width);
            parsedChars[i].Height.Should().Be(originalChars[i].Height);
            parsedChars[i].XOffset.Should().Be(originalChars[i].XOffset);
            parsedChars[i].YOffset.Should().Be(originalChars[i].YOffset);
            parsedChars[i].XAdvance.Should().Be(originalChars[i].XAdvance);
            parsedChars[i].Page.Should().Be(originalChars[i].Page);
            parsedChars[i].Channel.Should().Be(originalChars[i].Channel);
        }
    }

    [Fact]
    public void RoundTrip_BinaryFormat_PreservesKerningDetails()
    {
        // Arrange
        var original = GenerateTestFont();
        var binary = original.ToBinary();

        // Act
        var parsed = BmFontReader.ReadBinary(binary);

        // Assert
        var originalKerns = original.Model.KerningPairs.OrderBy(k => k.First).ThenBy(k => k.Second).ToList();
        var parsedKerns = parsed.KerningPairs.OrderBy(k => k.First).ThenBy(k => k.Second).ToList();

        parsedKerns.Count.Should().Be(originalKerns.Count);
        for (int i = 0; i < originalKerns.Count; i++)
        {
            parsedKerns[i].First.Should().Be(originalKerns[i].First);
            parsedKerns[i].Second.Should().Be(originalKerns[i].Second);
            parsedKerns[i].Amount.Should().Be(originalKerns[i].Amount);
        }
    }

    [Fact]
    public void RoundTrip_XmlFormat_PreservesCommonBlock()
    {
        // Arrange
        var original = GenerateTestFont();
        var xml = original.ToXml();

        // Act
        var parsed = BmFontReader.ReadXml(xml);

        // Assert
        parsed.Common.Base.Should().Be(original.Model.Common.Base);
        parsed.Common.ScaleW.Should().Be(original.Model.Common.ScaleW);
        parsed.Common.ScaleH.Should().Be(original.Model.Common.ScaleH);
        parsed.Common.Packed.Should().Be(original.Model.Common.Packed);
    }

    // ------------------------------------------------------------------
    // Error handling tests
    // ------------------------------------------------------------------

    [Fact]
    public void ReadText_MissingInfoLine_ThrowsFormatException()
    {
        // Arrange
        var badText = "common lineHeight=38 base=30 scaleW=256 scaleH=256 pages=1 packed=0";

        // Act
        var act = () => BmFontReader.ReadText(badText);

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*missing*info*");
    }

    [Fact]
    public void ReadText_MissingCommonLine_ThrowsFormatException()
    {
        // Arrange
        var badText = "info face=\"Test\" size=32 bold=0 italic=0 charset=\"\" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=0,0";

        // Act
        var act = () => BmFontReader.ReadText(badText);

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*missing*common*");
    }

    [Fact]
    public void ReadBinary_TooShort_ThrowsFormatException()
    {
        // Arrange
        var tooShort = new byte[] { 66, 77 };

        // Act
        var act = () => BmFontReader.ReadBinary(tooShort);

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*too short*");
    }

    [Fact]
    public void ReadBinary_InvalidHeader_ThrowsFormatException()
    {
        // Arrange
        var badHeader = new byte[] { 0, 0, 0, 3 };

        // Act
        var act = () => BmFontReader.ReadBinary(badHeader);

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*invalid header*");
    }

    [Fact]
    public void ReadBinary_UnsupportedVersion_ThrowsFormatException()
    {
        // Arrange
        var badVersion = new byte[] { 66, 77, 70, 99 };

        // Act
        var act = () => BmFontReader.ReadBinary(badVersion);

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*unsupported version*");
    }

    [Fact]
    public void ReadXml_MissingRootElement_ThrowsFormatException()
    {
        // Arrange
        var badXml = "<?xml version=\"1.0\"?><notfont></notfont>";

        // Act
        var act = () => BmFontReader.ReadXml(badXml);

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*root element*");
    }
}
