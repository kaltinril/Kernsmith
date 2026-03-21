namespace KernSmith.Font.Tables;

/// <summary>
/// A variation axis of a variable font, such as weight or width.
/// </summary>
/// <param name="Tag">Four-character axis tag: "wght" for weight, "wdth" for width, "ital" for italic, etc.</param>
/// <param name="MinValue">Minimum allowed value (e.g., 100 for thin weight).</param>
/// <param name="DefaultValue">Default value used when no override is specified.</param>
/// <param name="MaxValue">Maximum allowed value (e.g., 900 for black weight).</param>
/// <param name="Name">Friendly name like "Weight" or "Width", if the font provides one.</param>
public sealed record VariationAxis(
    string Tag,
    float MinValue,
    float DefaultValue,
    float MaxValue,
    string? Name
);
