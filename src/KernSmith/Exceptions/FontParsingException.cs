namespace KernSmith;

/// <summary>
/// Thrown when a font file can't be read -- for example, a corrupt or unsupported TTF/OTF.
/// </summary>
public class FontParsingException : BmFontException
{
    /// <summary>Creates a parsing error with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public FontParsingException(string message) : base(message) { }

    /// <summary>Creates a parsing error with a message and the original exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying exception.</param>
    public FontParsingException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates a parsing error at a specific location in a font table.</summary>
    /// <param name="tableTag">Four-character table tag like "GPOS" or "kern".</param>
    /// <param name="offset">Byte offset where the error happened.</param>
    /// <param name="details">What went wrong.</param>
    public FontParsingException(string tableTag, int offset, string details)
        : base($"Error parsing table '{tableTag}' at offset {offset}: {details}")
    {
        TableTag = tableTag;
        Offset = offset;
    }

    /// <summary>Which font table had the error, if known (e.g., "GPOS").</summary>
    public string? TableTag { get; }

    /// <summary>Byte offset in the file where the error happened, if known.</summary>
    public int? Offset { get; }
}
