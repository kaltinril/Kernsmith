using KernSmith.Fonts.Web.Internal;
using Shouldly;

namespace KernSmith.Tests.Fonts.Web;

public class CssParserTests
{
    // Representative Bunny Fonts CSS: one @font-face per subset, each preceded by a
    // /* subset */ comment, with both woff2 and woff in the src list.
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
    public void ExtractFontUrl_PrefersWoff2ForRequestedSubset()
    {
        var url = CssParser.ExtractFontUrl(MultiSubsetCss, "latin");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-latin-400.woff2");
    }

    [Fact]
    public void ExtractFontUrl_SubsetMatchIsCaseInsensitive()
    {
        var url = CssParser.ExtractFontUrl(MultiSubsetCss, "LATIN");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-latin-400.woff2");
    }

    [Fact]
    public void ExtractFontUrl_DifferentSubset_ReturnsThatSubset()
    {
        var url = CssParser.ExtractFontUrl(MultiSubsetCss, "cyrillic");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-cyrillic-400.woff2");
    }

    [Fact]
    public void ExtractFontUrl_Woff1ListedFirst_StillPrefersWoff2()
    {
        const string woffFirst = """
            /* latin */
            @font-face {
              src: url(https://cdn.example/roboto-latin.woff) format('woff'),
                   url(https://cdn.example/roboto-latin.woff2) format('woff2');
            }
            """;

        var url = CssParser.ExtractFontUrl(woffFirst, "latin");

        url.ShouldBe("https://cdn.example/roboto-latin.woff2");
    }

    [Fact]
    public void ExtractFontUrl_OnlyWoff2Available_ReturnsWoff2()
    {
        // Google Fonts serves a single woff2 source to modern browser User-Agents.
        const string woff2Only = """
            /* latin */
            @font-face {
              font-family: 'Roboto';
              src: url(https://fonts.gstatic.com/s/roboto/v51/abc.woff2) format('woff2');
            }
            """;

        var url = CssParser.ExtractFontUrl(woff2Only, "latin");

        url.ShouldBe("https://fonts.gstatic.com/s/roboto/v51/abc.woff2");
    }

    [Fact]
    public void ExtractFontUrl_OnlyWoffAvailable_FallsBackToWoff()
    {
        const string woffOnly = """
            /* latin */
            @font-face {
              font-family: 'Roboto';
              src: url(https://cdn.example/roboto-latin.woff) format('woff');
            }
            """;

        var url = CssParser.ExtractFontUrl(woffOnly, "latin");

        url.ShouldBe("https://cdn.example/roboto-latin.woff");
    }

    [Fact]
    public void ExtractFontUrl_RequestedSubsetOnlyHasWoff_ReturnsItsWoffNotAnotherSubsetsWoff2()
    {
        const string mixed = """
            /* cyrillic */
            @font-face {
              src: url(https://cdn.example/cyrillic.woff2) format('woff2');
            }
            /* latin */
            @font-face {
              src: url(https://cdn.example/latin.woff) format('woff');
            }
            """;

        var url = CssParser.ExtractFontUrl(mixed, "latin");

        url.ShouldBe("https://cdn.example/latin.woff");
    }

    [Fact]
    public void ExtractFontUrl_FormatHintWinsOverMissingExtension()
    {
        // Some CDNs serve fonts from extension-less URLs; the format() hint identifies them.
        const string noExtensions = """
            /* latin */
            @font-face {
              src: url("https://cdn.example/font?id=1") format("woff"),
                   url("https://cdn.example/font?id=2") format("woff2");
            }
            """;

        var url = CssParser.ExtractFontUrl(noExtensions, "latin");

        url.ShouldBe("https://cdn.example/font?id=2");
    }

    [Fact]
    public void ExtractFontUrl_Woff2VariationsFormat_IsTreatedAsWoff2()
    {
        const string variations = """
            /* latin */
            @font-face {
              src: url(https://cdn.example/a.woff) format('woff'),
                   url(https://cdn.example/b) format('woff2-variations');
            }
            """;

        var url = CssParser.ExtractFontUrl(variations, "latin");

        url.ShouldBe("https://cdn.example/b");
    }

    [Fact]
    public void ExtractFontUrl_OnlyUnsupportedFormats_ReturnsNull()
    {
        const string ttfOnly = """
            /* latin */
            @font-face {
              src: local('Roboto'), url(https://cdn.example/roboto.ttf) format('truetype');
            }
            """;

        var url = CssParser.ExtractFontUrl(ttfOnly, "latin");

        url.ShouldBeNull();
    }

    [Fact]
    public void ExtractFontUrl_UnlabelledSubset_FallsBackToFirstBlock()
    {
        // No /* subset */ comments — should fall back to the first usable block.
        const string unlabelled = """
            @font-face {
              font-family: 'Roboto';
              src: url(https://fonts.bunny.net/roboto/files/roboto-400.woff2) format('woff2'),
                   url(https://fonts.bunny.net/roboto/files/roboto-400.woff) format('woff');
            }
            """;

        var url = CssParser.ExtractFontUrl(unlabelled, "latin");

        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-400.woff2");
    }

    [Fact]
    public void ExtractFontUrl_RequestedSubsetMissing_FallsBackToFirstBlock()
    {
        var url = CssParser.ExtractFontUrl(MultiSubsetCss, "greek");

        // "greek" is not in the CSS, so the first block (cyrillic) is used.
        url.ShouldBe("https://fonts.bunny.net/roboto/files/roboto-cyrillic-400.woff2");
    }
}
