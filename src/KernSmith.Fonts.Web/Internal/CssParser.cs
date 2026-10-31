using System.Text.RegularExpressions;

namespace KernSmith.Fonts.Web.Internal;

/// <summary>
/// Parses <c>@font-face</c> CSS returned by Google-Fonts-style CDNs and extracts
/// the font URL for a requested Unicode subset, preferring WOFF2 over WOFF.
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
/// A source's format comes from its <c>format()</c> hint when present, otherwise from the
/// URL's file extension. WOFF2 is preferred (smaller download); WOFF is the fallback. Other
/// formats (TrueType, OpenType, EOT, SVG) are ignored.
/// </remarks>
internal static class CssParser
{
    /// <summary>
    /// Extracts the font URL for the requested subset from a CSS response.
    /// </summary>
    /// <param name="css">The raw CSS text.</param>
    /// <param name="subset">The desired subset (e.g. "latin"). Case-insensitive.</param>
    /// <returns>
    /// The WOFF2 URL (else the WOFF URL) of the block labelled with the requested subset;
    /// or, if that subset is not labelled in the CSS, the WOFF2 (else WOFF) URL of the first
    /// block that has one; or <c>null</c> if no block offers WOFF2 or WOFF.
    /// </returns>
    public static string? ExtractFontUrl(string css, string subset)
    {
        ArgumentNullException.ThrowIfNull(css);
        ArgumentNullException.ThrowIfNull(subset);

        string? firstUrl = null;

        foreach (var block in EnumerateFontFaceBlocks(css))
        {
            var url = ExtractPreferredUrlFromBlock(block.Body);
            if (url is null)
                continue;

            firstUrl ??= url;

            if (block.Subset is not null
                && string.Equals(block.Subset, subset, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
        }

        // Subset not labelled (or not found) — fall back to the first usable block.
        return firstUrl;
    }

    /// <summary>
    /// Splits the CSS into <c>@font-face</c> blocks, capturing the preceding
    /// <c>/* subset */</c> comment label when present.
    /// </summary>
    private static IEnumerable<(string? Subset, string Body)> EnumerateFontFaceBlocks(string css)
    {
        foreach (Match m in FontFaceRegex.Matches(css))
        {
            var subset = m.Groups["subset"].Success
                ? m.Groups["subset"].Value.Trim()
                : null;
            yield return (subset, m.Groups["body"].Value);
        }
    }

    /// <summary>
    /// Returns the first WOFF2 URL in a font-face body, else the first WOFF URL, else null.
    /// </summary>
    private static string? ExtractPreferredUrlFromBlock(string body)
    {
        string? woff = null;

        foreach (Match m in SourceRegex.Matches(body))
        {
            var url = m.Groups["url"].Value.Trim();
            var format = m.Groups["format"].Success
                ? FormatFromHint(m.Groups["format"].Value)
                : FormatFromExtension(url);

            if (format == SourceFormat.Woff2)
                return url;
            if (format == SourceFormat.Woff)
                woff ??= url;
        }

        return woff;
    }

    private enum SourceFormat { Other, Woff, Woff2 }

    /// <summary>Maps a CSS <c>format()</c> hint, e.g. <c>woff2</c> or <c>woff2-variations</c>.</summary>
    private static SourceFormat FormatFromHint(string hint)
    {
        var h = hint.Trim();
        if (h.StartsWith("woff2", StringComparison.OrdinalIgnoreCase))
            return SourceFormat.Woff2;
        if (h.StartsWith("woff", StringComparison.OrdinalIgnoreCase))
            return SourceFormat.Woff;
        return SourceFormat.Other;
    }

    private static SourceFormat FormatFromExtension(string url)
    {
        // Strip an optional trailing query/fragment for the extension check.
        var pathEnd = url.IndexOfAny(['?', '#']);
        var path = pathEnd >= 0 ? url[..pathEnd] : url;

        if (path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
            return SourceFormat.Woff2;
        if (path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
            return SourceFormat.Woff;
        return SourceFormat.Other;
    }

    // The CSS is fetched from a remote CDN, so it is untrusted input. Both patterns run on the
    // NonBacktracking engine: matching is linear in the input, so hostile CSS cannot trigger
    // catastrophic backtracking ("url(" followed by 20,000 spaces did not finish in 30 s on
    // the backtracking engine). Neither pattern uses backreferences, lookarounds or atomic groups,
    // and the matches are the same as the backtracking engine's. Where a lazy group meets a
    // \s*, the two engines can split the whitespace differently, so every group value is
    // trimmed before use. These are plain runtime Regex instances rather than [GeneratedRegex]:
    // the source generator does not support NonBacktracking, and runtime Regex is
    // trim/NativeAOT/WASM safe.

    // Optional "/* latin */" comment immediately before an @font-face rule, then the rule body.
    private static readonly Regex FontFaceRegex = new(
        @"(?:/\*\s*(?<subset>[^*]+?)\s*\*/\s*)?@font-face\s*\{(?<body>[^}]*)\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking);

    // url(...) with optional single/double quotes, then an optional format(...) hint.
    private static readonly Regex SourceRegex = new(
        @"url\(\s*['""]?(?<url>[^'""\)]+?)['""]?\s*\)(?:\s*format\(\s*['""]?(?<format>[^'""\)]+?)['""]?\s*\))?",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
}
