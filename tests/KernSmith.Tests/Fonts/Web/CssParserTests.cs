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

    [Fact]
    public void ExtractFontUrl_NoFormatHint_UsesUrlExtension()
    {
        // Without format() the extension decides; the query string is ignored for the check.
        const string noHints = """
            /* latin */
            @font-face {
              src: url('https://cdn.example/latin.woff?v=2'),
                   url('https://cdn.example/latin.woff2?v=2#x'),
                   url(https://cdn.example/latin.ttf);
            }
            """;

        var url = CssParser.ExtractFontUrl(noHints, "latin");

        url.ShouldBe("https://cdn.example/latin.woff2?v=2#x");
    }

    [Fact]
    public void ExtractFontUrl_ManyBlocks_SubsetLabelTrimmedAndUnlabelledBlocksSkippedForMatch()
    {
        // Mixed spacing/casing in the rule and comment, an unlabelled block first, and quoted
        // URLs with padding inside url( ) and format( ).
        const string css = """
            @FONT-FACE{src:url(https://cdn.example/unlabelled.woff2)format('woff2')}
            /*   greek-ext   */@font-face {
              src: url(  "https://cdn.example/greek-ext.woff"  ) format(  "woff"  );
            }
            /*latin*/
            @font-face{src:url(https://cdn.example/latin.bin) format('woff2')}
            """;

        CssParser.ExtractFontUrl(css, "greek-ext").ShouldBe("https://cdn.example/greek-ext.woff");
        CssParser.ExtractFontUrl(css, "latin").ShouldBe("https://cdn.example/latin.bin");
        CssParser.ExtractFontUrl(css, "cyrillic").ShouldBe("https://cdn.example/unlabelled.woff2");
    }

    [Fact]
    public void ExtractFontUrl_UnquotedUrlAndHintWithLineBreakPadding_AreTrimmed()
    {
        const string padded = "/*\n latin \n*/\n@font-face {\n  src: url(\n  https://cdn.example/a\n  )\n  format(\n woff2 \n);\n}";

        CssParser.ExtractFontUrl(padded, "latin").ShouldBe("https://cdn.example/a");
    }

    [Fact]
    public void ExtractFontUrl_EmptyOrNoFontFace_ReturnsNull()
    {
        CssParser.ExtractFontUrl("", "latin").ShouldBeNull();
        CssParser.ExtractFontUrl("/* latin */ body { src: url(a.woff2); }", "latin").ShouldBeNull();
        CssParser.ExtractFontUrl("@font-face { src: url(a.woff2)", "latin").ShouldBeNull();
    }

    // The CSS comes from a remote CDN, so it is untrusted input: hostile shapes must not make
    // the parser backtrack catastrophically. Each case is tens of KB and a linear-time parse
    // takes milliseconds; on the backtracking engine the unclosed url( followed by whitespace did
    // not finish within the 30 s timeout.
    [Theory(Timeout = 30_000)]
    [InlineData("unclosed-url-whitespace")]
    [InlineData("many-unclosed-urls")]
    [InlineData("many-unclosed-font-faces")]
    [InlineData("many-unclosed-comments")]
    public async Task ExtractFontUrl_HostileCss_CompletesQuickly(string shape)
    {
        var css = shape switch
        {
            "unclosed-url-whitespace" => "@font-face { src: url(" + new string(' ', 20_000) + "x }",
            "many-unclosed-urls" => "@font-face { src: " + string.Concat(Enumerable.Repeat("url( 'a ", 20_000)) + "}",
            "many-unclosed-font-faces" => string.Concat(Enumerable.Repeat("@font-face { ", 50_000)),
            "many-unclosed-comments" => string.Concat(Enumerable.Repeat("/* a  ", 50_000)) + "@font-face {}",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var elapsed = await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CssParser.ExtractFontUrl(css, "latin").ShouldBeNull();
            return sw.Elapsed;
        }, TestContext.Current.CancellationToken);

        // Generous bound for a busy CI machine; catastrophic backtracking does not finish at all.
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
