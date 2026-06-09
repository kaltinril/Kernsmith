using System.Text.RegularExpressions;

namespace KernSmith.Fonts.Web.Internal;

/// <summary>
/// Parses <c>@font-face</c> CSS returned by Google-Fonts-style CDNs and extracts
/// the <c>.woff</c> URL for a requested Unicode subset.
/// </summary>
/// <remarks>
/// Google Fonts and Bunny Fonts emit one <c>@font-face</c> block per subset, each
/// preceded by a comment naming the subset, e.g.:
/// <code>
/// /* latin */
/// @font-face {
///   font-family: 'Roboto';
///   src: url(https://.../roboto.woff2) format('woff2'),
///        url(https://.../roboto.woff) format('woff');
/// }
/// </code>
/// We deliberately extract only <c>.woff</c> URLs (never <c>.woff2</c>) because WOFF2
/// uses Brotli compression which is not yet supported (Phase 178).
/// </remarks>
internal static partial class CssParser
{
    /// <summary>
    /// Extracts the <c>.woff</c> URL for the requested subset from a CSS response.
    /// </summary>
    /// <param name="css">The raw CSS text.</param>
    /// <param name="subset">The desired subset (e.g. "latin"). Case-insensitive.</param>
    /// <returns>
    /// The <c>.woff</c> URL for the requested subset; or the first available
    /// <c>.woff</c> URL if the subset is not labelled in the CSS; or <c>null</c>
    /// if no <c>.woff</c> URL exists at all.
    /// </returns>
    public static string? ExtractWoffUrl(string css, string subset)
    {
        ArgumentNullException.ThrowIfNull(css);
        ArgumentNullException.ThrowIfNull(subset);

        string? firstWoff = null;

        foreach (var block in EnumerateFontFaceBlocks(css))
        {
            var woff = ExtractWoffFromBlock(block.Body);
            if (woff is null)
                continue;

            firstWoff ??= woff;

            if (block.Subset is not null
                && string.Equals(block.Subset, subset, StringComparison.OrdinalIgnoreCase))
            {
                return woff;
            }
        }

        // Subset not labelled (or not found) — fall back to the first .woff we saw.
        return firstWoff;
    }

    /// <summary>
    /// Splits the CSS into <c>@font-face</c> blocks, capturing the preceding
    /// <c>/* subset */</c> comment label when present.
    /// </summary>
    private static IEnumerable<(string? Subset, string Body)> EnumerateFontFaceBlocks(string css)
    {
        foreach (Match m in FontFaceRegex().Matches(css))
        {
            var subset = m.Groups["subset"].Success
                ? m.Groups["subset"].Value.Trim()
                : null;
            yield return (subset, m.Groups["body"].Value);
        }
    }

    /// <summary>Extracts the first <c>.woff</c> (not <c>.woff2</c>) URL from a font-face body.</summary>
    private static string? ExtractWoffFromBlock(string body)
    {
        foreach (Match m in UrlRegex().Matches(body))
        {
            var url = m.Groups["url"].Value.Trim();
            // Strip an optional trailing query/fragment for the extension check.
            var pathEnd = url.IndexOfAny(['?', '#']);
            var path = pathEnd >= 0 ? url[..pathEnd] : url;

            if (path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
                return url;
        }
        return null;
    }

    // Optional "/* latin */" comment immediately before an @font-face rule, then the rule body.
    [GeneratedRegex(
        @"(?:/\*\s*(?<subset>[^*]+?)\s*\*/\s*)?@font-face\s*\{(?<body>[^}]*)\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FontFaceRegex();

    // url(...) with optional single/double quotes.
    [GeneratedRegex(
        @"url\(\s*['""]?(?<url>[^'""\)]+?)['""]?\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
