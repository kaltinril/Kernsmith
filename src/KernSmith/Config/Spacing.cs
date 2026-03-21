namespace KernSmith;

/// <summary>
/// Gap between glyphs in the atlas, in pixels.
/// </summary>
public readonly record struct Spacing(int Horizontal, int Vertical)
{
    /// <summary>Same spacing in both directions.</summary>
    public Spacing(int both) : this(both, both) { }

    /// <summary>No spacing in either direction.</summary>
    public static Spacing Zero => new(0, 0);
}
