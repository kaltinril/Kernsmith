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
/// <param name="UniqueId">Unique font identifier (name ID 3).</param>
/// <param name="Version">Version string (name ID 5).</param>
/// <param name="Manufacturer">Font manufacturer name (name ID 8).</param>
/// <param name="Designer">Font designer name (name ID 9).</param>
/// <param name="Description">Font description (name ID 10).</param>
/// <param name="License">License description (name ID 13).</param>
/// <param name="LicenseUrl">License info URL (name ID 14).</param>
public sealed record NameInfo(
    string? FontFamily,
    string? FontSubfamily,
    string? FullName,
    string? PostScriptName,
    string? Copyright,
    string? Trademark,
    string? UniqueId = null,
    string? Version = null,
    string? Manufacturer = null,
    string? Designer = null,
    string? Description = null,
    string? License = null,
    string? LicenseUrl = null);
