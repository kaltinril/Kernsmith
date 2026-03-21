using KernSmith.Font.Models;

namespace KernSmith.Font;

/// <summary>
/// Provides discovery and loading of fonts installed on the system.
/// </summary>
public interface ISystemFontProvider
{
    /// <summary>
    /// Returns a list of all installed fonts discovered on the system.
    /// </summary>
    IReadOnlyList<SystemFontInfo> GetInstalledFonts();

    /// <summary>
    /// Loads the raw font file bytes for the specified font family.
    /// </summary>
    /// <param name="familyName">The font family name to search for (case-insensitive).</param>
    /// <param name="styleName">
    /// Optional style name to match (e.g., "Bold"). If null, prefers "Regular".
    /// </param>
    /// <returns>The raw font file bytes, or null if no matching font was found.</returns>
    byte[]? LoadFont(string familyName, string? styleName = null);
}
