namespace KernSmith.Font.Tables;

/// <summary>
/// Human-readable strings from the font's 'name' table: family, style, copyright, etc.
/// </summary>
/// <param name="FontFamily">Family name like "Roboto" or "Arial".</param>
/// <param name="FontSubfamily">Style name like "Regular" or "Bold".</param>
/// <param name="FullName">Full display name like "Roboto Bold Italic".</param>
/// <param name="PostScriptName">PostScript name (used by PDF renderers and such).</param>
/// <param name="Copyright">Copyright text, if the font includes one.</param>
/// <param name="Trademark">Trademark text, if the font includes one.</param>
public sealed record NameInfo(
    string? FontFamily,
    string? FontSubfamily,
    string? FullName,
    string? PostScriptName,
    string? Copyright,
    string? Trademark);
