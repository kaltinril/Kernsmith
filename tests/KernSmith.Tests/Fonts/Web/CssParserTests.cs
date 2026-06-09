using KernSmith.Fonts.Web.Internal;
using Shouldly;

namespace KernSmith.Tests.Fonts.Web;

public class CssParserTests
{
    // Representative Bunny Fonts / Google Fonts CSS: one @font-face per subset, each
    // preceded by a /* subset */ comment, with both woff2 and woff in the src list.
    private const string MultiSubsetCss = """
        /* cyrillic */
        @font-face {
          font-family: 'Roboto';
          font-style: normal;
          font-weight: 400;
          src: url(https://fonts.bunny.net/roboto/files/roboto-cyrillic-400.woff2) format('woff2'),
               url(https://fonts.bunny.net/roboto/files/roboto-cyrillic-400.woff) format('woff');
          unicode-range: U+0301, U+0400-045F;
        }
        /* latin */
        @font-face {
          font-family: 'Roboto';
          font-style: normal;
          font-weight: 400;
          src: url(https://fonts.bunny.net/roboto/files/roboto-latin-400.woff2) format('woff2'),
               url(https://fonts.bunny.net/roboto/files/roboto-latin-400.woff) format('woff');
          unicode-range: U+0000-00FF;
        }
        """;

    [Fact]
    public void ExtractWoffUrl_ReturnsWoffForRequestedSubset()
    {
        var url = CssParser.ExtractWoffUrl(MultiSubsetCss, "latin");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-latin-400.woff");
    }

    [Fact]
    public void ExtractWoffUrl_SubsetMatchIsCaseInsensitive()
    {
        var url = CssParser.ExtractWoffUrl(MultiSubsetCss, "LATIN");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-latin-400.woff");
    }

    [Fact]
    public void ExtractWoffUrl_DifferentSubset_ReturnsThatSubset()
    {
        var url = CssParser.ExtractWoffUrl(MultiSubsetCss, "cyrillic");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-cyrillic-400.woff");
    }

    [Fact]
    public void ExtractWoffUrl_NeverReturnsWoff2()
    {
        var url = CssParser.ExtractWoffUrl(MultiSubsetCss, "latin");

        url.ShouldNotBeNull();
        url.ShouldNotContain(".woff2");
        url.ShouldEndWith(".woff");
    }

    [Fact]
    public void ExtractWoffUrl_OnlyWoff2Available_ReturnsNull()
    {
        const string woff2Only = """
            /* latin */
            @font-face {
              font-family: 'Roboto';
              src: url(https://fonts.bunny.net/roboto/files/roboto-latin-400.woff2) format('woff2');
            }
            """;

        var url = CssParser.ExtractWoffUrl(woff2Only, "latin");

        url.ShouldBeNull();
    }

    [Fact]
    public void ExtractWoffUrl_UnlabelledSubset_FallsBackToFirstWoff()
    {
        // No /* subset */ comments — should fall back to the first .woff found.
        const string unlabelled = """
            @font-face {
              font-family: 'Roboto';
              src: url(https://fonts.bunny.net/roboto/files/roboto-400.woff2) format('woff2'),
                   url(https://fonts.bunny.net/roboto/files/roboto-400.woff) format('woff');
            }
            """;

        var url = CssParser.ExtractWoffUrl(unlabelled, "latin");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-400.woff");
    }

    [Fact]
    public void ExtractWoffUrl_RequestedSubsetMissing_FallsBackToFirstWoff()
    {
        var url = CssParser.ExtractWoffUrl(MultiSubsetCss, "greek");

        // "greek" is not in the CSS, so the first available .woff (cyrillic) is returned.
        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-cyrillic-400.woff");
    }
}
