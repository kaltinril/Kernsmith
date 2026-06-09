namespace KernSmith;

/// <summary>
/// Thrown when a font file's binary structure is invalid -- for example, a corrupt or
/// truncated table directory, an unsupported sfnt version, or out-of-bounds table offsets.
/// </summary>
public class FontFormatException : BmFontException
{
    /// <summary>Creates a format error with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public FontFormatException(string message) : base(message) { }

    /// <summary>Creates a format error with a message and the original exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying exception.</param>
    public FontFormatException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates a format error at a specific location in a font table.</summary>
    /// <param name="tableTag">Four-character table tag like "head" or "cmap".</param>
    /// <param name="offset">Byte offset where the error happened.</param>
    /// <param name="details">What went wrong.</param>
    public FontFormatException(string tableTag, int offset, string details)
        : base($"Error reading table '{tableTag}' at offset {offset}: {details}")
    {
        TableTag = tableTag;
        Offset = offset;
    }

    /// <summary>Which font table had the error, if known (e.g., "cmap").</summary>
    public string? TableTag { get; }

    /// <summary>Byte offset in the file where the error happened, if known.</summary>
    public int? Offset { get; }
}
