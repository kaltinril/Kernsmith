namespace Bmfontier.Font.Tables;

public sealed record NameInfo(
    string? FontFamily,
    string? FontSubfamily,
    string? FullName,
    string? PostScriptName,
    string? Copyright,
    string? Trademark);
