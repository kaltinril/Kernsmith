using KernSmith.Font.Models;

namespace KernSmith.Ui.Models;

public class SystemFontGroup
{
    public string FamilyName { get; init; } = "";
    public IReadOnlyList<SystemFontInfo> Styles { get; init; } = Array.Empty<SystemFontInfo>();
}
