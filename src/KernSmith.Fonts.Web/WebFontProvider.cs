namespace KernSmith.Fonts.Web;

/// <summary>
/// Identifies a CSS-based web font CDN and how to build its stylesheet request URL.
/// </summary>
/// <remarks>
/// Use the built-in <see cref="BunnyFonts"/> or <see cref="GoogleFonts"/> providers,
/// or <see cref="Custom(string)"/> to point at any CDN that exposes a Google-Fonts-style
/// <c>?family={family}:{weight}</c> CSS endpoint.
/// </remarks>
public sealed class WebFontProvider
{
    /// <summary>
    /// Bunny Fonts — a privacy-friendly, drop-in Google Fonts replacement
    /// (<c>https://fonts.bunny.net/css</c>).
    /// </summary>
    public static WebFontProvider BunnyFonts { get; } =
        new("BunnyFonts", "https://fonts.bunny.net/css");

    /// <summary>
    /// Google Fonts (<c>https://fonts.googleapis.com/css</c>).
    /// </summary>
    public static WebFontProvider GoogleFonts { get; } =
        new("GoogleFonts", "https://fonts.googleapis.com/css");

    /// <summary>Human-readable provider name.</summary>
    public string Name { get; }

    /// <summary>Base CSS endpoint URL (without query string).</summary>
    public string CssBaseUrl { get; }

    private WebFontProvider(string name, string cssBaseUrl)
    {
        Name = name;
        CssBaseUrl = cssBaseUrl;
    }

    /// <summary>
    /// Creates a custom provider for a CDN that exposes a Google-Fonts-style CSS endpoint.
    /// </summary>
    /// <param name="cssBaseUrl">
    /// The CSS endpoint base URL, e.g. <c>https://fonts.bunny.net/css</c>. The query string
    /// (<c>?family=...</c>) is appended by <see cref="WebFontSource"/>.
    /// </param>
    public static WebFontProvider Custom(string cssBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(cssBaseUrl);
        if (cssBaseUrl.Length == 0)
            throw new ArgumentException("Base URL must not be empty.", nameof(cssBaseUrl));
        return new WebFontProvider("Custom", cssBaseUrl);
    }

    /// <summary>
    /// Builds the CSS stylesheet request URL for the given family, weight, and style.
    /// </summary>
    internal string BuildCssUrl(string family, int weight, bool italic)
    {
        // Google Fonts / Bunny Fonts CSS v1 syntax: family=Name:{wght}[italic]
        // e.g. ?family=Roboto:400  or  ?family=Roboto:700italic
        // Not "ital,700": both CDNs read that as two variants (italic 400 + normal 700). The
        // CSS2 tuple form (ital,wght@1,700) is only understood by the /css2 endpoint.
        var encodedFamily = Uri.EscapeDataString(family).Replace("%20", "+");
        var weightText = weight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var spec = italic ? $"{weightText}italic" : weightText;
        return $"{CssBaseUrl}?family={encodedFamily}:{spec}";
    }
}
