using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.Services;

public class FontDiscoveryService
{
    private IReadOnlyList<SystemFontGroup>? _cachedFonts;

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
