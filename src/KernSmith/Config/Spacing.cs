namespace KernSmith;

/// <summary>
/// Defines spacing between glyphs in the atlas, in pixels.
/// </summary>
public readonly record struct Spacing(int Horizontal, int Vertical)
{
    /// <summary>Creates uniform spacing in both directions.</summary>
    public Spacing(int both) : this(both, both) { }

    public static Spacing Zero => new(0, 0);
}
