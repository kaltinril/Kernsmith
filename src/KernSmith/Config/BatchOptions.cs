namespace KernSmith;

/// <summary>
/// Options controlling batch font generation behavior.
/// </summary>
public sealed class BatchOptions
{
    /// <summary>Max parallel jobs. Default 1 (sequential). 0 = Environment.ProcessorCount.</summary>
    public int MaxParallelism { get; init; } = 1;

    /// <summary>Optional shared font cache. If null, a temporary cache is created internally.</summary>
    public FontCache? FontCache { get; init; }

    /// <summary>Whether to generate separate or combined atlas textures. Default is Separate.</summary>
    public BatchAtlasMode AtlasMode { get; init; } = BatchAtlasMode.Separate;

    /// <summary>Maximum combined atlas texture width when using <see cref="BatchAtlasMode.Combined"/>.</summary>
    public int CombinedMaxTextureWidth { get; init; } = 4096;

    /// <summary>Maximum combined atlas texture height when using <see cref="BatchAtlasMode.Combined"/>.</summary>
    public int CombinedMaxTextureHeight { get; init; } = 4096;
}
