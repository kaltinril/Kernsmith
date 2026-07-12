namespace KernSmith.Font;

/// <summary>
/// Best-guess candidate file locations for common font families on Windows and macOS,
/// used to lazily pre-populate <see cref="DefaultSystemFontProvider"/>'s per-family
/// resolved-font cache with UNVERIFIED entries before the family has ever actually been
/// resolved.
/// </summary>
/// <remarks>
/// Every seeded entry is validated exactly like any other cache entry the first time it's
/// used (see <c>IsCacheEntryValidCore</c> in <see cref="DefaultSystemFontProvider"/>) — an
/// incorrect or missing guess costs one bounded failed check, then falls straight through to
/// the same registry/full-scan resolution chain that existed before this table. Seed table
/// accuracy only affects performance, never correctness, so entries here don't need to be
/// authoritative for every OS version or locale.
///
/// No Linux table: <c>fc-list</c> already covers the fast path there (see
/// <see cref="DefaultSystemFontProvider"/>'s <c>ScanSystemFonts</c>), so seeding would add
/// nothing.
/// </remarks>
internal static class WellKnownFontSeeds
{
    /// <summary>
    /// Candidate filenames relative to Windows font directories — resolved the same way
    /// <see cref="DefaultSystemFontProvider"/> already resolves <c>%WINDIR%\Fonts</c> and the
    /// per-user font directory. Helvetica is intentionally omitted: it isn't bundled with
    /// Windows and has no plausible default install path there.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> WindowsFileNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = new[] { "arial.ttf" },
            ["Times New Roman"] = new[] { "times.ttf" },
            ["Courier New"] = new[] { "cour.ttf" },
            ["Verdana"] = new[] { "verdana.ttf" },
            ["Georgia"] = new[] { "georgia.ttf" },
            ["Comic Sans MS"] = new[] { "comic.ttf" },
            ["Trebuchet MS"] = new[] { "trebuc.ttf" },
            ["Segoe UI"] = new[] { "segoeui.ttf" },
            ["Tahoma"] = new[] { "tahoma.ttf" },
            ["Calibri"] = new[] { "calibri.ttf" },
            ["Cambria"] = new[] { "cambria.ttc" },
            ["Consolas"] = new[] { "consola.ttf" },
        };

    /// <summary>
    /// Candidate absolute paths on macOS. Segoe UI, Tahoma, Calibri, Cambria, and Consolas
    /// are Microsoft fonts NOT bundled with stock macOS — they're only present if Microsoft
    /// Office is installed, typically under <c>/Library/Fonts/Microsoft/</c>. Those five
    /// entries (plus the exact Arial supplemental-vs-legacy path split) are the
    /// least-confident guesses in this table: the Office install path/filename can vary by
    /// Office version, and none of this has been verified against real macOS hardware. A
    /// miss just falls through to the normal resolution chain, per this table's design —
    /// low-urgency to correct in a follow-up if a real Mac turns up different paths.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> MacPaths =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = new[]
            {
                "/System/Library/Fonts/Supplemental/Arial.ttf", // current macOS
                "/Library/Fonts/Arial.ttf" // older macOS versions
            },
            ["Helvetica"] = new[] { "/System/Library/Fonts/Helvetica.ttc" },
            ["Times New Roman"] = new[] { "/System/Library/Fonts/Supplemental/Times New Roman.ttf" },
            ["Courier New"] = new[] { "/System/Library/Fonts/Supplemental/Courier New.ttf" },
            ["Verdana"] = new[] { "/System/Library/Fonts/Supplemental/Verdana.ttf" },
            ["Georgia"] = new[] { "/System/Library/Fonts/Supplemental/Georgia.ttf" },
            ["Comic Sans MS"] = new[] { "/System/Library/Fonts/Supplemental/Comic Sans MS.ttf" },
            ["Trebuchet MS"] = new[] { "/System/Library/Fonts/Supplemental/Trebuchet MS.ttf" },
            ["Segoe UI"] = new[] { "/Library/Fonts/Microsoft/Segoe UI.ttf" }, // Office-only, low confidence
            ["Tahoma"] = new[] { "/Library/Fonts/Microsoft/Tahoma.ttf" }, // Office-only, low confidence
            ["Calibri"] = new[] { "/Library/Fonts/Microsoft/Calibri.ttf" }, // Office-only, low confidence
            ["Cambria"] = new[] { "/Library/Fonts/Microsoft/Cambria.ttf" }, // Office-only, low confidence
            ["Consolas"] = new[] { "/Library/Fonts/Microsoft/Consolas.ttf" }, // Office-only, low confidence
        };
}
