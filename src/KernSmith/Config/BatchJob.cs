namespace KernSmith;

/// <summary>
/// Describes a single font generation job within a batch.
/// Exactly one font source (FontData, FontPath, or SystemFont) should be specified.
/// </summary>
public sealed class BatchJob
{
    /// <summary>Raw font data bytes. Takes priority over FontPath and SystemFont.</summary>
    public byte[]? FontData { get; init; }

    /// <summary>Path to a font file on disk.</summary>
    public string? FontPath { get; init; }

    /// <summary>System font family name (e.g., "Arial").</summary>
    public string? SystemFont { get; init; }

    /// <summary>Generation options for this job.</summary>
    public required FontGeneratorOptions Options { get; init; }
}
