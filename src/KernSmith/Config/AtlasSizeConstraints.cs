namespace KernSmith;

/// <summary>
/// Constraints applied to atlas dimensions after size estimation.
/// </summary>
public sealed class AtlasSizeConstraints
{
    /// <summary>If true, the atlas must be square (width == height).</summary>
    public bool ForceSquare { get; set; }

    /// <summary>If true, both dimensions are rounded up to powers of two.</summary>
    public bool ForcePowerOfTwo { get; set; }

    /// <summary>If greater than zero, locks the atlas width to this value and recalculates height.</summary>
    public int FixedWidth { get; set; }
}
