namespace KernSmith.Font.Tables;

/// <summary>
/// A preset style in a variable font, like "Bold" or "Light Condensed".
/// </summary>
/// <param name="Name">Display name, like "Bold" or "Light".</param>
/// <param name="Coordinates">Axis values for this preset. For example, "wght" = 700 for bold.</param>
public sealed record NamedInstance(
    string? Name,
    IReadOnlyDictionary<string, float> Coordinates
);
