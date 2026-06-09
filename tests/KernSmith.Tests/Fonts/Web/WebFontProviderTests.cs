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

    [Fact]
    public void BuildCssUrl_Italic_IncludesItalSpec()
    {
        var url = WebFontProvider.BunnyFonts.BuildCssUrl("Roboto", 400, italic: true);

        url.ShouldBe("https://fonts.bunny.net/css?family=Roboto:ital,400");
    }
}
