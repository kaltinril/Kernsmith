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
}
