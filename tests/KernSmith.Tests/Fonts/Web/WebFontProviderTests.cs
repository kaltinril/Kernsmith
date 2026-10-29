using KernSmith.Fonts.Web;
using Shouldly;

namespace KernSmith.Tests.Fonts.Web;

public class WebFontProviderTests
{
    [Fact]
    public void BunnyFonts_HasExpectedEndpoint()
    {
        WebFontProvider.BunnyFonts.CssBaseUrl.ShouldBe("https://fonts.bunny.net/css");
    }

    [Fact]
    public void GoogleFonts_HasExpectedEndpoint()
    {
        WebFontProvider.GoogleFonts.CssBaseUrl.ShouldBe("https://fonts.googleapis.com/css");
    }

    [Fact]
    public void Custom_UsesProvidedBaseUrl()
    {
        var provider = WebFontProvider.Custom("https://my.cdn/css");

        provider.CssBaseUrl.ShouldBe("https://my.cdn/css");
    }

    [Fact]
    public void Custom_EmptyUrl_Throws()
    {
        Should.Throw<ArgumentException>(() => WebFontProvider.Custom(""));
    }

    [Fact]
    public void BuildCssUrl_NormalWeight_EncodesFamilyAndWeight()
    {
        var url = WebFontProvider.BunnyFonts.BuildCssUrl("Open Sans", 700, italic: false);

        url.ShouldBe("https://fonts.bunny.net/css?family=Open+Sans:700");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(700)]
    public void BuildCssUrl_Italic_RequestsTheItalicOfThatWeight(int weight)
    {
        // "Roboto:ital,700" is read by both CDNs as two variants (italic 400 + normal 700), so
        // an italic 700 request came back italic 400. The v1 "{weight}italic" form names one variant.
        var url = WebFontProvider.BunnyFonts.BuildCssUrl("Roboto", weight, italic: true);

        url.ShouldBe($"https://fonts.bunny.net/css?family=Roboto:{weight}italic");
    }

    [Fact]
    public void BuildCssUrl_ItalicOnGoogleFonts_RequestsTheItalicOfThatWeight()
    {
        var url = WebFontProvider.GoogleFonts.BuildCssUrl("Open Sans", 700, italic: true);

        url.ShouldBe("https://fonts.googleapis.com/css?family=Open+Sans:700italic");
    }
}
