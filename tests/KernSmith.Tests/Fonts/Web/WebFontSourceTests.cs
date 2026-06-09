using System.Net;
using System.Text;
using KernSmith.Font;
using KernSmith.Fonts.Web;
using Shouldly;

namespace KernSmith.Tests.Fonts.Web;

public class WebFontSourceTests
{
    private const string LatinCss = """
        /* latin */
        @font-face {
          font-family: 'Roboto';
          font-style: normal;
          font-weight: 400;
          src: url(https://cdn.example/roboto-latin-400.woff2) format('woff2'),
               url(https://cdn.example/roboto-latin-400.woff) format('woff');
        }
        """;

    private static readonly byte[] FakeWoffBytes = [0x77, 0x4F, 0x46, 0x46, 1, 2, 3, 4]; // "wOFF" + filler

    [Fact]
    public async Task GetFontAsync_FetchesCssThenWoff_ReturnsWoffBytes()
    {
        var handler = new StubHandler(LatinCss, FakeWoffBytes);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        var bytes = await source.GetFontAsync("Roboto", weight: 400);

        bytes.ShouldBe(FakeWoffBytes);
        handler.RequestedUrls.Count.ShouldBe(2);
        handler.RequestedUrls[0].ShouldStartWith("https://cdn.example/css?family=Roboto:400");
        handler.RequestedUrls[1].ShouldBe("https://cdn.example/roboto-latin-400.woff");
    }

    [Fact]
    public async Task GetFontAsync_SecondCall_ReturnsCachedBytesWithoutHttp()
    {
        var handler = new StubHandler(LatinCss, FakeWoffBytes);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        var first = await source.GetFontAsync("Roboto");
        var countAfterFirst = handler.RequestedUrls.Count;
        var second = await source.GetFontAsync("Roboto");

        second.ShouldBeSameAs(first);
        countAfterFirst.ShouldBe(2);
        // No additional HTTP requests on the cached call.
        handler.RequestedUrls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetFontAsync_ItalicStyle_RequestsItalicSpec()
    {
        var handler = new StubHandler(LatinCss, FakeWoffBytes);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        await source.GetFontAsync("Roboto", weight: 700, style: FontStyle.Italic);

        handler.RequestedUrls[0].ShouldContain("family=Roboto:ital,700");
    }

    [Fact]
    public async Task GetFontAsync_NoWoffInCss_Throws()
    {
        const string woff2Only = """
            /* latin */
            @font-face { src: url(https://cdn.example/roboto.woff2) format('woff2'); }
            """;
        var handler = new StubHandler(woff2Only, FakeWoffBytes);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await source.GetFontAsync("Roboto"));
    }

    [Fact]
    public async Task GetFontAsync_EmptyFamily_Throws()
    {
        var handler = new StubHandler(LatinCss, FakeWoffBytes);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        await Should.ThrowAsync<ArgumentException>(
            async () => await source.GetFontAsync(""));
    }

    [Fact]
    public async Task ListFamiliesAsync_ReturnsEmptyList()
    {
        using var source = new WebFontSource(WebFontProvider.BunnyFonts);

        var families = await source.ListFamiliesAsync();

        families.ShouldBeEmpty();
    }

    /// <summary>
    /// Returns the canned CSS for any *.woff2/css-ish request, and the canned WOFF bytes
    /// for any request whose URL ends in .woff.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _css;
        private readonly byte[] _woff;

        public List<string> RequestedUrls { get; } = [];

        public StubHandler(string css, byte[] woff)
        {
            _css = css;
            _woff = woff;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            HttpResponseMessage response;
            if (url.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_woff)
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_css, Encoding.UTF8, "text/css")
                };
            }

            return Task.FromResult(response);
        }
    }
}
