namespace KernSmith;

/// <summary>
/// Base error type for all KernSmith operations. Catch this to handle any bitmap font generation error.
/// </summary>
public class BmFontException : Exception
{
    /// <summary>Creates an error with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public BmFontException(string message) : base(message) { }

    /// <summary>Creates an error with a message and the original exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying exception.</param>
    public BmFontException(string message, Exception inner) : base(message, inner) { }
}
