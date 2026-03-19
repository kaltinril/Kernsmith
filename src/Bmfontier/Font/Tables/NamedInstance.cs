namespace Bmfontier.Font.Tables;

public sealed record NamedInstance(
    string? Name,                                     // e.g., "Bold", "Light"
    IReadOnlyDictionary<string, float> Coordinates    // tag -> value
);
