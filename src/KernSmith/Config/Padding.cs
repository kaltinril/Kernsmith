namespace KernSmith;

/// <summary>
/// Padding around each glyph in the atlas, in pixels.
/// </summary>
public readonly record struct Padding(int Up, int Right, int Down, int Left)
{
    /// <summary>Same padding on all four sides.</summary>
    public Padding(int all) : this(all, all, all, all) { }

    /// <summary>No padding on any side.</summary>
    public static Padding Zero => new(0, 0, 0, 0);
}
