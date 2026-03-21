namespace KernSmith;

/// <summary>
/// Which algorithm to use for fitting glyphs into atlas pages.
/// </summary>
public enum PackingAlgorithm
{
    /// <summary>MaxRects best short-side fit (default). Best overall packing density.</summary>
    MaxRects,

    /// <summary>Skyline bottom-left. Faster, works well when glyphs are similar sizes.</summary>
    Skyline
}
