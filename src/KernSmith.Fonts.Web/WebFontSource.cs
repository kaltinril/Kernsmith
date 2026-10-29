using KernSmith.Font;
using KernSmith.Fonts.Web.Internal;

namespace KernSmith.Fonts.Web;

/// <summary>
/// An <see cref="IFontSource"/> that fetches WOFF2/WOFF fonts from CSS-based font CDNs
/// (Bunny Fonts, Google Fonts, or any compatible custom endpoint).
/// </summary>
/// <remarks>
/// The fetch flow is:
/// <list type="number">
///   <item><description>Build the CSS request URL from the provider, family, weight, and style.</description></item>
///   <item><description>GET the CSS (<c>text/css</c>) response.</description></item>
///   <item><description>Parse the <c>@font-face</c> rules and extract the font URL for the requested subset, preferring <c>.woff2</c> and falling back to <c>.woff</c>.</description></item>
///   <item><description>GET the font file (binary).</description></item>
///   <item><description>Cache the bytes (keyed by family + weight + style + subset) and return them.</description></item>
/// </list>
/// The returned bytes are the WOFF2 or WOFF file as served; KernSmith core decodes both
/// (WOFF2 with its own managed Brotli decoder, so this works on browser-wasm too), so they
/// can be passed straight to <c>BmFont.RegisterFont</c> or <c>BmFont.Generate</c>.
/// </remarks>
public sealed class WebFontSource : IFontSource, IDisposable
{
    private readonly WebFontProvider _provider;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly WebFontCache _cache;

    /// <summary>
    /// Creates a web font source for the given provider.
    /// </summary>
    /// <param name="provider">The CDN provider. Defaults to <see cref="WebFontProvider.BunnyFonts"/>.</param>
    /// <param name="httpClient">
    /// An optional <see cref="HttpClient"/> to use for requests. When null, an internally
    /// owned client is created and disposed with this source. Supply your own (e.g. from
    /// <c>IHttpClientFactory</c> or the Blazor WASM DI container) for connection reuse.
    /// </param>
    /// <param name="diskCachePath">
    /// Optional path to a <em>directory</em> (not a file) for persisting fetched fonts across
    /// runs. Pass <c>null</c> to use an in-memory cache only; this must be <c>null</c> on WASM,
    /// which has no filesystem.
    /// </param>
    public WebFontSource(
        WebFontProvider? provider = null,
        HttpClient? httpClient = null,
        string? diskCachePath = null)
        : this(provider, httpClient, diskCachePath, OperatingSystem.IsBrowser())
    {
    }

    /// <summary>Test seam: <paramref name="runningInBrowser"/> stands in for <see cref="OperatingSystem.IsBrowser"/>.</summary>
    internal WebFontSource(
        WebFontProvider? provider,
        HttpClient? httpClient,
        string? diskCachePath,
        bool runningInBrowser)
    {
        _provider = provider ?? WebFontProvider.BunnyFonts;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _cache = new WebFontCache(diskCachePath);

        // CDNs serve a different CSS payload (and font format) per User-Agent. Google Fonts
        // returns TTF to unknown agents and WOFF2 to modern browsers, so send a modern
        // browser UA. Bunny Fonts offers WOFF2 and WOFF to any agent.
        // Not in a browser: there the header would go into fetch(), where it is not
        // CORS-safelisted, so Firefox sends a preflight that the CDNs reject. The browser's
        // own UA already gets WOFF2.
        if (!runningInBrowser && !_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GetFontAsync(
        string family,
        int weight = 400,
        FontStyle style = FontStyle.Normal,
        string subset = "latin",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Length == 0)
            throw new ArgumentException("Family must not be empty.", nameof(family));
        ArgumentNullException.ThrowIfNull(subset);

        var italic = style == FontStyle.Italic;
        var key = WebFontCache.BuildKey(family, weight, italic, subset);
        if (_cache.TryGet(key, out var cached))
            return cached;

        var cssUrl = _provider.BuildCssUrl(family, weight, italic);
        var css = await _httpClient.GetStringAsync(cssUrl, cancellationToken).ConfigureAwait(false);

        var fontUrl = CssParser.ExtractFontUrl(css, subset)
            ?? throw new InvalidOperationException(
                $"No .woff2 or .woff URL found for font '{family}' (weight {weight}, subset '{subset}') "
                + $"from {_provider.Name}. The CSS response has no @font-face source in either format.");

        var bytes = await _httpClient.GetByteArrayAsync(fontUrl, cancellationToken).ConfigureAwait(false);
        _cache.Set(key, bytes);
        return bytes;
    }

    /// <inheritdoc />
    /// <remarks>
    /// CSS-based CDNs do not expose a family-listing endpoint, so this returns an empty list.
    /// Provider-specific listing (e.g. the Google Fonts metadata API) is future work.
    /// </remarks>
    public Task<IReadOnlyList<string>> ListFamiliesAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>Disposes the internally owned <see cref="HttpClient"/>, if any.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
