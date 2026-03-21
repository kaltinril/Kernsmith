using KernSmith.Atlas;

namespace KernSmith.Output;

/// <summary>
/// Aggregated result of a batch font generation run.
/// </summary>
public sealed class BatchResult
{
    /// <summary>Per-job results in the same order as the input jobs.</summary>
    public IReadOnlyList<BatchJobResult> Results { get; init; } = Array.Empty<BatchJobResult>();

    /// <summary>Shared atlas pages when using <see cref="BatchAtlasMode.Combined"/>. Null for Separate mode.</summary>
    public IReadOnlyList<AtlasPage>? SharedPages { get; init; }

    /// <summary>Number of jobs that completed successfully.</summary>
    public int Succeeded => Results.Count(r => r.Success);

    /// <summary>Number of jobs that failed.</summary>
    public int Failed => Results.Count(r => !r.Success);

    /// <summary>Total wall-clock time for the entire batch.</summary>
    public TimeSpan TotalElapsed { get; init; }
}

/// <summary>
/// Result of a single job within a batch.
/// </summary>
public sealed class BatchJobResult
{
    /// <summary>Zero-based index of this job in the input list.</summary>
    public int Index { get; init; }

    /// <summary>Whether the job completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>The generation result, if successful.</summary>
    public BmFontResult? Result { get; init; }

    /// <summary>The exception, if the job failed.</summary>
    public Exception? Error { get; init; }

    /// <summary>Wall-clock time for this individual job.</summary>
    public TimeSpan Elapsed { get; init; }
}
