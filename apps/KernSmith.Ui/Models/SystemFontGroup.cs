using KernSmith.Font.Models;

namespace KernSmith.Ui.Models;

/// <summary>
/// Groups all installed style variants (Regular, Bold, Italic, etc.) of a single font family
/// for display in the system font dropdown.
/// </summary>
public class SystemFontGroup
{
    /// <summary>Shared family name, e.g. "Arial".</summary>
    public string FamilyName { get; init; } = "";
    /// <summary>Available style variants for this family, ordered by style name.</summary>
    public IReadOnlyList<SystemFontInfo> Styles { get; init; } = Array.Empty<SystemFontInfo>();
}
