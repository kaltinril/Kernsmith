using KernSmith.Font;
using KernSmith.Fonts.Web.Internal;

namespace KernSmith.Fonts.Web;

/// <summary>
/// An <see cref="IFontSource"/> that fetches WOFF fonts from CSS-based font CDNs
/// (Bunny Fonts, Google Fonts, or any compatible custom endpoint).
/// </summary>
/// <remarks>
/// The fetch flow is:
/// <list type="number">
///   <item><description>Build the CSS request URL from the provider, family, weight, and style.</description></item>
///   <item><description>GET the CSS (<c>text/css</c>) response.</description></item>
///   <item><description>Parse the <c>@font-face</c> rules and extract the <c>.woff</c> URL for the requested subset.</description></item>
///   <item><description>GET the <c>.woff</c> file (binary).</description></item>
///   <item><description>Cache the bytes (keyed by family + weight + style + subset) and return them.</description></item>
/// </list>
/// Only <c>.woff</c> (not <c>.woff2</c>) is requested, because WOFF2 uses Brotli
/// compression which KernSmith does not yet decode (Phase 178).
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
    {
        _provider = provider ?? WebFontProvider.BunnyFonts;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _cache = new WebFontCache(diskCachePath);

        // CDNs serve a different CSS payload (and font format) per User-Agent. Without a
        // browser-like UA, Google Fonts returns TTF instead of the WOFF we want.
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
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

        var woffUrl = CssParser.ExtractWoffUrl(css, subset)
            ?? throw new InvalidOperationException(
                $"No .woff URL found for font '{family}' (weight {weight}, subset '{subset}') "
                + $"from {_provider.Name}. The CDN may only offer .woff2 for this font, which is "
                + "not yet supported.");

        var bytes = await _httpClient.GetByteArrayAsync(woffUrl, cancellationToken).ConfigureAwait(false);
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
