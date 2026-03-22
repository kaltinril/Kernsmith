using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.Services;

/// <summary>
/// Discovers system-installed fonts and groups them by family name.
/// Results are cached after the first call for the lifetime of the service.
/// </summary>
public class FontDiscoveryService
{
    private IReadOnlyList<SystemFontGroup>? _cachedFonts;

    /// <summary>
    /// Returns all system fonts grouped by family name and ordered alphabetically.
    /// Caches the result on first call.
    /// </summary>
    public IReadOnlyList<SystemFontGroup> GetSystemFonts()
    {
        if (_cachedFonts != null)
            return _cachedFonts;

        var provider = new DefaultSystemFontProvider();
        var allFonts = provider.GetInstalledFonts();

        _cachedFonts = allFonts
            .GroupBy(f => f.FamilyName)
            .OrderBy(g => g.Key)
            .Select(g => new SystemFontGroup
            {
                FamilyName = g.Key,
                Styles = g.OrderBy(f => f.StyleName).ToList()
            })
            .ToList();

        return _cachedFonts;
    }
}
