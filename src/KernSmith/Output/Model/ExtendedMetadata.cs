namespace KernSmith.Output.Model;

/// <summary>
/// KernSmith-specific metadata for generation settings that are not part of the standard BMFont format.
/// </summary>
public sealed class ExtendedMetadata
{
    /// <summary>KernSmith library version that generated this font.</summary>
    public required string GeneratorVersion { get; init; }

    /// <summary>SDF spread value, if SDF mode was used.</summary>
    public int? SdfSpread { get; init; }

    /// <summary>Outline thickness, if outline post-processor was used.</summary>
    public float? OutlineThickness { get; init; }

    /// <summary>Gradient top/start color as hex RGB (e.g. "FFD700"), if gradient was used.</summary>
    public string? GradientTopColor { get; init; }

    /// <summary>Gradient bottom/end color as hex RGB (e.g. "DC143C"), if gradient was used.</summary>
    public string? GradientBottomColor { get; init; }

    /// <summary>Shadow X offset, if shadow post-processor was used.</summary>
    public int? ShadowOffsetX { get; init; }

    /// <summary>Shadow Y offset, if shadow post-processor was used.</summary>
    public int? ShadowOffsetY { get; init; }

    /// <summary>Shadow color as hex RGB (e.g. "000000"), if shadow was used.</summary>
    public string? ShadowColor { get; init; }

    /// <summary>Super sampling level, if super sampling was used.</summary>
    public int? SuperSampleLevel { get; init; }

    /// <summary>Variable font axis values used during generation.</summary>
    public IReadOnlyDictionary<string, float>? VariationAxes { get; init; }

    /// <summary>True if color font rendering (COLR/CPAL) was enabled during generation.</summary>
    public bool? ColorFont { get; init; }

    /// <summary>Unicode codepoint of the fallback character for missing glyphs, if configured.</summary>
    public int? FallbackCharacter { get; init; }

    /// <summary>
    /// Returns true if any extended field (beyond GeneratorVersion) is set.
    /// </summary>
    internal bool HasExtendedFields =>
        SdfSpread.HasValue ||
        OutlineThickness.HasValue ||
        GradientTopColor != null ||
        GradientBottomColor != null ||
        ShadowOffsetX.HasValue ||
        ShadowOffsetY.HasValue ||
        ShadowColor != null ||
        SuperSampleLevel.HasValue ||
        VariationAxes is { Count: > 0 } ||
        ColorFont is true ||
        FallbackCharacter.HasValue;
}
