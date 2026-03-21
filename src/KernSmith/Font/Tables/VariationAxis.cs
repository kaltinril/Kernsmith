namespace KernSmith.Font.Tables;

public sealed record VariationAxis(
    string Tag,           // e.g., "wght", "wdth", "ital"
    float MinValue,
    float DefaultValue,
    float MaxValue,
    string? Name          // resolved from name table
);
