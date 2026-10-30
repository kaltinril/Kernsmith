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

    private const string WoffOnlyCss = """
        /* latin */
        @font-face {
          src: url(https://cdn.example/roboto-latin-400.woff) format('woff');
        }
        """;

    private static readonly byte[] FakeWoffBytes = [0x77, 0x4F, 0x46, 0x46, 1, 2, 3, 4]; // "wOFF" + filler
    private static readonly byte[] FakeWoff2Bytes = [0x77, 0x4F, 0x46, 0x32, 5, 6, 7, 8]; // "wOF2" + filler

    [Fact]
    public async Task GetFontAsync_CssOffersWoff2AndWoff_FetchesWoff2()
    {
        var handler = new StubHandler(LatinCss);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        var bytes = await source.GetFontAsync("Roboto", weight: 400);

        bytes.ShouldBe(FakeWoff2Bytes);
        handler.RequestedUrls.Count.ShouldBe(2);
        handler.RequestedUrls[0].ShouldStartWith("https://cdn.example/css?family=Roboto:400");
        handler.RequestedUrls[1].ShouldBe("https://cdn.example/roboto-latin-400.woff2");
    }

    [Fact]
    public async Task GetFontAsync_CssOffersOnlyWoff_FallsBackToWoff()
    {
        var handler = new StubHandler(WoffOnlyCss);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        var bytes = await source.GetFontAsync("Roboto");

        bytes.ShouldBe(FakeWoffBytes);
        handler.RequestedUrls[1].ShouldBe("https://cdn.example/roboto-latin-400.woff");
    }

    [Fact]
    public async Task GetFontAsync_OutsideBrowser_SendsModernBrowserUserAgent()
    {
        // Google Fonts picks the font format from the User-Agent: unknown agents get TTF,
        // modern Chrome/Firefox/Safari get WOFF2.
        var handler = new StubHandler(LatinCss);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler), diskCachePath: null, runningInBrowser: false);

        await source.GetFontAsync("Roboto");

        handler.UserAgents[0].ShouldContain("Mozilla/5.0", Case.Sensitive);
        handler.UserAgents[0].ShouldContain("Chrome/", Case.Sensitive);
    }

    [Fact]
    public async Task GetFontAsync_InBrowser_LeavesUserAgentToTheBrowser()
    {
        // In browser-wasm a User-Agent set on the HttpClient becomes a fetch() request header.
        // It is not CORS-safelisted, so browsers that honour it (Firefox) send a preflight the
        // CDNs reject. The browser's own UA is already a modern one, so send none.
        var handler = new StubHandler(LatinCss);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler), diskCachePath: null, runningInBrowser: true);

        await source.GetFontAsync("Roboto");

        handler.UserAgents.ShouldAllBe(ua => ua.Length == 0);
    }

    [Fact]
    public async Task GetFontAsync_SecondCall_ReturnsCachedBytesWithoutHttp()
    {
        var handler = new StubHandler(LatinCss);
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
    public async Task GetFontAsync_DiskCache_ServesWoff2AcrossInstancesWithoutHttp()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ks-webfont-{Guid.NewGuid():N}");
        try
        {
            var handler1 = new StubHandler(LatinCss);
            using (var first = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
                       new HttpClient(handler1), diskCachePath: dir))
            {
                await first.GetFontAsync("Roboto");
            }

            Directory.GetFiles(dir, "*.woff2").Length.ShouldBe(1);
            Directory.GetFiles(dir, "*.woff").Length.ShouldBe(0);

            var handler2 = new StubHandler(LatinCss);
            using var second = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
                new HttpClient(handler2), diskCachePath: dir);

            var bytes = await second.GetFontAsync("Roboto");

            bytes.ShouldBe(FakeWoff2Bytes);
            handler2.RequestedUrls.ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetFontAsync_ItalicStyle_RequestsItalicSpec()
    {
        var handler = new StubHandler(LatinCss);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        await source.GetFontAsync("Roboto", weight: 700, style: FontStyle.Italic);

        handler.RequestedUrls[0].ShouldContain("family=Roboto:ital,700");
    }

    [Fact]
    public async Task GetFontAsync_NoWoffOrWoff2InCss_ThrowsNamingBothFormats()
    {
        const string ttfOnly = """
            /* latin */
            @font-face { src: url(https://cdn.example/roboto.ttf) format('truetype'); }
            """;
        var handler = new StubHandler(ttfOnly);
        using var source = new WebFontSource(WebFontProvider.Custom("https://cdn.example/css"),
            new HttpClient(handler));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await source.GetFontAsync("Roboto"));

        ex.Message.ShouldContain(".woff2", Case.Sensitive);
        ex.Message.ShouldNotContain("not yet supported");
    }

    [Fact]
    public async Task GetFontAsync_EmptyFamily_Throws()
    {
        var handler = new StubHandler(LatinCss);
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
    /// Returns the canned WOFF2 bytes for URLs ending in .woff2, the canned WOFF bytes for
    /// URLs ending in .woff, and the canned CSS for anything else.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _css;

        public List<string> RequestedUrls { get; } = [];

        public List<string> UserAgents { get; } = [];

        public StubHandler(string css)
        {
            _css = css;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            UserAgents.Add(request.Headers.UserAgent.ToString());

            HttpContent content;
            if (url.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
                content = new ByteArrayContent(FakeWoff2Bytes);
            else if (url.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
                content = new ByteArrayContent(FakeWoffBytes);
            else
                content = new StringContent(_css, Encoding.UTF8, "text/css");

            return Task.FromResult(Ok(content));
        }

        // HttpClient disposes the response it is handed.
        private static HttpResponseMessage Ok(HttpContent content) => new(HttpStatusCode.OK) { Content = content };
    }
}
