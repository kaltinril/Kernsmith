namespace KernSmith;

/// <summary>
/// Defines padding around each glyph in the atlas, in pixels.
/// </summary>
public readonly record struct Padding(int Up, int Right, int Down, int Left)
{
    /// <summary>Creates uniform padding on all sides.</summary>
    public Padding(int all) : this(all, all, all, all) { }

    public static Padding Zero => new(0, 0, 0, 0);
}
