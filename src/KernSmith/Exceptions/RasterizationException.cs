namespace KernSmith;

/// <summary>
/// Thrown when a glyph can't be rendered from the font.
/// </summary>
public class RasterizationException : BmFontException
{
    /// <summary>Creates a rasterization error with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public RasterizationException(string message) : base(message) { }

    /// <summary>Creates a rasterization error with a message and the original exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying exception.</param>
    public RasterizationException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates a rasterization error for a specific character that failed.</summary>
    /// <param name="codepoint">Unicode character code that failed (e.g., 0x0041 for 'A').</param>
    /// <param name="details">What went wrong.</param>
    public RasterizationException(int codepoint, string details)
        : base($"Error rasterizing glyph for codepoint U+{codepoint:X4}: {details}")
    {
        Codepoint = codepoint;
    }

    /// <summary>Unicode character code that failed to render, if known.</summary>
    public int? Codepoint { get; }
}
